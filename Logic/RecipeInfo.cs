using System;
using System.Collections.Generic;
using System.Linq;

namespace FurnaceControlMod.Logic
{
    // One reagent of a recipe: its share of the mix (the shares sum to 1).
    public class Ingredient
    {
        public readonly int Hash;
        public readonly string Name;
        public readonly double Ratio;

        public Ingredient(int hash, string name, double ratio)
        {
            Hash = hash;
            Name = name;
            Ratio = ratio;
        }
    }

    // The recipe list's families, in the order it shows them.
    public enum RecipeGroup { Metal, Alloy, SuperAlloy, Other }

    // A furnace recipe as the board uses it: read from the game's recipe list, not kept by the board.
    public class RecipeInfo
    {
        public readonly string Name;
        public readonly int OutputHash;     // the ingot's prefab hash: the furnace's RecipeHash while it is forming
        public readonly IReadOnlyList<Ingredient> Ingredients;
        public readonly double MinTempK;
        public readonly double MaxTempK;
        public readonly double MinPressureKPa;
        public readonly double MaxPressureKPa;
        public readonly double OutputScale; // ingots per unit of reagent: 1, or 0.25 for super-alloys
        public readonly bool IsIngot;       // false for Charcoal and the like

        public RecipeInfo(string name, int outputHash, IEnumerable<Ingredient> ingredients,
            double minTempK, double maxTempK, double minPressureKPa, double maxPressureKPa,
            double outputScale, bool isIngot = true)
        {
            IsIngot = isIngot;
            Name = name;
            OutputHash = outputHash;
            Ingredients = ingredients.ToList();
            MinTempK = minTempK;
            MaxTempK = maxTempK;
            MinPressureKPa = minPressureKPa;
            MaxPressureKPa = maxPressureKPa;
            OutputScale = outputScale > 0 ? outputScale : 1;
        }

        public Ingredient Find(int reagentHash) => Ingredients.FirstOrDefault(ingredient => ingredient.Hash == reagentHash);

        public RecipeGroup Group =>
            !IsIngot ? RecipeGroup.Other
            : OutputScale < 1 ? RecipeGroup.SuperAlloy
            : Ingredients.Count == 1 ? RecipeGroup.Metal
            : RecipeGroup.Alloy;
    }

    // How the recipe list lays the recipes out: by family, A-Z within one, each family from a new row.
    public static class RecipeLayout
    {
        public static List<RecipeInfo> Sort(IEnumerable<RecipeInfo> recipes) =>
            recipes.OrderBy(recipe => recipe.Group).ThenBy(recipe => recipe.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // The grid's cells, row by row: a recipe, or null for a gap at the end of a family's last row.
        public static List<RecipeInfo> Cells(IEnumerable<RecipeInfo> recipes, int columns)
        {
            var cells = new List<RecipeInfo>();
            RecipeGroup? group = null;
            foreach (var recipe in Sort(recipes))
            {
                if (group != null && recipe.Group != group)
                    while (cells.Count % columns != 0) cells.Add(null);
                group = recipe.Group;
                cells.Add(recipe);
            }
            return cells;
        }
    }

    // A reagent in the furnace, or on its way in.
    public class Holding
    {
        public readonly int Hash;
        public readonly string Name;
        public readonly double Quantity;

        public Holding(int hash, string name, double quantity)
        {
            Hash = hash;
            Name = name;
            Quantity = quantity;
        }
    }
}
