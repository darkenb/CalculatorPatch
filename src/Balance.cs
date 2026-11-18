using System;
using System.Collections.Generic;
using DSPCalculator.Logic;

// The calculator sizes recipes item by item along its tree and then trims surplus only where every product of a recipe
// is in surplus. A recipe with two products (tar + hydrogen, water + salt) therefore keeps running for the product that
// is short, and the other piles up even when another chosen recipe could cover the shortage more cheaply. Here, after
// the tree solve and before proliferators, the run counts of the recipes already in the line are chosen again as a
// linear programme: every item gets at least what it needs, raw supply only for raw items, and the total raw input per
// minute is as small as possible (machine count breaks ties). Recipe choices, raw/X settings and targets are untouched;
// the result is only taken when it is valid and uses less raw input than the tree's.
public static class Balance
{
	public const double TieBreak = 1e-6;

	public static int Improved;

	public static bool Optimize(SolutionTree tree)
	{
		var recipes = new List<RecipeInfo>(tree.recipeInfos.Values);
		var items = new List<ItemNode>(tree.itemNodes.Values);
		var row = new Dictionary<int, int>();
		for (int i = 0; i < items.Count; i++) row[items[i].itemId] = i;
		int m = items.Count, nr = recipes.Count;
		var raw = new List<int>();
		for (int i = 0; i < m; i++) if (items[i].IsOre(tree.userPreference)) raw.Add(i);
		int n = nr + raw.Count;

		// a[i, j]: net output of item i per run of recipe j (or 1 for raw supply column of item i).
		var a = new double[m, n];
		var used = new double[m];  // what the tree's recipes take of each item
		for (int j = 0; j < nr; j++)
		{
			RecipeInfo r = recipes[j];
			foreach (int id in r.productIndices.Keys)
				if (row.TryGetValue(id, out int i)) a[i, j] += r.GetOutputSpeedByChangedCount(id, 1.0);
			foreach (int id in r.resourceIndices.Keys)
			{
				if (!row.TryGetValue(id, out int i)) return false;
				double per = r.GetInputSpeedByChangedCount(id, 1.0);
				a[i, j] -= per;
				used[i] += per * r.count;
			}
		}
		for (int k = 0; k < raw.Count; k++) a[raw[k], nr + k] = 1.0;

		var b = new double[m];  // demand from outside the recipes (targets)
		for (int i = 0; i < m; i++) b[i] = Math.Max(0.0, items[i].needSpeed - used[i]);
		var c = new double[n];
		for (int j = 0; j < nr; j++) c[j] = TieBreak;
		for (int k = 0; k < raw.Count; k++) c[nr + k] = 1.0;

		double before = 0.0;
		foreach (int i in raw) before += items[i].speedFromOre;
		for (int j = 0; j < nr; j++) before += TieBreak * recipes[j].count;
		double[] z = Simplex.Minimize(a, b, c);
		if (z == null) return false;
		double after = 0.0;
		for (int j = 0; j < n; j++) after += c[j] * z[j];
		// The tree's own surplus trimming can leave a made item short (it logs "over-reduced"); then the new numbers are taken anyway.
		bool treeShort = false;
		foreach (ItemNode node in items) treeShort |= !node.IsOre(tree.userPreference) && node.satisfiedSpeed < node.needSpeed - 1e-4 * Math.Max(1.0, node.needSpeed);
		if (!treeShort && after > before - 1e-6 * Math.Max(1.0, before)) return false;  // no gain: keep the tree's numbers
		for (int i = 0; i < m; i++)
		{
			double net = 0.0;
			for (int j = 0; j < n; j++) net += a[i, j] * z[j];
			if (net < b[i] - 1e-6 * Math.Max(1.0, b[i])) return false;
		}

		// Write back: counts, then every row's need, output and raw supply from them.
		for (int j = 0; j < nr; j++) recipes[j].count = z[j];
		for (int i = 0; i < m; i++)
		{
			items[i].needSpeed = b[i];
			items[i].satisfiedSpeed = 0.0;
			items[i].speedFromOre = 0.0;
		}
		for (int j = 0; j < nr; j++)
		{
			RecipeInfo r = recipes[j];
			foreach (int id in r.productIndices.Keys)
				if (row.TryGetValue(id, out int i)) items[i].satisfiedSpeed += r.GetOutputSpeedByChangedCount(id, z[j]);
			foreach (int id in r.resourceIndices.Keys) items[row[id]].needSpeed += r.GetInputSpeedByChangedCount(id, z[j]);
		}
		for (int k = 0; k < raw.Count; k++)
		{
			ItemNode node = items[raw[k]];
			node.speedFromOre = Math.Max(0.0, node.needSpeed - node.satisfiedSpeed);
			node.satisfiedSpeed += node.speedFromOre;
		}
		Improved++;
		return true;
	}
}

