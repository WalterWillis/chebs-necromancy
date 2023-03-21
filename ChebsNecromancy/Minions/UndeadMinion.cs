﻿using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using ChebsNecromancy.Items;
using Jotunn.Managers;
using UnityEngine;
using Logger = Jotunn.Logger;

namespace ChebsNecromancy.Minions
{
    internal class UndeadMinion : MonoBehaviour
    {
        // we add this component to the creatures we create in the mod
        // so that we can use .GetComponent<UndeadMinion>()
        // to determine whether a creature was created by the mod, or
        // whether it was created by something else.
        //
        // This allows us to only call wait/follow/whatever on minions
        // that the mod has created. The component is lost between sessions
        // so it must be checked for in Awake and readded (see harmony patching).

        public enum CleanupType
        {
            None,
            Time,
            Logout,
        }

        public enum DropType
        {
            Nothing,
            JustResources,
            Everything,
        }

        public enum State
        {
            Waiting,
            Roaming,
            Following,
        }

        public enum ArmorType
        {
            None,
            Leather,
            LeatherTroll,
            LeatherWolf,
            LeatherLox,
            Bronze,
            Iron,
            BlackMetal,
        }

        public bool canBeCommanded = true;

        public static ConfigEntry<CleanupType> CleanupAfter;
        public static ConfigEntry<int> CleanupDelay;
        public static ConfigEntry<bool> Commandable;
        public static ConfigEntry<float> RoamRange;

        protected float CleanupAt;

        public const string MinionOwnershipZdoKey = "UndeadMinionMaster";
        public const string MinionDropsZdoKey = "UndeadMinionDrops";
        public const string MinionWaitPosZdoKey = "UndeadMinionWaitPosition";
        public const string MinionWaitObjectName = "UndeadMinionWaitPositionObject";
        public const string MinionCreatedAtLevelKey = "UndeadMinionCreatedAtLevel";
        public const string MinionEmblemZdoKey = "UndeadMinionEmblem";
        
        public int createdOrder;

        #region DeathCrates
        private static List<Transform> _deathCrates = new();

        public void DepositIntoNearbyDeathCrate(CharacterDrop characterDrop, float range=15f)
        {
            // cleanup
            _deathCrates.RemoveAll(t => t == null);

            // try depositing everything into existing containers
            var deathCrates = _deathCrates
                .OrderBy(t => Vector3.Distance(t.position, transform.position) < range);
            foreach (var t in deathCrates)
            {
                if (characterDrop.m_drops.Count < 1) break;
                if (!t.TryGetComponent(out Container container)) continue;

                var inv = container.GetInventory();
                if (inv is null) continue;

                var dropsRemaining = new List<CharacterDrop.Drop>();
                foreach (var drop in characterDrop.m_drops)
                {
                    if (inv.CanAddItem(drop.m_prefab))
                    {
                        inv.AddItem(drop.m_prefab, drop.m_amountMax);
                    }
                    else
                    {
                        dropsRemaining.Add(drop);
                    }
                }

                characterDrop.m_drops = dropsRemaining;
            }
            
            // if items remain undeposited, create a new crate for them
            if (characterDrop.m_drops.Count < 1) return;
            var crate = CreateDeathCrate();
            if (crate != null)
            {
                // warning: we mustn't ever exceed the maximum storage capacity
                // of the crate. Not a problem right now, but could be in the future
                // if the ingredients exceed 4. Right now, can only be 3, so it's fine.
                // eg. bones, meat, ingot (draugr) OR bones, ingot, surtling core (skele)
                var inv = crate.GetInventory();
                var unsuccessful = new List<CharacterDrop.Drop>();
                characterDrop.m_drops.ForEach(drop =>
                {
                    if (!inv.AddItem(drop.m_prefab, drop.m_amountMax))
                    {
                        unsuccessful.Add(drop);
                    }
                });
                characterDrop.m_drops = unsuccessful;
            }
        }
        
        private Container CreateDeathCrate()
        {
            // use vanilla cargo crate -> same as a karve/longboat drops
            var cratePrefab = ZNetScene.instance.GetPrefab("CargoCrate");
            var result = Instantiate(cratePrefab, transform.position + Vector3.up, Quaternion.identity);
            _deathCrates.Add(result.transform);
            return result.GetComponent<Container>();
        }
        #endregion

