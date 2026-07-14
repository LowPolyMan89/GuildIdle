using System;

namespace GuildIdle.Core
{
    // ===== Enums =====

    public enum RequirementTypeEnum
    {
        SkillLevel,
        LocationUnlocked,
        BuildingLevel,
        Building,
        ItemCount,
        Item,
        Currency,
        ActivityCompleted,
        HeroAvailable,
        ItemEquipped,
    }

    public enum RewardTypeEnum
    {
        Resource,
        Item,
        Equipment,
        Consumable,
        Recipe,
        SkillExp,
        Currency,
        Gold,
        Hero,
        Building,
        Location,
        LootTable,
    }

    public enum DropTypeEnum
    {
        Resource,
        Item,
        Equipment,
        Consumable,
        Recipe,
        Currency,
        Gold,
    }

    public enum TriggerTypeEnum
    {
        ActivityCompleted,
        BuildingLevel,
        HeroAvailable,
        LocationUnlocked,
        ItemCount,
    }

    public enum GrantMomentEnum
    {
        OnStart,
        OnCycle,
        OnComplete,
        OnFirstComplete,
    }

    public enum LootRollModeEnum
    {
        GuaranteedAll,
        WeightedOne,
        WeightedMany,
    }

    // ===== String constants (for config data that is always strings) =====

    public static class RequirementType
    {
        public const string SkillLevel = "SkillLevel";
        public const string LocationUnlocked = "LocationUnlocked";
        public const string BuildingLevel = "BuildingLevel";
        public const string Building = "Building";
        public const string ItemCount = "ItemCount";
        public const string Item = "Item";
        public const string Currency = "Currency";
        public const string ActivityCompleted = "ActivityCompleted";
        public const string HeroAvailable = "HeroAvailable";
        public const string ItemEquipped = "ItemEquipped";
    }

    public static class RewardType
    {
        public const string Resource = "Resource";
        public const string Item = "Item";
        public const string Equipment = "Equipment";
        public const string Consumable = "Consumable";
        public const string Recipe = "Recipe";
        public const string SkillExp = "SkillExp";
        public const string Currency = "Currency";
        public const string Gold = "Gold";
        public const string Hero = "Hero";
        public const string Building = "Building";
        public const string Location = "Location";
        public const string LootTable = "LootTable";
    }

    public static class DropType
    {
        public const string Resource = "Resource";
        public const string Item = "Item";
        public const string Equipment = "Equipment";
        public const string Consumable = "Consumable";
        public const string Recipe = "Recipe";
        public const string Currency = "Currency";
        public const string Gold = "Gold";
    }

    public static class TriggerType
    {
        public const string ActivityCompleted = "ActivityCompleted";
        public const string BuildingLevel = "BuildingLevel";
        public const string HeroAvailable = "HeroAvailable";
        public const string LocationUnlocked = "LocationUnlocked";
        public const string ItemCount = "ItemCount";
    }

    public static class GrantMoment
    {
        public const string OnStart = "OnStart";
        public const string OnCycle = "OnCycle";
        public const string OnComplete = "OnComplete";
        public const string OnFirstComplete = "OnFirstComplete";
    }

    public static class LootRollMode
    {
        public const string GuaranteedAll = "GuaranteedAll";
        public const string WeightedOne = "WeightedOne";
        public const string WeightedMany = "WeightedMany";

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);
    }

    // ===== Parser =====

    public static class ActivityTypeParser
    {
        // --- RequirementType ---

        public static bool TryParseRequirementType(string value, out RequirementTypeEnum result)
        {
            return Enum.TryParse(value, ignoreCase: true, out result);
        }

        // --- RewardType ---

        public static bool TryParseRewardType(string value, out RewardTypeEnum result)
        {
            return Enum.TryParse(value, ignoreCase: true, out result);
        }

        /// <summary>
        /// Parses reward type with legacy name mapping.
        /// "BuildingUnlock" → RewardTypeEnum.Building
        /// "MapAccess" → RewardTypeEnum.Location
        /// "UnlockBuilding" → RewardTypeEnum.Building
        /// "UnlockLocation" → RewardTypeEnum.Location
        /// </summary>
        public static bool TryParseRewardTypeLegacy(string value, out RewardTypeEnum result)
        {
            if (string.IsNullOrEmpty(value))
            {
                result = default;
                return false;
            }

            switch (value)
            {
                case "BuildingUnlock":
                case "UnlockBuilding":
                    result = RewardTypeEnum.Building;
                    return true;
                case "MapAccess":
                case "UnlockLocation":
                    result = RewardTypeEnum.Location;
                    return true;
                default:
                    return Enum.TryParse(value, ignoreCase: true, out result);
            }
        }

        // --- DropType ---

        public static bool TryParseDropType(string value, out DropTypeEnum result)
        {
            return Enum.TryParse(value, ignoreCase: true, out result);
        }

        // --- TriggerType ---

        public static bool TryParseTriggerType(string value, out TriggerTypeEnum result)
        {
            return Enum.TryParse(value, ignoreCase: true, out result);
        }

        // --- GrantMoment ---

        public static bool TryParseGrantMoment(string value, out GrantMomentEnum result)
        {
            return Enum.TryParse(value, ignoreCase: true, out result);
        }

        // --- LootRollMode ---

        public static bool TryParseLootRollMode(string value, out LootRollModeEnum result)
        {
            return Enum.TryParse(value, ignoreCase: true, out result);
        }
    }
}