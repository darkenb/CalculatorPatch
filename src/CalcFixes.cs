using System.Collections.Generic;
using System.IO;
using DSPCalculator;
using DSPCalculator.Compatibility;
using DSPCalculator.Logic;
using DSPCalculator.UI;
using UnityEngine;
using UnityEngine.UI;

public static class CalcFixes
{
	// The proliferator mode switch of a row is a clone of the game's assembler window switch at a fixed spot. With its thumb
	// on the right (Production Speedup) it reached past the visible list and was cut off: the switch is moved left until it
	// fits, thumb on the right, left of the scrollbar.
	public static void FitIncSwitch(UIItemNode ui)
	{
		if (ui == null || ui.incToggleObj == null || ui.parentCalcWindow == null || ui.parentCalcWindow.contentScrollRect == null)
		{
			return;
		}
		Transform toggle = ui.incToggleObj.transform;
		RectTransform track = toggle.Find("inc-switch") as RectTransform;
		RectTransform thumb = toggle.Find("inc-switch/switch-thumb") as RectTransform;
		ScrollRect scroll = ui.parentCalcWindow.contentScrollRect;
		RectTransform view = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
		if (track == null || thumb == null || view == null || toggle.parent == null || toggle.parent.lossyScale.x <= 0f)
		{
			return;
		}
		Vector3[] corners = new Vector3[4];
		view.GetWorldCorners(corners);
		float limit = corners[2].x;
		if (scroll.verticalScrollbar != null && scroll.verticalScrollbar.gameObject.activeInHierarchy)
		{
			((RectTransform)scroll.verticalScrollbar.transform).GetWorldCorners(corners);
			limit = Mathf.Min(limit, corners[0].x);
		}
		track.GetWorldCorners(corners);
		float right = corners[2].x;
		thumb.GetWorldCorners(corners);
		right = Mathf.Max(right, corners[2].x + (10f - thumb.anchoredPosition.x) * thumb.parent.lossyScale.x);
		float over = OverflowLocal(right, limit, toggle.parent.lossyScale.x);
		if (over > 0f)
		{
			toggle.localPosition -= new Vector3(over, 0f, 0f);
		}
	}

	// How far (in the row's units) the switch must move left so its right end stays 8 units inside the limit.
	public static float OverflowLocal(float rightWorld, float limitWorld, float scale)
	{
		return scale <= 0f ? 0f : (rightWorld - limitWorld) / scale + 8f;
	}

	private static HashSet<ItemConfig> autoRaw;

	private static List<string> pendingData;

	private static List<float> pendingX;

	private static List<float> pendingY;

	private static List<bool> pendingLarge;

	private static int pendingClosed;

	public static void SaveWindows(BinaryWriter w)
	{
		List<UICalcWindow> list = new List<UICalcWindow>();
		if (WindowsManager.closedWindows != null)
		{
			foreach (UICalcWindow closedWindow in WindowsManager.closedWindows)
			{
				if (closedWindow.windowObj != null)
				{
					list.Add(closedWindow);
				}
			}
		}
		int count = list.Count;
		if (WindowsManager.windows != null)
		{
			foreach (UICalcWindow window in WindowsManager.windows)
			{
				if (window.windowObj != null)
				{
					list.Add(window);
				}
			}
		}
		List<ItemConfig> list2 = ((autoRaw == null) ? new List<ItemConfig>() : new List<ItemConfig>(autoRaw));
		foreach (ItemConfig item in list2)
		{
			item.consideredAsOre = false;
		}
		try
		{
			w.Write(1);
			w.Write(count);
			w.Write(list.Count);
			foreach (UICalcWindow item2 in list)
			{
				Vector3 localPosition = item2.windowObj.transform.localPosition;
				w.Write(item2.Copy());
				w.Write(localPosition.x);
				w.Write(localPosition.y);
				w.Write(item2.isLargeWindow);
			}
		}
		finally
		{
			foreach (ItemConfig item3 in list2)
			{
				item3.consideredAsOre = true;
			}
		}
	}

