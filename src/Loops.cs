using System;
using System.Collections.Generic;
using DSPCalculator.Logic;

// Production loops. The calculator builds a tree: every item has one recipe and its inputs below it, and an item that
// needs itself further down stops the solve ("no solution loop"). Catalysed and enriched recipes make such loops
// common (sulfuric acid -> water -> hydrogen -> graphene -> sulfuric acid). A loop whose items cannot switch to
// another recipe is closed instead: the inner use of the item points back at the item's own row. The amounts are
// then found by the calculator's own step-by-step pass (CalcTree), which adds the loop's extra demand round after
// round; each round is smaller by the loop's gain, so it settles when that gain is below 1. Loops that do not
// shrink (salt <-> hydrogen chloride, 1:1) are still refused, and a step limit ends any solve that would not settle.
public static class Loops
{
	public sealed class LimitException : Exception
	{
		public LimitException() : base("production loop did not settle") { }
	}

	public const int StepLimit = 3000000;
	public const double MaxGain = 0.95;

	[ThreadStatic] private static int steps;

	public static int Closed;

	public static void Reset()
	{
		steps = 0;
	}

	public static void Step()
	{
		if (++steps > StepLimit)
		{
			throw new LimitException();
		}
	}

	// Called with every node SolvePath pops: a node that closes an allowed loop is settled here and the next one taken.
	public static ItemNode ClosePopped(SolutionTree tree, ItemNode node)
	{
		while (node != null && Closable(tree, node))
		{
			node.parents[0].SolveOne();
			Closed++;
			int last = tree.nodeStack.Count - 1;
			if (last < 0)
			{
				// Nothing left: hand back a node SolvePath settles without effect (a target with no extra need).
				return new ItemNode(tree.targets[0].itemId, 0.0, tree);
			}
			node = tree.nodeStack[last];
			tree.nodeStack.RemoveAt(last);
		}
		return node;
	}

	// node: a new use of an item whose own row is still being expanded above it. Closable when no recipe on the way
	// back up could be switched (the original would step that one instead) and the loop shrinks every round.
	public static bool Closable(SolutionTree tree, ItemNode node)
	{
		if (node.parents.Count == 0 || !tree.itemNodes.TryGetValue(node.itemId, out var own) || own == node || own.unsolvedCount <= 0)
		{
			return false;
		}
		var path = new List<ItemNode>();
		ItemNode x = node.parents[0];
		while (true)
		{
			if (x.mainRecipe == null)
			{
				return false;
			}
			bool picked = tree.userPreference.itemConfigs.TryGetValue(x.itemId, out var config) && config.recipeID > 0;
			if (!picked && CalcDB.itemDict.TryGetValue(x.itemId, out var data) && data.recipes.IndexOf(x.mainRecipe.recipeNorm) < data.recipes.Count - 1)
			{
				return false;
			}
			path.Add(x);
			if (x == own)
			{
				break;
			}
			if (x.parents.Count == 0)
			{
				return false;
			}
			x = x.parents[0];
		}
		return Gain(path, node.itemId) < MaxGain;
	}

	// Units of the loop's item one unit of it needs again around the loop. path: from the user of the item up to the
	// item's own row.
	public static double Gain(List<ItemNode> path, int item)
	{
		double gain = 1.0;
		int child = item;
		foreach (ItemNode n in path)
		{
			NormalizedRecipe r = n.mainRecipe.recipeNorm;
			double made = Count(r.products, r.productCounts, n.itemId);
			if (made <= 0.0)
			{
				return double.MaxValue;
			}
			gain *= Count(r.resources, r.resourceCounts, child) / made;
			child = n.itemId;
		}
		return gain;
	}

	// The same for a loop of item ids (item, its input, ..., item), each made by its chosen recipe or its first one.
	// Not a loop: MaxValue.
	public static double Gain(UserPreference pref, List<int> loop)
	{
		if (loop.Count < 2)
		{
			return double.MaxValue;
		}
		double gain = 1.0;
		for (int i = 0; i + 1 < loop.Count; i++)
		{
			NormalizedRecipe r = null;
			if (pref.itemConfigs.TryGetValue(loop[i], out var c) && c.recipeID > 0)
			{
				CalcDB.recipeDict.TryGetValue(c.recipeID, out r);
			}
			if (r == null)
			{
				r = CalcDB.itemDict[loop[i]].recipes[0];
			}
			double made = Count(r.products, r.productCounts, loop[i]);
			if (made <= 0.0)
			{
				return double.MaxValue;
			}
			gain *= Count(r.resources, r.resourceCounts, loop[i + 1]) / made;
		}
		return gain;
	}

	private static double Count(int[] ids, int[] counts, int id)
	{
		double sum = 0.0;
		for (int i = 0; i < ids.Length; i++)
		{
			if (ids[i] == id)
			{
				sum += counts[i];
			}
		}
		return sum;
	}

	// The product list walks rows through their children; with a closed loop that walk would never end, so each row's
	// children are handed out once per refresh (the list shows every row once anyway).
	private static readonly HashSet<ItemNode> shown = new HashSet<ItemNode>();
	private static readonly List<ItemNode> none = new List<ItemNode>();
	private static ItemNode shownLast;
	private static List<ItemNode> shownList;

	public static void ResetShown()
	{
		shown.Clear();
		shownLast = null;
		shownList = null;
	}

	public static List<ItemNode> ChildrenOnce(ItemNode n)
	{
		if (n != shownLast)
		{
			shownLast = n;
			shownList = shown.Add(n) ? n.children : none;
		}
		return shownList;
	}
}
