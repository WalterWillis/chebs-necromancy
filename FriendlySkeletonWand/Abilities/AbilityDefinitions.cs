﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FriendlySkeletonWand.Abilities
{
    public static class AbilityDefinitions
    {
        public static AbilityConfig Config;
        public static readonly Dictionary<string, AbilityDefinition> Abilities = new Dictionary<string, AbilityDefinition>();

        public static void Initialize(AbilityConfig config)
        {
            Config = config;

            Abilities.Clear();
            foreach (var def in Config.Abilities)
            {
                Abilities.Add(def.ID, def);
            }
        }

        public static bool TryGetAbilityDef(string abilityID, out AbilityDefinition abilityDef)
        {
            return Abilities.TryGetValue(abilityID, out abilityDef);
        }
    }
}
