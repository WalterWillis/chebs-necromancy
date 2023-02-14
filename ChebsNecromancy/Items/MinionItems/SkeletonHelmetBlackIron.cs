﻿using System.Reflection;

namespace ChebsNecromancy.Items.MinionItems
{
    internal class SkeletonHelmetBlackIron : Item
    {
        public SkeletonHelmetBlackIron()
        {
            ChebsRecipeConfig.ObjectName = MethodBase.GetCurrentMethod().DeclaringType.Name;
            ChebsRecipeConfig.RecipeName = "$item_chebgonaz_" + ChebsRecipeConfig.ObjectName.ToLower() + "_name";
            ChebsRecipeConfig.ItemName = "ChebGonaz_" + ChebsRecipeConfig.ObjectName;
            ChebsRecipeConfig.RecipeDescription = "$item_chebgonaz_" + ChebsRecipeConfig.ObjectName.ToLower() + "_desc";
            ChebsRecipeConfig.PrefabName = "ChebGonaz_" + ChebsRecipeConfig.ObjectName + ".prefab";

        }
    }
}
