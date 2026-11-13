# BUG_CalculatorPatch

Fixes for [DSPCalculator](https://thunderstore.io/c/dyson-sphere-program/p/jinxOAO/DSPCalculator/) 0.5.25. The calculator's own file is not changed: the fixes are applied to it while the game runs, and removing this mod gives you the plain calculator back.

## What it fixes

- **Missing recipes and machines.** Every valid recipe of the loaded game is in the calculator, and each recipe offers every building that can run it, including GenesisBook's furnaces, chemical plants and large factories.
- **GenesisBook output and power.** The Negentropy Smelter's extra product is added to the proliferator bonus as the game does, not multiplied; recipes with extra power draw show it.
- **GenesisBook recipe picker.** The picker uses GenesisBook's 17 by 7 grid, so recipes in the last columns can be picked.
- **Proliferators.** Any proliferator a spray coater of the loaded game accepts can be used, not only the vanilla three; a failed proliferator solve is reported.
- **Recipe loops.** Picking a recipe whose input can only be made from its own product no longer ends in an "unsolvable loop": that input is supplied from outside for this line, and you can change which item is supplied.
- **Lists.** An input fully covered by a by-product of the same line is not listed as a demand; a "sources" section shows where shared items come from. The proliferator mode switch of each row stays inside the list.
- **Windows.** Open calculator windows are stored in the save and come back after loading. The interstellar assembly button no longer sits on top of another button with GenesisBook and MoreMegaStructure.
- **BUG_OreEnrichment.** All of its recipes are known to the calculator: dusts and enriched smelting, catalysed copies, the gold and silver chain (copper refining, anode slime, dust, ingots) and the gold and silver copies of other recipes. They are listed after an item's normal recipes, also with GenesisBook, so the calculator keeps using the normal recipe until you pick an alternative. A line with a gold or silver recipe is solved through the whole chain, with the leftover silver or gold and copper shown as by-products.

## Notes

- Written for DSPCalculator 0.5.25. With another version the mod checks every patch first and, if one does not fit, applies none and says so in the BepInEx log.
- Needs DSPModSave. The calculator writes `DSPCalculator-recipes.csv` next to its DLL at start: the list of recipes it knows.

## Building from source

You need a current .NET SDK and the .NET Framework 4.7.2 targeting pack. The game, Unity and BepInEx assemblies are referenced from your own installation and are not part of this repository.

```text
dotnet build -c Release "-p:DSPGamePath=D:\Games\Dyson Sphere Program" "-p:BepInExCorePath=C:\path\to\BepInEx\core"
```

The output is `bin/Release/net472/BUG_CalculatorPatch.dll`. Copy it to `BepInEx/plugins/BUG_CalculatorPatch/`.

## License

MIT, see [LICENSE](LICENSE).
