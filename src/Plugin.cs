using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using crecheng.DSPModSave;
using DSPCalculator.Compatibility;
using DSPCalculator.Logic;
using DSPCalculator.UI;
using HarmonyLib;

[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.0.1")]

namespace CalculatorPatch
{
    // Fixes for DSPCalculator 0.5.25, applied to the original mod at runtime with Harmony. The calculator's own DLL is
    // not changed. A build of the calculator that already carries these fixes is left alone.
    [BepInPlugin(Guid, "BUG_CalculatorPatch", "1.0.1")]
    [BepInDependency(Calculator)]
    [BepInDependency("crecheng.DSPModSave")]
    [BepInDependency(OreEnrichment, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin, IModCanSave
    {
        public const string Guid = "bug.dsp.calculatorpatch", Calculator = "com.GniMaerd.DSPCalculator", OreEnrichment = "bug.dsp.oreenrichment";
        internal static ManualLogSource Log;
        internal static bool Active;

        void Awake()
        {
            Log = Logger;
            if (typeof(CalcDB).Assembly.GetType("RecipeUpdates") != null)
            {
                Log.LogInfo("DSPCalculator already contains these fixes, nothing patched");
                return;
            }
            string version = Chainloader.PluginInfos.TryGetValue(Calculator, out var info) ? info.Metadata.Version.ToString() : "?";
            var harmony = new Harmony(Guid);
            try
            {
                harmony.PatchAll(typeof(Patches));
                Active = true;
                Log.LogInfo("DSPCalculator " + version + " patched");
            }
            catch (Exception e)
            {
                // All or nothing: half of the fixes would leave the calculator inconsistent.
                harmony.UnpatchSelf();
                Log.LogError("DSPCalculator " + version + " is not the version these fixes were written for (0.5.25); it is left as it is. " + e);
            }
        }

        // Open calculator windows are kept in the save (under this mod's own entry).
        public void Export(BinaryWriter w)
        {
            if (Active) CalcFixes.SaveWindows(w); else w.Write(0);
        }

        public void Import(BinaryReader r)
        {
            if (Active) CalcFixes.LoadWindows(r);
        }

        public void IntoOtherSave()
        {
            if (Active) CalcFixes.ClearPendingWindows();
        }
    }

    public static class Patches
    {
        // ---- CalcDB.TryInit: belts reset, every proliferator the game has, recipes the catalogue skipped ----

        [HarmonyPrefix, HarmonyPatch(typeof(CalcDB), nameof(CalcDB.TryInit))]
        static void BeforeInit() => CalcDB.maxBeltItemSpeedPreSec = 0.0;

        [HarmonyPostfix, HarmonyPatch(typeof(CalcDB), nameof(CalcDB.TryInit))]
        static void AfterInit()
        {
            var ids = NativeProliferatorItems();
            CalcDB.proliferatorItemIds = ids;
            CalcDB.proliferatorAbilitiesMap = new Dictionary<int, int>();
            CalcDB.proliferatorAbilityToId = new Dictionary<int, int>();
            foreach (int id in ids)
            {
                CalcDB.proliferatorAbilitiesMap[id] = 0;
                var item = LDB.items.Select(id);
                if (item == null) continue;
                if (item.Ability > 10) CalcDB.proliferatorAbilitiesMap[id] = 10;
                else if (item.Ability >= 0) CalcDB.proliferatorAbilitiesMap[id] = item.Ability;
                CalcDB.proliferatorAbilityToId[item.Ability] = item.ID;
            }
            RecipeUpdates.CompleteCatalog();
            KeepAlternativesLast();
        }

        // The vanilla three plus any proliferator a spray coater of the loaded game accepts, one per spray level.
        public static List<int> NativeProliferatorItems()
        {
            var list = new List<int> { 1141, 1142, 1143 };
            var levels = new HashSet<int>();
            foreach (int id in list)
            {
                var item = LDB.items.Select(id);
                if (item != null) levels.Add(item.Ability);
            }
            foreach (var model in LDB.models.dataArray)
            {
                int[] sprays = model?.prefabDesc?.incItemId;
                if (sprays == null) continue;
                foreach (int id in sprays)
                {
                    var item = LDB.items.Select(id);
                    if (item != null && item.Ability > 0 && item.Ability <= 10 && item.Ability < Cargo.accTableMilli.Length && item.Ability < Cargo.incTableMilli.Length
                        && item.Ability < Cargo.powerTableRatio.Length && !list.Contains(id) && levels.Add(item.Ability)) list.Add(id);
                }
            }
            return list;
        }

        // BUG_OreEnrichment adds higher-yield alternatives for existing products: gold and silver copies (7000 + the
        // copied recipe's id), dust and enriched smelting (9501-9547), the gold and silver chain and its fixed variants
        // (9601-9699) and catalysed copies (10000 + id). The calculator takes an item's first recipe as its default and
        // steps to the next one to get out of a loop, so the normal recipes stay first and the alternatives are what
        // the player picks on purpose.
        public static bool IsEnrichmentRecipe(int recipeId) => (recipeId > 7000 && recipeId < 9000) || (recipeId >= 9501 && recipeId <= 9547)
            || (recipeId >= 9601 && recipeId <= 9699) || (recipeId > 10000 && recipeId < 12000);

        internal static bool Enrichment => Chainloader.PluginInfos.ContainsKey(Plugin.OreEnrichment);
        public static bool IsAlternative(NormalizedRecipe r) => Enrichment && IsEnrichmentRecipe(r.ID);

        static void KeepAlternativesLast()
        {
            if (!Enrichment) return;
            int moved = 0;
            foreach (var item in CalcDB.itemDict.Values)
            {
                var normal = new List<NormalizedRecipe>();
                var alternatives = new List<NormalizedRecipe>();
                foreach (var r in item.recipes) (IsEnrichmentRecipe(r.ID) ? alternatives : normal).Add(r);
                if (alternatives.Count == 0 || normal.Count == 0) continue;  // dust has only its own recipes: order kept
                bool ordered = true;
                for (int i = 0; i < normal.Count; i++) ordered &= item.recipes[i] == normal[i];
                if (ordered) continue;
                item.recipes.Clear();
                item.recipes.AddRange(normal);
                item.recipes.AddRange(alternatives);
                moved++;
            }
            Plugin.Log.LogInfo("BUG_OreEnrichment found: its alternative recipes are listed after the normal ones (" + moved + " items reordered)");
        }

        // ---- RecipeInfo: output maths (GenesisBook's extra product adds to the proliferator bonus), power, spray ----

        [HarmonyPrefix, HarmonyPatch(typeof(RecipeInfo), nameof(RecipeInfo.CalcCountByOutputSpeed))]
        static bool CountByOutput(RecipeInfo __instance, int itemId, double speed, ref double __result)
        {
            __result = CalcFixes.CalcCountByOutputSpeed(__instance, itemId, speed);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(RecipeInfo), nameof(RecipeInfo.GetOutputSpeedByChangedCount))]
        static bool OutputByCount(RecipeInfo __instance, int itemId, double changedCount, ref double __result)
        {
            __result = CalcFixes.GetOutputSpeedByChangedCount(__instance, itemId, changedCount);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(RecipeInfo), nameof(RecipeInfo.GetOutputSpeedOriProto))]
        static bool OutputOriProto(RecipeInfo __instance, int index, double changedCount, ref double __result)
        {
            __result = CalcFixes.GetOutputSpeedOriProto(__instance, index, changedCount);
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(RecipeInfo), nameof(RecipeInfo.GetTotalEnergyConsumption))]
        static void Power(RecipeInfo __instance, ref double __result) => __result = RecipeUpdates.ExtraPower(__instance, __result);

        [HarmonyPostfix, HarmonyPatch(typeof(RecipeInfo), nameof(RecipeInfo.GetProliferatorUsed))]
        static void Spray(RecipeInfo __instance, ref double proliferatorCount) => proliferatorCount = RecipeUpdates.FractionatorSpray(__instance, proliferatorCount);

        // ---- SolutionTree: selection check before solving, proliferator solve for any number of proliferators ----

        [ThreadStatic] static bool proliferatorLineFailed;

        [HarmonyPrefix, HarmonyPatch(typeof(SolutionTree), nameof(SolutionTree.Solve))]
        static bool BeforeSolve(SolutionTree __instance, ref bool __result)
        {
            proliferatorLineFailed = false;
            if (RecipeUpdates.ValidateSelection(__instance)) return true;
            __result = false;
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(SolutionTree), nameof(SolutionTree.CalcProliferatorInProductLine))]
        static void ProliferatorLine(bool __result) => proliferatorLineFailed |= !__result;

        // The original ignores a failed proliferator solve and reports success.
        [HarmonyPostfix, HarmonyPatch(typeof(SolutionTree), nameof(SolutionTree.Solve))]
        static void AfterSolve(SolutionTree __instance, ref bool __result)
        {
            if (__result && proliferatorLineFailed && __instance.userPreference.solveProliferators) __result = false;
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(SolutionTree), nameof(SolutionTree.CalcProliferatorInProductLine))]
        static IEnumerable<CodeInstruction> ProliferatorCount(IEnumerable<CodeInstruction> instructions)
        {
            int found = 0;
            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Ldc_I4_3)
                {
                    found++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RecipeUpdates), nameof(RecipeUpdates.ProliferatorCount))).WithLabels(code.labels);
                }
                else yield return code;
            }
            if (found != 9) throw new InvalidOperationException("proliferator matrix: " + found + " sizes instead of 9");
        }

        // ---- Recipe picker: GenesisBook's grid is 17 by 7, not 14 by 8 ----

        [HarmonyTranspiler, HarmonyPatch(typeof(RecipePickerPatcher), nameof(RecipePickerPatcher.RecipePickerPrefix))]
        static IEnumerable<CodeInstruction> PickerGrid(IEnumerable<CodeInstruction> instructions)
        {
            int rows = 0, columns = 0;
            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Ldc_I4_8)
                {
                    rows++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RecipeUpdates), nameof(RecipeUpdates.PickerRows))).WithLabels(code.labels);
                }
                else if (code.opcode == OpCodes.Ldc_I4_S && Convert.ToInt32(code.operand) == 14)
                {
                    columns++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RecipeUpdates), nameof(RecipeUpdates.PickerColumns))).WithLabels(code.labels);
                }
                else yield return code;
            }
            if (rows != 1 || columns != 2) throw new InvalidOperationException("recipe picker grid: " + rows + " row and " + columns + " column sizes instead of 1 and 2");
        }

        // ---- Window ----

        // With GenesisBook the interstellar assembly button was forced onto (415, 0), on top of a research machine button.
        [HarmonyTranspiler, HarmonyPatch(typeof(UICalcWindow), MethodType.Constructor, typeof(int))]
        static IEnumerable<CodeInstruction> AssemblyButton(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var gb = AccessTools.Field(typeof(CompatManager), nameof(CompatManager.GB));
            int at = codes.FindIndex(c => c.opcode == OpCodes.Ldc_R4 && c.operand is float f && f == 415f), test = -1;
            for (int i = at; i >= 0 && test < 0; i--)
                if (codes[i].opcode == OpCodes.Ldsfld && Equals(codes[i].operand, gb)) test = i;
            if (at < 0 || test < 0 || at - test > 12) throw new InvalidOperationException("interstellar assembly button placement not found");
            codes[test] = new CodeInstruction(OpCodes.Ldc_I4_0).WithLabels(codes[test].labels);
            return codes;
        }

        // A raw input fully covered by a by-product of the same line is not listed as a demand.
        [HarmonyTranspiler, HarmonyPatch(typeof(UICalcWindow), nameof(UICalcWindow.RefreshProductContent))]
        static IEnumerable<CodeInstruction> ListedNeed(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var need = AccessTools.Field(typeof(ItemNode), nameof(ItemNode.needSpeed));
            int found = 0;
            for (int i = 2; i + 2 < codes.Count; i++)  // only the two list filters, which follow a ContainsKey branch
                if ((codes[i - 2].opcode == OpCodes.Brtrue_S || codes[i - 2].opcode == OpCodes.Brtrue) && codes[i].opcode == OpCodes.Ldfld && Equals(codes[i].operand, need) && codes[i + 1].opcode == OpCodes.Ldc_R8 && codes[i + 2].opcode == OpCodes.Cgt)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CalcFixes), nameof(CalcFixes.ListedNeed))).WithLabels(codes[i].labels);
                    found++;
                }
            if (found != 2) throw new InvalidOperationException("product list demand checks: " + found + " instead of 2");
            return codes;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UICalcWindow), nameof(UICalcWindow.RefreshResourceNeedAndByProductContent))]
        static void Sources(UICalcWindow __instance) => CalcFixes.AddSourcesSection(__instance);

        [HarmonyPostfix, HarmonyPatch(typeof(UIItemNode), MethodType.Constructor, typeof(ItemNode), typeof(UICalcWindow))]
        static void IncSwitchInside(UIItemNode __instance) => CalcFixes.FitIncSwitch(__instance);

        [HarmonyPostfix, HarmonyPatch(typeof(UIItemNode), nameof(UIItemNode.RefreshAssemblerDisplay))]
        static void SharedRecipe(UIItemNode __instance) => CalcFixes.MarkSharedRecipe(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(WindowsManager), nameof(WindowsManager.OnUpdate))]
        static void PendingWindows() => CalcFixes.ApplyPendingWindows();
    }
}
