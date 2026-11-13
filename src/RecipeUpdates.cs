using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DSPCalculator;
using DSPCalculator.Compatibility;
using DSPCalculator.Logic;

public static class RecipeUpdates
{
	private static readonly FieldInfo PowerFactor = typeof(RecipeProto).GetField("PowerFactor");

	public static int PickerColumns()
	{
		if (!CompatManager.GB)
		{
			return 14;
		}
		return 17;
	}

	public static int PickerRows()
	{
		if (!CompatManager.GB)
		{
			return 8;
		}
		return 7;
	}

	public static int ProliferatorCount()
	{
		return CalcDB.proliferatorItemIds.Count;
	}

	public static bool ValidRecipe(RecipeProto r)
	{
		if (r == null || r.Items == null || r.ItemCounts == null || r.Results == null || r.ResultCounts == null || r.Items.Length != r.ItemCounts.Length || r.Results.Length != r.ResultCounts.Length || r.Results.Length == 0 || r.TimeSpend <= 0)
		{
			return false;
		}
		for (int i = 0; i < r.Items.Length; i++)
		{
			if (r.ItemCounts[i] <= 0 || LDB.items.Select(r.Items[i]) == null)
			{
				return false;
			}
		}
		for (int j = 0; j < r.Results.Length; j++)
		{
			if (r.ResultCounts[j] <= 0 || LDB.items.Select(r.Results[j]) == null)
			{
				return false;
			}
		}
		if (r.Type == ERecipeType.Fractionate && (r.Items.Length != 1 || r.Results.Length != 1 || r.ResultCounts[0] > r.ItemCounts[0] || r.Items[0] == r.Results[0]))
		{
			return false;
		}
		return true;
	}

	public static bool ContextDependent(RecipeProto r)
	{
		if (CompatManager.GB)
		{
			return r.ID == 429;
		}
		return false;
	}

	public static void CompleteCatalog()
	{
		RecipeProto[] dataArray = LDB.recipes.dataArray;
		foreach (RecipeProto recipeProto in dataArray)
		{
			if (!ValidRecipe(recipeProto) || CalcDB.recipeDict.ContainsKey(recipeProto.ID) || (recipeProto.Results.Length == 1 && recipeProto.Results[0] == 9500) || (recipeProto.Items.Length == 0 && !ContextDependent(recipeProto)))
			{
				continue;
			}
			NormalizedRecipe normalizedRecipe = new NormalizedRecipe(recipeProto);
			CalcDB.recipeDict[recipeProto.ID] = normalizedRecipe;
			int type = (int)recipeProto.Type;
			if (!CalcDB.assemblerListByType.ContainsKey(type))
			{
				CalcDB.assemblerListByType[type] = new List<AssemblerData>();
			}
			ItemProto[] dataArray2 = LDB.items.dataArray;
			foreach (ItemProto itemProto in dataArray2)
			{
				if (itemProto == null)
				{
					continue;
				}
				PrefabDesc prefabDesc = LDB.models.Select(itemProto.ModelIndex)?.prefabDesc;
				if (prefabDesc != null && (prefabDesc.isFractionator || prefabDesc.isAssembler) && (int)prefabDesc.assemblerRecipeType == type)
				{
					if (!CalcDB.assemblerDict.TryGetValue(itemProto.ID, out var value))
					{
						value = new AssemblerData(itemProto, prefabDesc);
						CalcDB.assemblerDict[itemProto.ID] = value;
					}
					if (prefabDesc.isFractionator)
					{
						value.speed = CalcDB.maxBeltItemSpeedPreSec * CalcDB.maxStackSize;
					}
					CalcDB.assemblerListByType[type].Add(value);
				}
			}
			if (ContextDependent(recipeProto))
			{
				int[] products = normalizedRecipe.products;
				foreach (int key in products)
				{
					CalcDB.itemDict[key].defaultAsOre = true;
				}
			}
		}
		foreach (List<AssemblerData> value2 in CalcDB.assemblerListByType.Values)
		{
			HashSet<int> hashSet = new HashSet<int>();
			for (int k = 0; k < value2.Count; k++)
			{
				if (!hashSet.Add(value2[k].ID))
				{
					value2.RemoveAt(k--);
				}
			}
		}
		CalcFixes.AddGBFilterMachines();
		CalcFixes.GBPreferMainOutputRecipes();
		WriteCatalog();
	}

