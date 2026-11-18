# Changelog

## 1.0.2

- "Item sources" in the right-hand panel: a recipe whose picture does not load shows its product instead of an empty card, and the card for raw supply shows the item with a small vein mark instead of the bare vein icon.
- Main Info settings: each checkbox label ends before the next column or input and shrinks to fit, so "Proliferator Extra/Spd+" no longer run under their inputs and "Round up facility number" (typo fixed) no longer spills onto "Add proliferator product line".
- BUG_OreEnrichment 1.2.0: its new catalysed copies (fluid chemistry, particle collider, buildings), Moebius silver refining and the new dust recipes (rare veins, aluminium, niobium) are listed after the normal recipes like its other alternatives.
- An item with no recipe of its own that comes entirely from other rows' by-products (salt from seawater washing) no longer gets an "Excessive" row in the product list; its surplus stays in the by-product panel.
- X on a raw row that would close a loop (for example salt after making hydrogen chloride, when salt is made from hydrogen chloride) is undone: the window keeps its result instead of going empty, and the warning names the items of the loop.
- Production loops that shrink every round are solved: X on an item inside such a loop (sulfuric acid in the catalysed graphene chain, fire ice dust) makes it, and the calculator works out the extra amount the loop uses itself. Loops that do not shrink (salt <-> hydrogen chloride, 1:1) are still refused or supplied from outside, and a solve that would not settle stops with the window keeping its last result.
- Surplus is used inside the line: after the usual solve, the run counts of the chosen recipes are worked out again together so that by-products replace other production where they can and the line asks for as little raw input as possible. A recipe with two products (tar and hydrogen, water electrolysis) no longer keeps running just for one of them while the other piles up; what is left over is only what the chosen recipes cannot avoid. Recipe choices, raw/X settings and targets are not changed.
- Right click on an item's picture in the product list counts it as raw again, the opposite of X in the raw list.
- BUG_Reclamation: Dark Fog items made only by the trash converter count as raw (drops) by default instead of asking for millions of trash points; the converter's recipes come after an item's other recipes.
- BUG_OreEnrichment: recipes it hides because their product has no normal recipe in the game (magnet tiers under GenesisBook) no longer show in the calculator. Recipe 9600 (another mod's Signal Nexus) is no longer taken for one of its alternatives.

## 1.0.1

- The proliferator mode switch of each row fits inside the list; on Production Speedup it was cut off at the right edge.

## 1.0.0

First release.
