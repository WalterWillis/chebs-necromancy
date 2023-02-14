﻿using System.Collections;
using System.Reflection;
using System.Security.AccessControl;
using BepInEx.Configuration;
using ChebsNecromancy.Common;
using UnityEngine;
using static Piece;
using Logger = Jotunn.Logger;

namespace ChebsNecromancy.Structures
{
    internal class NeckroGathererPylon : Structure
    {
        public static ConfigEntry<float> SpawnInterval;
        public static ConfigEntry<int> NeckTailsConsumedPerSpawn;

        protected Container Container;
        
        public override void CreateConfigs(BasePlugin plugin)
        {
            ChebsRecipeConfig.DefaultRecipe = "Stone:15,NeckTail:25,SurtlingCore:1";
            ChebsRecipeConfig.IconName = "chebgonaz_neckrogathererpylon_icon.png";
            ChebsRecipeConfig.PieceTable = "_HammerPieceTable";
            ChebsRecipeConfig.PieceCategory = "Misc";
            ChebsRecipeConfig.RecipeName = "$chebgonaz_neckrogathererpylon_name";
            ChebsRecipeConfig.RecipeDescription = "$chebgonaz_neckrogathererpylon_desc";
            ChebsRecipeConfig.PrefabName = "ChebGonaz_NeckroGathererPylon.prefab";
            ChebsRecipeConfig.ObjectName = MethodBase.GetCurrentMethod().DeclaringType.Name;

            ChebsRecipeConfig.Allowed = plugin.ModConfig(ChebsRecipeConfig.ObjectName, "NeckroGathererPylonAllowed", true,
                "Whether making a the pylon is allowed or not.", plugin.BoolValue, true);

            ChebsRecipeConfig.CraftingCost = plugin.ModConfig(ChebsRecipeConfig.ObjectName, "NeckroGathererPylonBuildCosts", 
                ChebsRecipeConfig.DefaultRecipe, 
                "Materials needed to build the pylon. None or Blank will use Default settings. Format: " + ChebsRecipeConfig.RecipeValue, 
                null, true);

            SpawnInterval = plugin.ModConfig(ChebsRecipeConfig.ObjectName, "NeckroGathererSpawnInterval", 60f,
                "How often the pylon will attempt to create a Neckro Gatherer.", plugin.FloatQuantityValue, true);

            NeckTailsConsumedPerSpawn = plugin.ModConfig(ChebsRecipeConfig.ObjectName, "NeckroGathererCreationCost", 1,
                "How many Neck Tails get consumed when creating a Neckro Gatherer.", plugin.IntQuantityValue, true);
        }

#pragma warning disable IDE0051 // Remove unused private members
        private void Awake()
#pragma warning restore IDE0051 // Remove unused private members
        {
            Container = GetComponent<Container>();
            StartCoroutine(SpawnNeckros());
        }
        
        private IEnumerator SpawnNeckros()
        {
            yield return new WaitWhile(() => ZInput.instance == null);

            // prevent coroutine from doing its thing while the pylon isn't
            // yet constructed
            Piece piece = GetComponent<Piece>();
            yield return new WaitWhile(() => !piece.IsPlacedByPlayer());

            while (true)
            {
                yield return new WaitForSeconds(SpawnInterval.Value);

                SpawnNeckro();
            }
        }

        protected GameObject SpawnNeckro()
        {
            int neckTailsInInventory = Container.GetInventory().CountItems("$item_necktail");
            if (neckTailsInInventory < NeckTailsConsumedPerSpawn.Value) return null;

            Container.GetInventory().RemoveItem("$item_necktail", NeckTailsConsumedPerSpawn.Value);

            int quality = 1;

            string prefabName = "ChebGonaz_NeckroGatherer";
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (!prefab)
            {
                Logger.LogError($"spawning {prefabName} failed!");
                return null;
            }

            GameObject spawnedChar = Instantiate(
                prefab,
                transform.position + transform.forward * 2f + Vector3.up,
                Quaternion.identity);

            Character character = spawnedChar.GetComponent<Character>();
            character.SetLevel(quality);

            return spawnedChar;
        }
    }
}
