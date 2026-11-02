using System;
using System.Collections.Generic;
using System.Linq;

namespace FurnaceControlMod.Logic
{
    // How much of one reagent the batch takes, and how much is in.
    public class Need(Ingredient ingredient, double have, double want)
    {
        public readonly Ingredient Ingredient = ingredient;
        public readonly double Have = have;
        public readonly double Want = want;

        public double ToAdd => Math.Max(0, Want - Have);
    }

    // What a batch of a recipe still needs, given what's in the furnace.
     public class BatchPlan
    {
        public const int MinAmount = 1;
        public const int MaxAmount = 500;
        const double Epsilon = 1e-6;

        public int Units { get; private set; }    // reagent units the batch takes
        public int Makes { get; private set; }    // ingots those units make: the amount, or more after too much went in
        public int Ready { get; private set; }    // ingots the reagents in so far would make, up to the amount
        public IReadOnlyList<Need> Needs { get; private set; }
        public IReadOnlyList<Holding> Unwanted { get; private set; } // reagents not in the recipe: they spoil it

        public bool Complete => Needs.All(need => need.ToAdd < Epsilon) && Unwanted.Count == 0;

        public static BatchPlan For(RecipeInfo recipe, int amount, IEnumerable<Holding> holdings)
        {
            var have = new Dictionary<int, double>();
            var unwanted = new List<Holding>();
            foreach (var holding in holdings)
            {
                if (holding.Quantity < Epsilon) continue;
                if (recipe.Find(holding.Hash) == null)
                {
                    unwanted.Add(holding);
                    continue;
                }
                have.TryGetValue(holding.Hash, out var sum);
                have[holding.Hash] = sum + holding.Quantity;
            }
            double Have(Ingredient ingredient) => have.TryGetValue(ingredient.Hash, out var quantity) ? quantity : 0;

            var step = Step(recipe);
            var units = (int)Math.Ceiling(amount / recipe.OutputScale - Epsilon);
            foreach (var ingredient in recipe.Ingredients)
                units = Math.Max(units, (int)Math.Ceiling(Have(ingredient) / ingredient.Ratio - Epsilon));
            units = RoundUp(units, step);

            var sets = recipe.Ingredients.Count == 0 ? 0 : recipe.Ingredients.Min(ingredient => Have(ingredient) / ingredient.Ratio);
            var readyUnits = (int)Math.Floor(sets / step + Epsilon) * step;

            return new BatchPlan
            {
                Units = units,
                Makes = Ingots(recipe, units),
                Ready = Math.Min(amount, Ingots(recipe, readyUnits)),
                Needs = recipe.Ingredients.Select(ingredient => new Need(ingredient, Have(ingredient), ingredient.Ratio * units)).ToList(),
                Unwanted = unwanted,
            };
        }

        // The ingots a number of reagent units makes
        public static int Ingots(RecipeInfo recipe, double units) =>
            (int)Math.Round(Math.Floor(units + Epsilon) * recipe.OutputScale, MidpointRounding.ToEven);

        // The smallest batch in which every reagent is a whole number of units: 4 for a .5/.25/.25 recipe.
        public static int Step(RecipeInfo recipe)
        {
            for (var step = 1; step <= 1000; step++)
                if (recipe.Ingredients.All(ingredient => IsWhole(ingredient.Ratio * step))) return step;
            return 1;
        }

        // The recipe's ratio in its smallest whole numbers, each ingredient's: Solder 1 : 1, Stellite 2 : 1 : 1.
        public static IReadOnlyList<int> WholeRatio(RecipeInfo recipe)
        {
            var step = Step(recipe);
            return recipe.Ingredients.Select(ingredient => (int)Math.Round(ingredient.Ratio * step)).ToList();
        }

        // The batch is done when the furnace forms the recipe's ingot and every unit has melted in.
        public static bool IsDone(RecipeInfo recipe, int units, int furnaceRecipeHash, double melted) =>
            furnaceRecipeHash == recipe.OutputHash && melted >= units - Epsilon;

        public static int ClampAmount(int amount) => Math.Max(MinAmount, Math.Min(MaxAmount, amount));

        static int RoundUp(int value, int step) => (value + step - 1) / step * step;

        static bool IsWhole(double value) => Math.Abs(value - Math.Round(value)) < Epsilon;
    }
}