	public static void LoadWindows(BinaryReader r)
	{
		pendingData = null;
		if (r.ReadInt32() == 1)
		{
			int num = r.ReadInt32();
			int num2 = r.ReadInt32();
			List<string> list = new List<string>();
			pendingX = new List<float>();
			pendingY = new List<float>();
			pendingLarge = new List<bool>();
			for (int i = 0; i < num2; i++)
			{
				list.Add(r.ReadString());
				pendingX.Add(r.ReadSingle());
				pendingY.Add(r.ReadSingle());
				pendingLarge.Add(r.ReadBoolean());
			}
			pendingClosed = num;
			pendingData = list;
		}
	}

	public static void ClearPendingWindows()
	{
		pendingData = null;
	}

	public static void ApplyPendingWindows()
	{
		if (pendingData == null || !GameMain.isRunning || DSPGame.IsMenuDemo || !CalcDB.inited || WindowsManager.windows == null)
		{
			return;
		}
		List<string> list = pendingData;
		pendingData = null;
		for (int num = WindowsManager.windows.Count - 1; num >= 0; num--)
		{
			WindowsManager.windows[num].CloseWindow();
		}
		foreach (UICalcWindow closedWindow in WindowsManager.closedWindows)
		{
			Object.Destroy(closedWindow.windowObj);
		}
		WindowsManager.closedWindows.Clear();
		for (int i = 0; i < list.Count; i++)
		{
			UICalcWindow uICalcWindow = WindowsManager.OpenOne(forceNewWindow: true);
			if (!uICalcWindow.TryPaste(list[i]))
			{
				Utils.logger.LogWarning("Saved calculator window " + i + " could not be restored.");
			}
			Vector3 localPosition = uICalcWindow.windowObj.transform.localPosition;
			uICalcWindow.windowObj.transform.localPosition = new Vector3(pendingX[i], pendingY[i], localPosition.z);
			uICalcWindow.isLargeWindow = pendingLarge[i];
			if (i < pendingClosed)
			{
				uICalcWindow.CloseWindow();
			}
		}
	}

	private static bool IsRaw(UserPreference pref, int itemId)
	{
		if (!CalcDB.itemDict.TryGetValue(itemId, out var value))
		{
			return true;
		}
		bool flag = value.defaultAsOre || value.recipes.Count == 0;
		if (pref.itemConfigs.TryGetValue(itemId, out var value2))
		{
			flag = value2.consideredAsOre || flag;
			if (value2.forceNotOre && value.recipes.Count > 0)
			{
				flag = false;
			}
		}
		return flag;
	}

	private static bool MustNeed(UserPreference pref, int itemId, int target, HashSet<int> path, HashSet<int> no)
	{
		if (itemId == target)
		{
			return true;
		}
		if (no.Contains(itemId) || IsRaw(pref, itemId) || !path.Add(itemId))
		{
			return false;
		}
		bool flag;
		if (pref.itemConfigs.TryGetValue(itemId, out var value) && value.recipeID > 0 && CalcDB.recipeDict.TryGetValue(value.recipeID, out var value2))
		{
			flag = RecipeNeeds(pref, value2, target, path, no);
		}
		else
		{
			flag = true;
			foreach (NormalizedRecipe recipe in CalcDB.itemDict[itemId].recipes)
			{
				if (!RecipeNeeds(pref, recipe, target, path, no))
				{
					flag = false;
					break;
				}
			}
		}
		path.Remove(itemId);
		if (!flag)
		{
			no.Add(itemId);
		}
		return flag;
	}

	private static bool RecipeNeeds(UserPreference pref, NormalizedRecipe r, int target, HashSet<int> path, HashSet<int> no)
	{
		for (int i = 0; i < r.resources.Length; i++)
		{
			if (r.resourceCounts[i] > 0 && MustNeed(pref, r.resources[i], target, path, no))
			{
				return true;
			}
		}
		return false;
	}

	public static void SyncRecipeTimes()
	{
		if (CalcDB.recipeDict == null)
		{
			return;
		}
		foreach (NormalizedRecipe value in CalcDB.recipeDict.Values)
		{
			if (value != null && value.oriProto != null && value.ID >= 0 && value.type != NormalizedRecipe.factionateType)
			{
				int timeSpend = value.oriProto.TimeSpend;
				if (timeSpend > 0)
				{
					value.time = (double)timeSpend / 60.0;
				}
			}
		}
	}