        #region CleanupAfterLogout
        private const float NextPlayerOnlineCheckInterval = 15f;
        private float nextPlayerOnlineCheckAt;
        #endregion

        private Vector3 StatusRoaming => Vector3.negativeInfinity;
        private Vector3 StatusFollowing => Vector3.positiveInfinity;
        
        public static ArmorType DetermineArmorType()
        {
            Player player = Player.m_localPlayer;
            
            int blackMetalInInventory = player.GetInventory().CountItems("$item_blackmetal");
            if (blackMetalInInventory >= BasePlugin.ArmorBlackIronRequiredConfig.Value)
            {
                return ArmorType.BlackMetal;
            }
            
            int ironInInventory = player.GetInventory().CountItems("$item_iron");
            if (ironInInventory >= BasePlugin.ArmorIronRequiredConfig.Value)
            {
                return ArmorType.Iron;
            }
            
            int bronzeInInventory = player.GetInventory().CountItems("$item_bronze");
            if (bronzeInInventory >= BasePlugin.ArmorBronzeRequiredConfig.Value)
            {
                return ArmorType.Bronze;
            }
            
            int trollHideInInventory = player.GetInventory().CountItems("$item_trollhide");
            if (trollHideInInventory >= BasePlugin.ArmorLeatherScrapsRequiredConfig.Value)
            {
                return ArmorType.LeatherTroll;
            }
            
            int wolfHideInInventory = player.GetInventory().CountItems("$item_wolfpelt");
            if (wolfHideInInventory >= BasePlugin.ArmorLeatherScrapsRequiredConfig.Value)
            {
                return ArmorType.LeatherWolf;
            }
            
            int loxHideInInventory = player.GetInventory().CountItems("$item_loxpelt");
            if (loxHideInInventory >= BasePlugin.ArmorLeatherScrapsRequiredConfig.Value)
            {
                return ArmorType.LeatherLox;
            }
            
            // todo: expose these options to config
            var leatherItemTypes = new List<string>()
            {
                "$item_leatherscraps",
                "$item_deerhide",
                "$item_scalehide"
            };
            
            foreach (var leatherItem in leatherItemTypes)
            {
                var leatherItemsInInventory = player.GetInventory().CountItems(leatherItem);
                if (leatherItemsInInventory >= BasePlugin.ArmorLeatherScrapsRequiredConfig.Value)
                {
                    return ArmorType.Leather;
                }
            }

            return ArmorType.None;
        }

