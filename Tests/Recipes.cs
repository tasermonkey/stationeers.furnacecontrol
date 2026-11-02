using FurnaceControlMod.Logic;

namespace FurnaceControl.Tests
{
    // Recipes as the game's furnace.xml and advancedfurnace.xml have them. The hashes only need to differ.
    static class Recipes
    {
        public static readonly Ingredient Iron = new Ingredient(1, "Iron", 1);

        public static readonly RecipeInfo IronIngot =
            new RecipeInfo("Iron", 101, new[] { Iron }, 800, 99999, 100, 99999, 1);

        public static readonly RecipeInfo Solder = new RecipeInfo("Solder", 102,
            new[] { new Ingredient(1, "Iron", .5), new Ingredient(2, "Lead", .5) }, 350, 550, 1000, 99999, 1);

        public static readonly RecipeInfo Stellite = new RecipeInfo("Stellite", 103,
            new[] { new Ingredient(3, "Silicon", .5), new Ingredient(4, "Silver", .25), new Ingredient(5, "Cobalt", .25) },
            1800, 99999, 10000, 20000, .25);

        public static Holding Have(int hash, double quantity) => new Holding(hash, "#" + hash, quantity);
    }
}