	public static void BreakForcedLoops(SolutionTree tree)
	{
		UserPreference userPreference = tree.userPreference;
		if (autoRaw == null)
		{
			autoRaw = new HashSet<ItemConfig>();
		}
		foreach (ItemConfig value4 in userPreference.itemConfigs.Values)
		{
			if (autoRaw.Remove(value4))
			{
				value4.consideredAsOre = false;
			}
		}
		foreach (ItemConfig item in new List<ItemConfig>(userPreference.itemConfigs.Values))
		{
			if (item.recipeID <= 0 || IsRaw(userPreference, item.ID) || !CalcDB.recipeDict.TryGetValue(item.recipeID, out var value))
			{
				continue;
			}
			List<int> list = new List<int>();
			bool flag = false;
			for (int i = 0; i < value.resources.Length; i++)
			{
				int num = value.resources[i];
				if (value.resourceCounts[i] > 0 && !IsRaw(userPreference, num) && MustNeed(userPreference, num, item.ID, new HashSet<int>(), new HashSet<int>()))
				{
					list.Add(num);
					if (userPreference.itemConfigs.TryGetValue(num, out var value2) && value2.forceNotOre)
					{
						flag = true;
					}
				}
			}
			if (list.Count == 0)
			{
				continue;
			}
			if (flag)
			{
				if (!item.forceNotOre && !IsTarget(tree, item.ID))
				{
					item.consideredAsOre = true;
					autoRaw.Add(item);
				}
				continue;
			}
			foreach (int item2 in list)
			{
				if (!userPreference.itemConfigs.TryGetValue(item2, out var value3))
				{
					value3 = new ItemConfig(item2);
					userPreference.itemConfigs[item2] = value3;
				}
				value3.consideredAsOre = true;
				autoRaw.Add(value3);
			}
		}
	}

	private static bool IsTarget(SolutionTree tree, int itemId)
	{
		foreach (ItemTarget target in tree.targets)
		{
			if (target.itemId == itemId)
			{
				return true;
			}
		}
		return false;
	}

	public static double ListedNeed(ItemNode n)
	{
		if (n.byProductRecipes.Count > 0 && n.speedFromOre <= 0.001 && n.satisfiedSpeed >= n.needSpeed - 0.001 && n.IsOre(n.solutionTree.userPreference))
		{
			return 0.0;
		}
		return n.needSpeed;
	}

	public static void MarkSharedRecipe(UIItemNode ui)
	{
		if (ui.assemblerCountText == null || ui.itemNode == null || ui.itemNode.mainRecipe == null || ui.parentCalcWindow == null)
		{
			return;
		}
		UIItemNode uIItemNode = null;
		foreach (UIItemNode uiItemNode in ui.parentCalcWindow.uiItemNodes)
		{
			if (uiItemNode.itemNode != null && uiItemNode.itemNode.mainRecipe != null && uiItemNode.itemNode.mainRecipe.ID == ui.itemNode.mainRecipe.ID)
			{
				uIItemNode = uiItemNode;
				break;
			}
		}
		UIButton uIButton = (UIButton)ui.assemblerCountTextObj.GetComponent(typeof(UIButton));
		string text = ui.assemblerCountText.text;
		bool flag = text.StartsWith("(");
		if (uIItemNode == null || uIItemNode == ui)
		{
			if (flag)
			{
				ui.assemblerCountText.text = text.Substring(1, text.Length - 2);
			}
			ui.assemblerCountText.color = UICalcWindow.TextWhiteColor;
			return;
		}
		if (!flag)
		{
			ui.assemblerCountText.text = "(" + text + ")";
		}
		Color textWhiteColor = UICalcWindow.TextWhiteColor;
		ui.assemblerCountText.color = new Color(textWhiteColor.r, textWhiteColor.g, textWhiteColor.b, 0.45f);
		if (uIButton != null)
		{
			uIButton.tips.tipTitle = "Shared machines";
			uIButton.tips.tipText = "Same " + ui.itemNode.mainRecipe.recipeNorm.oriProto.name + " machines as the " + Utils.GetItemName(uIItemNode.itemNode.itemId) + " row. Build them once.";
		}
	}