        public static void CreateConfigs(BaseUnityPlugin plugin)
        {
            CleanupAfter = plugin.Config.Bind("UndeadMinion (Server Synced)", "CleanupAfter",
                CleanupType.None, new ConfigDescription("Whether a minion should be cleaned up or not.", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            CleanupDelay = plugin.Config.Bind("UndeadMinion (Server Synced)", "CleanupDelay",
                300, new ConfigDescription("The delay, in seconds, after which a minion will be destroyed. It has no effect if CleanupAfter is set to None.", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            Commandable = plugin.Config.Bind("UndeadMinion (Client)", "Commandable",
                true, new ConfigDescription("If true, minions can be commanded individually with E (or equivalent) keybind."));
            RoamRange = plugin.Config.Bind("UndeadMinion (Client)", "RoamRange",
                10f, new ConfigDescription("How far a unit is allowed to roam from its current position."));
        }

        public virtual void Awake()
        {
            var tameable = GetComponent<Tameable>();
            if (tameable != null)
            {
                // let the minions generate a little necromancy XP for their master
                tameable.m_levelUpOwnerSkill = SkillManager.Instance.GetSkill(BasePlugin.NecromancySkillIdentifier).m_skill;

                tameable.m_commandable = Commandable.Value;
            }

            if (CleanupAfter.Value == CleanupType.Time)
            {
                CleanupAt = Time.time + CleanupDelay.Value;
            }
            else if (CleanupAfter.Value == CleanupType.Logout)
            {
                // check if player is still online every X seconds
                nextPlayerOnlineCheckAt = Time.time + NextPlayerOnlineCheckInterval;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // ignore collision with player

            Character character = collision.gameObject.GetComponent<Character>();
            if (character != null
                && character.m_faction == Character.Faction.Players
                && character.GetComponent<UndeadMinion>() == null) // allow collision between minions
            {
                Physics.IgnoreCollision(collision.gameObject.GetComponent<Collider>(), GetComponent<Collider>());
                return;
            }
        }

        private void Update()
        {
            if (CleanupAt > 0
                && Time.time > CleanupAt 
                && CleanupAfter.Value != CleanupType.None)
            {
                //Jotunn.Logger.LogInfo($"Cleaning up {name} because current time {Time.time} > {cleanupAt}");
                Kill();

                // check again in 5 seconds rather than spamming every frame with Kill requests. In
                // 99.9% of cases the 2nd check will never occur because the character will be dead
                CleanupAt += 5;
            }

            if (nextPlayerOnlineCheckAt > 0
                && Time.time > nextPlayerOnlineCheckAt)
            {
                bool playerOnline = Player.GetAllPlayers().Find(player => BelongsToPlayer(player.GetPlayerName()));
                if (!playerOnline)
                {
                    CleanupAt = Time.time + CleanupDelay.Value;
                }
                else
                {
                    CleanupAt = 0;
                }
                nextPlayerOnlineCheckAt = Time.time + NextPlayerOnlineCheckInterval;
            }
        }

        public void Kill()
        {
            if (TryGetComponent(out Character character))
            {
                if (!character.IsDead()) character.SetHealth(0);
            }
            else
            {
                Logger.LogError($"Cannot kill {name} because it has no Character component.");
            }
        }

        #region MinionMasterZDO
        public string UndeadMinionMaster
        {
            get => TryGetComponent(out ZNetView zNetView) ? zNetView.GetZDO().GetString(MinionOwnershipZdoKey) : "";
            set
            {
                if (TryGetComponent(out ZNetView zNetView))
                {
                    zNetView.GetZDO().Set(MinionOwnershipZdoKey, value);
                }
                else
                {
                    Logger.LogError($"Cannot SetUndeadMinionMaster to {value} because it has no ZNetView component.");
                }
            }
        }

        public bool BelongsToPlayer(string playerName)
        {
            return TryGetComponent(out ZNetView zNetView) 
                   && zNetView.GetZDO().GetString(MinionOwnershipZdoKey, "")
                       .ToLower()
                       .Trim()
                       .Equals(playerName.ToLower().Trim());
        }
        #endregion
        #region DropsZDO
        public void RecordDrops(CharacterDrop characterDrop)
        {
            // the component won't be remembered by the game on logout because
            // only what is on the prefab is remembered. Even changes to the prefab
            // aren't remembered. So we must write what we're dropping into
            // the ZDO as well and then read & restore this on Awake
            if (TryGetComponent(out ZNetView zNetView))
            {
                string dropsList = "";
                List<string> drops = new List<string>();
                characterDrop.m_drops.ForEach(drop => drops.Add($"{drop.m_prefab.name}:{drop.m_amountMax}"));
                dropsList = string.Join(",", drops);
                //Jotunn.Logger.LogInfo($"Drops list: {dropsList}");
                zNetView.GetZDO().Set(MinionDropsZdoKey, string.Join(",", dropsList));
            }
            else
            {
                Logger.LogError($"Cannot record drops because {name} has no ZNetView component.");
            }
        }

        public void RestoreDrops()
        {
            // the component won't be remembered by the game on logout because
            // only what is on the prefab is remembered. Even changes to the prefab
            // aren't remembered. So we must write what we're dropping into
            // the ZDO as well and then read & restore this on Awake
            if (TryGetComponent(out ZNetView zNetView))
            {
                if (gameObject.GetComponent<CharacterDrop>() != null)
                {
                    // abort - if it's already there, don't add it twice
                    return;
                }

                string minionDropsZdoValue = zNetView.GetZDO().GetString(MinionDropsZdoKey, "");
                if (minionDropsZdoValue == "")
                {
                    // abort - there's no drops record -> naked minion
                    return;
                }

                CharacterDrop characterDrop = gameObject.AddComponent<CharacterDrop>();
                List<string> dropsList = new List<string>(minionDropsZdoValue.Split(','));
                dropsList.ForEach(dropString =>
                {
                    string[] splut = dropString.Split(':');

                    string prefabName = splut[0];
                    int amount = int.Parse(splut[1]);

                    characterDrop.m_drops.Add(new CharacterDrop.Drop
                    {
                        m_prefab = ZNetScene.instance.GetPrefab(prefabName),
                        m_onePerPlayer = true,
                        m_amountMin = amount,
                        m_amountMax = amount,
                        m_chance = 1f
                    });
                });
            }
            else
            {
                Logger.LogError($"Cannot record drops because {name} has no ZNetView component.");
            }
        }
        #endregion
        #region WaitPositionZDO

        public State Status
        {
            get
            {
                var waitPos = GetWaitPosition();
                if (waitPos.Equals(StatusFollowing)) return State.Following;
                return waitPos.Equals(StatusRoaming) ? State.Roaming : State.Waiting;
            }
        }
        protected void RecordWaitPosition(Vector3 waitPos)
        {
            // waitPos == some position = wait at that position
            // waitPos == StatusFollow = follow owner
            // waitPos == StatusRoam = roam
            if (TryGetComponent(out ZNetView zNetView))
            {
                zNetView.GetZDO().Set(MinionWaitPosZdoKey, waitPos);
            }
            else
            {
                Logger.LogError($"Cannot RecordWaitPosition {waitPos} because it has no ZNetView component.");
            }
        }

        protected Vector3 GetWaitPosition()
        {
            if (TryGetComponent(out ZNetView zNetView))
            {
                return zNetView.GetZDO().GetVec3(MinionWaitPosZdoKey, StatusRoaming);
            }

            Logger.LogError($"Cannot GetWaitPosition because it has no ZNetView component.");
            return StatusRoaming;
        }

        public void RoamFollowOrWait()
        {
            Vector3 waitPos = GetWaitPosition();
            // we cant compare negative infinity with == because unity's == returns true for vectors that are almost
            // equal.
            if (waitPos.Equals(StatusFollowing))
            {
                // Try to find player that minion belongs to. If found, follow. Otherwise roam
                Player player = Player.GetAllPlayers().Find(p => BelongsToPlayer(p.GetPlayerName()));
                if (player == null)
                {
                    Logger.LogError($"{name} should be following but has no associated player. Roaming instead.");
                    Roam();
                    return;
                }
                Follow(player.gameObject);
                return;
            }
            
            if (waitPos.Equals(StatusRoaming))
            {
                Roam();
                return;
            }

            if (!TryGetComponent(out MonsterAI monsterAI))
            {
                Logger.LogError($"{name} cannot WaitAtRecordedPosition because it has no MonsterAI component.");
                return;
            }

            // create a temporary object. This has no ZDO so will be cleaned up
            // after the session ends
            GameObject waitObject = new GameObject(MinionWaitObjectName);
            waitObject.transform.position = waitPos;
            monsterAI.m_randomMoveRange = 0;
            monsterAI.SetFollowTarget(waitObject);
        }
        #endregion

        public void Follow(GameObject followObject)
        {
            if (!TryGetComponent(out MonsterAI monsterAI))
            {
                Logger.LogError($"Cannot Follow because it has no MonsterAI component.");
                return;
            }
            // clear out current wait object if it exists
            GameObject currentFollowTarget = monsterAI.GetFollowTarget();
            if (currentFollowTarget != null && currentFollowTarget.name == MinionWaitObjectName)
            {
                Destroy(currentFollowTarget);
            }
            // follow
            RecordWaitPosition(StatusFollowing);
            monsterAI.SetFollowTarget(followObject);
        }

        public void Wait(Vector3 waitPosition)
        {
            RecordWaitPosition(waitPosition);
            RoamFollowOrWait();
        }

        public void Roam()
        {
            RecordWaitPosition(StatusRoaming);
            if (!TryGetComponent(out MonsterAI monsterAI))
            {
                Logger.LogError($"Cannot Roam because {name} has no MonsterAI component!");
                return;
            }
            // clear out current wait object if it exists
            GameObject currentFollowTarget = monsterAI.GetFollowTarget();
            if (currentFollowTarget != null && currentFollowTarget.name == MinionWaitObjectName)
            {
                Destroy(currentFollowTarget);
            }
            monsterAI.m_randomMoveRange = RoamRange.Value;
            monsterAI.SetFollowTarget(null);
        }

        public static T FindClosest<T>(Transform targetTransform, float radius, int mask, Func<T, bool> where, bool interactable) where T : Component
        {
            return Physics.OverlapSphere(targetTransform.position, radius, mask)
                .Where(c => c.GetComponentInParent<T>() != null) // check if desired component exists
                .Select(c => c.GetComponentInParent<T>()) // get the component we want (e.g. ItemDrop)
                .Where(c => !interactable || (c.TryGetComponent(out ZNetView znv) && znv.IsValid())) // only interactable objects
                .Where(where) // allow the caller to specify additional constraints (e.g. drop => drop.GetTimeSinceSpawned() > 4)
                .OrderBy(t => Vector3.Distance(t.transform.position, targetTransform.position)) // sort to find closest
                .FirstOrDefault(); // return closest
        }

        #region CreatedAtLevelZDO
        public void SetCreatedAtLevel(float necromancyLevel)
        {
            // We store the level the minion was created at so it can be scaled
            // correctly in the Awake function
            if (!TryGetComponent(out ZNetView zNetView))
            {
                Logger.LogError($"Cannot SetCreatedAtLevel to {necromancyLevel} because it has no ZNetView component.");
                return;
            }
            zNetView.GetZDO().Set(MinionCreatedAtLevelKey, necromancyLevel);
        }

        protected float GetCreatedAtLevel()
        {
            if (!TryGetComponent(out ZNetView zNetView))
            {
                Logger.LogError($"Cannot read {MinionCreatedAtLevelKey} because it has no ZNetView component.");
                return 1f;
            }

            return zNetView.GetZDO().GetFloat(MinionCreatedAtLevelKey, 1f);
        }
        #endregion
        
        public static void CountActive<T>(int minionLimitIncrementsEveryXLevels, int maxMinions) where T : UndeadMinion
        {
            Logger.LogInfo($"increments: {minionLimitIncrementsEveryXLevels}, max: {maxMinions}");
            // Get all active skeleton minions that belong to the local player
            var minions = Character.GetAllCharacters()
                .Where(c => !c.IsDead())
                .Select(c => (c.GetComponent<T>(), c))
                .Where(t => t.Item1 != null && t.Item1.BelongsToPlayer(Player.m_localPlayer.GetPlayerName()))
                .OrderByDescending(t => t.Item1.createdOrder)
                .ToList();

            // Determine the maximum number of minions the player can have
            var necromancySkill = SkillManager.Instance.GetSkill(BasePlugin.NecromancySkillIdentifier).m_skill;
            var playerNecromancyLevel = Player.m_localPlayer.GetSkillLevel(necromancySkill);
            var bonusMinions = minionLimitIncrementsEveryXLevels > 0
                ? Mathf.FloorToInt(playerNecromancyLevel / minionLimitIncrementsEveryXLevels)
                : 0;
            var maxMinionsPlusBonus = maxMinions + bonusMinions;
            maxMinionsPlusBonus -= 1;

            Logger.LogInfo($"maxMinionsPlusBonus: {maxMinionsPlusBonus}");
            
            // Kill off surplus minions
            for (var i = maxMinionsPlusBonus; i < minions.Count; i++)
            {
                minions[i].Item2.SetHealth(0);
            }
        }

        #region EmblemZDO
        public string Emblem
        {
            get => TryGetComponent(out ZNetView zNetView) ? zNetView.GetZDO().GetString(MinionEmblemZdoKey) : InternalName.GetName(NecromancerCape.Emblem.Blank);
            set
            {
                if (TryGetComponent(out ZNetView zNetView))
                {
                    zNetView.GetZDO().Set(MinionEmblemZdoKey, value);
                }
                else
                {
                    Logger.LogError($"Cannot set emblem to {value} because it has no ZNetView component.");
                }
            }
        }
        #endregion
    }
}
