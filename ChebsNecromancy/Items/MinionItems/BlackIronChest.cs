﻿using ChebsNecromancy.Common;
using System.Reflection;

namespace ChebsNecromancy.Items.MinionItems
{
    internal class BlackIronChest : Item
    {
        public BlackIronChest()
        {
            ChebsRecipeConfig.ObjectName = MethodBase.GetCurrentMethod().DeclaringType.Name;
            ChebsRecipeConfig.RecipeName = "$item_chebgonaz_" + ChebsRecipeConfig.ObjectName.ToLower() + "_name";
            ChebsRecipeConfig.ItemName = "ChebGonaz_" + ChebsRecipeConfig.ObjectName;
            ChebsRecipeConfig.RecipeDescription = "$item_chebgonaz_" + ChebsRecipeConfig.ObjectName.ToLower() + "_desc";
            ChebsRecipeConfig.PrefabName = "ChebGonaz_" + ChebsRecipeConfig.ObjectName + ".prefab";
            ChebsRecipeConfig.DefaultRecipe = "BlackMetal:5";

        }
    }
}