	private static void AddSourceCard(UICalcWindow w, int focusItem, Sprite icon, double speed, string title, string text)
	{
		UIItemNodeSimple uIItemNodeSimple = new UIItemNodeSimple(new ItemNode(focusItem, 0.0, w.solution), isResources: false, w);
		uIItemNodeSimple.outputText.text = Utils.KMG(speed);
		Transform transform = uIItemNodeSimple.obj.transform.Find("icon");
		((Image)transform.GetComponent(typeof(Image))).sprite = icon;
		UIButton obj = (UIButton)transform.GetComponent(typeof(UIButton));
		obj.tips.itemId = 0;
		obj.tips.tipTitle = title;
		obj.tips.tipText = text;
		obj.tips.width = 220;
		w.uiSideItemNodes.Add(uIItemNodeSimple);
	}

	public static void AddSourcesSection(UICalcWindow w)
	{
		if (w.solution == null || w.solution.itemNodes == null || w.uiSideItemNodes == null)
		{
			return;
		}
		bool flag = false;
		foreach (ItemNode value in w.solution.itemNodes.Values)
		{
			if (value.byProductRecipes.Count == 0)
			{
				continue;
			}
			List<RecipeInfo> list = new List<RecipeInfo>();
			if (value.mainRecipe != null && value.mainRecipe.count > 0.001)
			{
				list.Add(value.mainRecipe);
			}
			foreach (RecipeInfo byProductRecipe in value.byProductRecipes)
			{
				list.Add(byProductRecipe);
			}
			bool flag2 = value.speedFromOre > 0.001;
			if (list.Count + (flag2 ? 1 : 0) < 2)
			{
				continue;
			}
			if (!flag)
			{
				while (w.uiSideItemNodes.Count % 3 != 0)
				{
					w.uiSideItemNodes.Add(new UIItemNodeSimple("", w));
				}
				w.uiSideItemNodes.Add(new UIItemNodeSimple("Item sources", w));
				w.uiSideItemNodes.Add(new UIItemNodeSimple("", w));
				w.uiSideItemNodes.Add(new UIItemNodeSimple("", w));
				flag = true;
			}
			UIItemNodeSimple uIItemNodeSimple = new UIItemNodeSimple(value, isResources: false, w);
			uIItemNodeSimple.outputText.text = Utils.KMG(value.satisfiedSpeed);
			w.uiSideItemNodes.Add(uIItemNodeSimple);
			string itemName = Utils.GetItemName(value.itemId);
			foreach (RecipeInfo item in list)
			{
				if (w.uiSideItemNodes.Count % 3 == 0)
				{
					w.uiSideItemNodes.Add(new UIItemNodeSimple("", w));
				}
				NormalizedRecipe recipeNorm = item.recipeNorm;
				AddSourceCard(w, (recipeNorm.products.Length != 0) ? recipeNorm.products[0] : value.itemId, recipeNorm.oriProto.iconSprite, item.GetOutputSpeedByItemId(value.itemId), recipeNorm.oriProto.name, itemName + " from this recipe" + ((item == value.mainRecipe) ? " (main product)" : " (by-product)"));
			}
			if (flag2)
			{
				if (w.uiSideItemNodes.Count % 3 == 0)
				{
					w.uiSideItemNodes.Add(new UIItemNodeSimple("", w));
				}
				AddSourceCard(w, value.itemId, UIItemNodeSimple.oreSprite, value.speedFromOre, "Raw", itemName + " supplied from outside (Raw Resources Demands)");
			}
			while (w.uiSideItemNodes.Count % 3 != 0)
			{
				w.uiSideItemNodes.Add(new UIItemNodeSimple("", w));
			}
		}
	}

	public static void GBPreferMainOutputRecipes()
	{
		if (!CompatManager.GB)
		{
			return;
		}
		foreach (KeyValuePair<int, ItemData> item2 in CalcDB.itemDict)
		{
			List<NormalizedRecipe> recipes = item2.Value.recipes;
			if (recipes.Count < 2 || (recipes[0].products.Length != 0 && recipes[0].products[0] == item2.Key))
			{
				continue;
			}
			for (int i = 1; i < recipes.Count; i++)
			{
				if (recipes[i].products.Length != 0 && recipes[i].products[0] == item2.Key && !CalculatorPatch.Patches.IsAlternative(recipes[i]))
				{
					NormalizedRecipe item = recipes[i];
					recipes.RemoveAt(i);
					recipes.Insert(0, item);
					break;
				}
			}
		}
	}

