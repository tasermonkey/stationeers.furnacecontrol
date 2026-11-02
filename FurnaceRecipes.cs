using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Pipes;
using FurnaceControlMod.Logic;
using Reagents;
using UnityEngine;

namespace FurnaceControlMod
{
    public static class FurnaceRecipes
    {
        static List<RecipeInfo> _all;
        //  This is seperate so that we can keep unity out of Tests
        static readonly Dictionary<int, Sprite> _pictures = new Dictionary<int, Sprite>();

        public static IReadOnlyList<RecipeInfo> All
        {
            get
            {
                if (_all == null || _all.Count == 0) _all = Load();
                return _all;
            }
        }

        public static RecipeInfo Find(int outputHash) =>
            outputHash == 0 ? null : All.FirstOrDefault(recipe => recipe.OutputHash == outputHash);

        public static Sprite Picture(RecipeInfo recipe) =>
            recipe != null && All.Count > 0 && _pictures.TryGetValue(recipe.OutputHash, out var picture) ? picture : null;

        static List<RecipeInfo> Load()
        {
            var recipes = new List<RecipeInfo>();
            _pictures.Clear();
            foreach (var entry in AdvancedFurnace.RecipeComparable.Recipes)
            {
                var recipe = entry.Key;
                if (!recipe.Temperature.IsValid && !recipe.Pressure.IsValid) continue;
                if (recipe.RequiredMix.IsAnyToRemove) continue;
                if (!(entry.Value is Thing output)) continue;
                if (recipes.Any(known => known.OutputHash == output.PrefabHash)) continue;

                var ingredients = Reagent.AllReagents
                    .Where(reagent => recipe.Get(reagent) > 0)
                    .Select(reagent => new Ingredient(reagent.Hash, reagent.DisplayName, recipe.Get(reagent)))
                    .ToList();
                if (ingredients.Count == 0) continue;
                if (output.Thumbnail != null) _pictures[output.PrefabHash] = output.Thumbnail;
                recipes.Add(new RecipeInfo(ShortName(output.DisplayName), output.PrefabHash, ingredients,
                    recipe.Temperature.Start, recipe.Temperature.Stop, recipe.Pressure.Start, recipe.Pressure.Stop,
                    AdvancedFurnace.RecipeComparable.GetOutputScale(recipe), output.PrefabName.Contains("Ingot")));
            }
            return RecipeLayout.Sort(recipes);
        }

        static string ShortName(string name)
        {
            var shorter = name.Replace("Ingot", "").Replace("(", "").Replace(")", "").Trim();
            return shorter.Length > 0 ? shorter : name;
        }
    }
}
