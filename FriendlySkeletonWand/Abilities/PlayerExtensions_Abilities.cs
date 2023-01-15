﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FriendlySkeletonWand.Abilities
{
    public static class PlayerExtensions_Abilities
    {
        public static Ability GetAbility(this Player player, string abilityID)
        {
            var abilityController = player.GetComponent<AbilityController>();
            if (abilityController != null)
            {
                return abilityController.GetCurrentAbility(abilityID);
            }

            return null;
        }

        public static T GetAbility<T>(this Player player, string abilityID) where T : Ability
        {
            return player.GetAbility(abilityID) as T;
        }
    }
}