	private static bool GBFilterAccepts(int machineType, int recipeType)
	{
		switch (machineType)
		{
		case 12:
			if (recipeType != 2 && recipeType != 3 && recipeType != 4 && recipeType != 5 && recipeType != 9 && recipeType != 10 && recipeType != 15)
			{
				return recipeType == 16;
			}
			return true;
		case 17:
			if (recipeType != 2 && recipeType != 3)
			{
				return recipeType == 16;
			}
			return true;
		case 18:
			if (recipeType != 4)
			{
				return recipeType == 9;
			}
			return true;
		case 19:
			if (recipeType != 1 && recipeType != 11)
			{
				return recipeType == 13;
			}
			return true;
		default:
			return false;
		}
	}

	public static void AddGBFilterMachines()
	{
		if (!CompatManager.GB)
		{
			return;
		}
		ItemProto[] dataArray = LDB.items.dataArray;
		foreach (ItemProto itemProto in dataArray)
		{
			if (itemProto == null)
			{
				continue;
			}
			PrefabDesc prefabDesc = LDB.models.Select(itemProto.ModelIndex)?.prefabDesc;
			if (prefabDesc == null || !prefabDesc.isAssembler || !CalcDB.assemblerDict.TryGetValue(itemProto.ID, out var value))
			{
				continue;
			}
			int assemblerRecipeType = (int)prefabDesc.assemblerRecipeType;
			foreach (KeyValuePair<int, List<AssemblerData>> item in CalcDB.assemblerListByType)
			{
				if (GBFilterAccepts(assemblerRecipeType, item.Key) && !item.Value.Contains(value))
				{
					item.Value.Add(value);
				}
			}
		}
	}

	private static double Multiplier(RecipeInfo r, double baseMultiplier)
	{
		return baseMultiplier + r.bonusFactor - 1.0;
	}

	private static double BaseMultiplier(RecipeInfo r, int itemId)
	{
		double num = 1.0 + r.CalcOutputDiracInc(itemId) + r.bonusInc;
		int incLevel = r.incLevel;
		if (r.isInc && incLevel >= 0 && incLevel < Cargo.incTableMilli.Length)
		{
			num += Utils.GetIncMilli(incLevel, r.userPreference);
		}
		return num;
	}

	private static bool Inferior(RecipeInfo r, int itemId, int firstProduct)
	{
		if (r.userPreference.inferior && itemId == 1501 && firstProduct == 1501)
		{
			return !r.useIA;
		}
		return false;
	}

	public static double CalcCountByOutputSpeed(RecipeInfo r, int itemId, double speed)
	{
		if (!r.productIndices.TryGetValue(itemId, out var value))
		{
			return 0.0;
		}
		NormalizedRecipe recipeNorm = r.recipeNorm;
		if (Inferior(r, itemId, recipeNorm.products[0]))
		{
			speed = speed * (double)recipeNorm.productCounts[0] / (double)(recipeNorm.productCounts[0] + 1);
		}
		return speed / (double)recipeNorm.productCounts[value] * recipeNorm.time / Multiplier(r, BaseMultiplier(r, itemId));
	}

	public static double GetOutputSpeedByChangedCount(RecipeInfo r, int itemId, double changedCount)
	{
		if (!r.productIndices.TryGetValue(itemId, out var value))
		{
			Utils.logger.LogWarning("Output requested for item " + itemId + " that recipe " + r.ID + " does not produce.");
			return 0.0;
		}
		NormalizedRecipe recipeNorm = r.recipeNorm;
		return (Inferior(r, itemId, recipeNorm.products[0]) ? ((double)(recipeNorm.productCounts[0] + 1) / (double)recipeNorm.productCounts[0]) : 1.0) * changedCount * (double)recipeNorm.productCounts[value] / recipeNorm.time * Multiplier(r, BaseMultiplier(r, itemId));
	}

	public static double GetOutputSpeedOriProto(RecipeInfo r, int index, double changedCount)
	{
		RecipeProto oriProto = r.recipeNorm.oriProto;
		int itemId = oriProto.Results[index];
		return (Inferior(r, itemId, oriProto.Results[0]) ? ((double)(oriProto.ResultCounts[0] + 1) / (double)oriProto.ResultCounts[0]) : 1.0) * changedCount * (double)oriProto.ResultCounts[index] / r.recipeNorm.time * Multiplier(r, BaseMultiplier(r, itemId));
	}
}