// Dense two-phase simplex (Bland's rule): minimise c.z subject to A z >= b, z >= 0, with b >= 0. Null when infeasible,
// unbounded or not finished within the step limit. Sizes here are a few hundred columns at most.
public static class Simplex
{
	private const double Eps = 1e-9;

	public static double[] Minimize(double[,] A, double[] b, double[] c)
	{
		int m = b.Length, n = c.Length;
		int cols = n + 2 * m;  // variables, surplus, artificial
		var t = new double[m, cols + 1];
		var basis = new int[m];
		for (int i = 0; i < m; i++)
		{
			for (int j = 0; j < n; j++) t[i, j] = A[i, j];
			t[i, n + i] = -1.0;
			t[i, n + m + i] = 1.0;
			t[i, cols] = b[i];
			basis[i] = n + m + i;
		}
		var phase1 = new double[cols];
		for (int i = 0; i < m; i++) phase1[n + m + i] = 1.0;
		if (!Run(t, basis, phase1, cols, cols)) return null;
		double infeasible = 0.0;
		for (int i = 0; i < m; i++) if (basis[i] >= n + m) infeasible += t[i, cols];
		if (infeasible > 1e-6) return null;
		// Artificials left in the basis at zero: swap them for any other column of their row.
		for (int i = 0; i < m; i++)
		{
			if (basis[i] < n + m) continue;
			for (int j = 0; j < n + m; j++)
				if (Math.Abs(t[i, j]) > 1e-7) { Pivot(t, basis, i, j, cols); break; }
		}
		var phase2 = new double[cols];
		for (int j = 0; j < n; j++) phase2[j] = c[j];
		if (!Run(t, basis, phase2, n + m, cols)) return null;
		var z = new double[n];
		for (int i = 0; i < m; i++) if (basis[i] < n) z[basis[i]] = Math.Max(0.0, t[i, cols]);
		return z;
	}

	// Columns >= enter may not enter (artificials in phase 2).
	private static bool Run(double[,] t, int[] basis, double[] cost, int enter, int cols)
	{
		int m = basis.Length;
		for (int step = 0; step < 50 * (m + cols); step++)
		{
			int jIn = -1;
			for (int j = 0; j < enter && jIn < 0; j++)
			{
				double d = cost[j];
				for (int i = 0; i < m; i++) d -= cost[basis[i]] * t[i, j];
				if (d < -Eps) jIn = j;
			}
			if (jIn < 0) return true;
			int iOut = -1;
			double best = double.MaxValue;
			for (int i = 0; i < m; i++)
			{
				if (t[i, jIn] <= Eps) continue;
				double ratio = t[i, cols] / t[i, jIn];
				if (ratio < best - Eps || (ratio < best + Eps && iOut >= 0 && basis[i] < basis[iOut]))
				{
					best = ratio;
					iOut = i;
				}
			}
			if (iOut < 0) return false;  // unbounded
			Pivot(t, basis, iOut, jIn, cols);
		}
		return false;
	}

	private static void Pivot(double[,] t, int[] basis, int r, int col, int cols)
	{
		int m = basis.Length;
		double p = t[r, col];
		for (int j = 0; j <= cols; j++) t[r, j] /= p;
		for (int i = 0; i < m; i++)
		{
			if (i == r) continue;
			double f = t[i, col];
			if (f == 0.0) continue;
			for (int j = 0; j <= cols; j++) t[i, j] -= f * t[r, j];
		}
		basis[r] = col;
	}
}
