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

[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]
[assembly: AssemblyInformationalVersion("1.0.2")]

namespace CalculatorPatch
{
    // Fixes for DSPCalculator 0.5.25, applied to the original mod at runtime with Harmony. The calculator's own DLL is
    // not changed. A build of the calculator that already carries these fixes is left alone.
    [BepInPlugin(Guid, "BUG_CalculatorPatch", "1.0.2")]
    [BepInDependency(Calculator)]
    [BepInDependency("crecheng.DSPModSave")]
    [BepInDependency(OreEnrichment, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Reclamation, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin, IModCanSave
    {
        public const string Guid = "bug.dsp.calculatorpatch", Calculator = "com.GniMaerd.DSPCalculator", OreEnrichment = "bug.dsp.oreenrichment",
            Reclamation = "bug.dsp.trashstation";
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
            var unlocked = new HashSet<int>();
            foreach (var tech in LDB.techs.dataArray)
                if (tech?.UnlockRecipes != null) unlocked.UnionWith(tech.UnlockRecipes);
            ArrangeOwnRecipes(Enrichment, ReclamationLoaded, unlocked, out int dropped, out int moved, out int raw);
            if (Enrichment || ReclamationLoaded)
                Plugin.Log.LogInfo("BUG mods: " + moved + " items list their alternative recipes after the normal ones, " + dropped
                    + " recipes that can never be unlocked left out, " + raw + " Dark Fog items kept as drops (raw)");
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
        // copied recipe's id), dust and enriched smelting (9501-9599; 9600 is another mod's Signal Nexus), the gold and
        // silver chain with Moebius refining, its fixed variants and Dark Fog recycling (9601-9699) and catalysed copies
        // (10000 + id, buildings and fluid chemistry included since OreEnrichment 1.2.0). The calculator takes an item's
        // first recipe as its default and steps to the next one to get out of a loop, so the normal recipes stay first
        // and the alternatives are what the player picks on purpose.
        public static bool IsEnrichmentRecipe(int recipeId) => (recipeId > 7000 && recipeId < 9000) || (recipeId >= 9501 && recipeId <= 9599)
            || (recipeId >= 9601 && recipeId <= 9699) || (recipeId > 10000 && recipeId < 12000);

        // BUG_Reclamation's trash converter (its own recipe type) turns trash points into Dark Fog items. Those items
        // are drops: by default they stay raw, and the converter is the alternative the player picks with X.
        public const int ReclamationType = 97;

        internal static bool Enrichment => Chainloader.PluginInfos.ContainsKey(Plugin.OreEnrichment);
        internal static bool ReclamationLoaded => Chainloader.PluginInfos.ContainsKey(Plugin.Reclamation);
        public static bool IsAlternative(NormalizedRecipe r) => Enrichment && IsEnrichmentRecipe(r.ID) || ReclamationLoaded && r.type == ReclamationType;

        // On a finished CalcDB: drops OreEnrichment's recipes it hid (no grid cell, no tech unlocks them: their product
        // has no normal recipe in this game, e.g. magnets under GenesisBook), lists alternatives after the normal
        // recipes, and keeps items only the converter makes raw by default.
        public static void ArrangeOwnRecipes(bool enrichment, bool reclamation, HashSet<int> unlocked, out int dropped, out int moved, out int raw)
        {
            dropped = moved = raw = 0;
            Func<NormalizedRecipe, bool> alternative = r => enrichment && IsEnrichmentRecipe(r.ID) || reclamation && r.type == ReclamationType;
            if (enrichment)
                foreach (var r in new List<NormalizedRecipe>(CalcDB.recipeDict.Values))
                    if (r != null && IsEnrichmentRecipe(r.ID) && r.oriProto != null && r.oriProto.GridIndex == 0 && !unlocked.Contains(r.ID))
                    {
                        CalcDB.recipeDict.Remove(r.ID);
                        foreach (var item in CalcDB.itemDict.Values) item.recipes.Remove(r);
                        dropped++;
                    }
            if (!enrichment && !reclamation) return;
            foreach (var item in CalcDB.itemDict.Values)
            {
                if (reclamation && item.recipes.Count > 0 && item.recipes.TrueForAll(r => r.type == ReclamationType))
                {
                    item.defaultAsOre = true;
                    raw++;
                }
                var normal = new List<NormalizedRecipe>();
                var alternatives = new List<NormalizedRecipe>();
                foreach (var r in item.recipes) (alternative(r) ? alternatives : normal).Add(r);
                if (alternatives.Count == 0 || normal.Count == 0) continue;  // dust has only its own recipes: order kept
                bool ordered = true;
                for (int i = 0; i < normal.Count; i++) ordered &= item.recipes[i] == normal[i];
                if (ordered) continue;
                item.recipes.Clear();
                item.recipes.AddRange(normal);
                item.recipes.AddRange(alternatives);
                moved++;
            }
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
            Loops.Step();  // called on every step of CalcTree and RemoveOverflow
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
            Loops.Reset();
            if (RecipeUpdates.ValidateSelection(__instance)) return true;
            __result = false;
            return false;
        }

        // After the tree solve, before proliferators: recipe run counts chosen again so surplus is used inside the line.
        [HarmonyPrefix, HarmonyPatch(typeof(SolutionTree), nameof(SolutionTree.CalcProliferator))]
        static void Rebalance(SolutionTree __instance) => Balance.Optimize(__instance);

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
        // A loop that would not settle ends the solve as a failed one (the window keeps its last result).
        [HarmonyFinalizer, HarmonyPatch(typeof(SolutionTree), nameof(SolutionTree.Solve))]
        static Exception SolveLimit(Exception __exception, ref bool __result)
        {
            if (!(__exception is Loops.LimitException)) return __exception;
            Plugin.Log?.LogWarning("Calculator: " + __exception.Message + ", solve stopped");
            __result = false;
            return null;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(SolutionTree), nameof(SolutionTree.Pop))]
        static void CloseLoop(SolutionTree __instance, ref ItemNode __result) => __result = Loops.ClosePopped(__instance, __result);

        [HarmonyPrefix, HarmonyPatch(typeof(UICalcWindow), nameof(UICalcWindow.RefreshProductContent))]
        static void ShowRowsOnce() => Loops.ResetShown();

        [HarmonyTranspiler, HarmonyPatch(typeof(UICalcWindow), nameof(UICalcWindow.RefreshProductContent))]
        static IEnumerable<CodeInstruction> ChildrenOnce(IEnumerable<CodeInstruction> instructions)
        {
            var children = AccessTools.Field(typeof(ItemNode), nameof(ItemNode.children));
            int found = 0;
            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Ldfld && Equals(code.operand, children))
                {
                    found++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Loops), nameof(Loops.ChildrenOnce))).WithLabels(code.labels);
                    continue;
                }
                yield return code;
            }
            if (found != 4) throw new InvalidOperationException("product list walks: " + found + " children reads instead of 4");
        }

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

        [HarmonyPrefix, HarmonyPatch(typeof(UIItemNodeSimple), nameof(UIItemNodeSimple.RemoveThisFromRawOre))]
        static bool RemoveFromRaw(UIItemNodeSimple __instance)
        {
            CalcFixes.RemoveFromRaw(__instance);
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UICalcWindow), nameof(UICalcWindow.RefreshCheckBoxes))]
        static void CheckBoxLabels(UICalcWindow __instance) => CalcFixes.FitCheckBoxLabels(__instance);

        [HarmonyPostfix, HarmonyPatch(typeof(UIItemNode), MethodType.Constructor, typeof(ItemNode), typeof(UICalcWindow))]
        static void IncSwitchInside(UIItemNode __instance)
        {
            CalcFixes.FitIncSwitch(__instance);
            CalcFixes.RightClickToRaw(__instance);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIItemNode), nameof(UIItemNode.RefreshAssemblerDisplay))]
        static void SharedRecipe(UIItemNode __instance) => CalcFixes.MarkSharedRecipe(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(WindowsManager), nameof(WindowsManager.OnUpdate))]
        static void PendingWindows() => CalcFixes.ApplyPendingWindows();
    }
}