	public static bool ValidateSelection(SolutionTree tree)
	{
		CalcFixes.SyncRecipeTimes();
		CalcFixes.BreakForcedLoops(tree);
		foreach (ItemConfig value3 in tree.userPreference.itemConfigs.Values)
		{
			if (!value3.consideredAsOre && value3.forceNotOre && value3.recipeID > 0 && CalcDB.recipeDict.TryGetValue(value3.recipeID, out var value) && ContextDependent(value.oriProto))
			{
				UIRealtimeTip.Popup("GenesisBook material recovery depends on incoming trash, power and free inventory space; it has no fixed input ratio.");
				return false;
			}
		}
		foreach (ItemConfig value4 in tree.userPreference.itemConfigs.Values)
		{
			if (!value4.consideredAsOre && value4.forceNotOre && value4.recipeID <= 0 && CalcDB.itemDict.TryGetValue(value4.ID, out var value2) && value2.recipes.Count != 0 && ContextDependent(value2.recipes[0].oriProto))
			{
				UIRealtimeTip.Popup("GenesisBook material recovery is a context-dependent recycling process. Supply its output externally.");
				return false;
			}
		}
		return true;
	}

	public static double ExtraPower(RecipeInfo recipe, double existing)
	{
		if (!CompatManager.GB || recipe.useIA || PowerFactor == null)
		{
			return existing;
		}
		int num = (int)PowerFactor.GetValue(recipe.recipeNorm.oriProto);
		if (num < 2)
		{
			return existing;
		}
		ItemProto itemProto = LDB.items.Select(recipe.assemblerItemId);
		PrefabDesc prefabDesc = ((itemProto == null) ? null : LDB.models.Select(itemProto.ModelIndex))?.prefabDesc;
		if (prefabDesc == null || !prefabDesc.isAssembler)
		{
			return existing;
		}
		int incLevel = recipe.incLevel;
		double num2 = ((incLevel >= 0 && incLevel < Cargo.powerTableRatio.Length) ? Utils.GetPowerRatio(incLevel, recipe.userPreference) : 1.0);
		int num3 = prefabDesc.assemblerSpeed;
		if (!recipe.isInc && incLevel >= 0 && incLevel < Cargo.accTableMilli.Length)
		{
			num3 = (int)((double)num3 * (1.0 + Utils.GetAccMilli(incLevel, recipe.userPreference)));
		}
		long num4 = (long)num3 * (long)num / 2;
		return existing + recipe.assemblerCount * (double)num4 * 60.0 * num2;
	}

	public static double FractionatorSpray(RecipeInfo recipe, double existing)
	{
		if (recipe.recipeNorm.type != NormalizedRecipe.factionateType)
		{
			return existing;
		}
		RecipeProto oriProto = recipe.recipeNorm.oriProto;
		return existing / (double)oriProto.ItemCounts[0];
	}

	private static string Join(int[] values)
	{
		string text = "";
		for (int i = 0; i < values.Length; i++)
		{
			text = text + ((i == 0) ? "" : ";") + values[i];
		}
		return text;
	}

	private static void WriteCatalog()
	{
		try
		{
			using StreamWriter streamWriter = new StreamWriter(Path.Combine(Path.GetDirectoryName(typeof(DSPCalculatorPlugin).Assembly.Location), "DSPCalculator-recipes.csv"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			streamWriter.WriteLine("id,name,type,seconds,input_ids,input_counts,output_ids,output_counts,productive,power_factor,in_calculator,context_dependent");
			RecipeProto[] dataArray = LDB.recipes.dataArray;
			foreach (RecipeProto recipeProto in dataArray)
			{
				if (recipeProto != null && recipeProto.Items != null && recipeProto.Results != null)
				{
					string[] obj = new string[23]
					{
						recipeProto.ID.ToString(),
						",\"",
						(recipeProto.name ?? recipeProto.Name ?? "").Replace("\"", "\"\""),
						"\",",
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null,
						null
					};
					int type = (int)recipeProto.Type;
					obj[4] = type.ToString();
					obj[5] = ",";
					obj[6] = ((double)recipeProto.TimeSpend / 60.0).ToString("R", CultureInfo.InvariantCulture);
					obj[7] = ",";
					obj[8] = Join(recipeProto.Items);
					obj[9] = ",";
					obj[10] = Join(recipeProto.ItemCounts ?? new int[0]);
					obj[11] = ",";
					obj[12] = Join(recipeProto.Results);
					obj[13] = ",";
					obj[14] = Join(recipeProto.ResultCounts ?? new int[0]);
					obj[15] = ",";
					obj[16] = recipeProto.productive.ToString();
					obj[17] = ",";
					obj[18] = ((!(PowerFactor == null)) ? ((int)PowerFactor.GetValue(recipeProto)) : 0).ToString();
					obj[19] = ",";
					obj[20] = CalcDB.recipeDict.ContainsKey(recipeProto.ID).ToString();
					obj[21] = ",";
					obj[22] = ContextDependent(recipeProto).ToString();
					streamWriter.WriteLine(string.Concat(obj));
				}
			}
		}
		catch (Exception ex)
		{
			Utils.logger.LogWarning("Could not write recipe audit: " + ex.Message);
		}
	}
}
