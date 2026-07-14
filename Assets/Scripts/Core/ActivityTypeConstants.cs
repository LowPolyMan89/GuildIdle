using System;

namespace GuildIdle.Core
{
    public enum RequirementTypeEnum
    {
        HeroLevel,
        SkillLevel,
        Resource,
        Building,
        LocationUnlocked,
        HeroClass,
        ItemEquipped,
        QuestCompleted,
        ActivityCompleted,
        Item,
        BuildingLevel,
        HeroAvailable,

        // Existing MVP values used by runtime and test data.
        ItemCount,
        Currency,
    }

    public enum RewardTypeEnum
    {
        Resource,
        Gold,
        Item,
        SkillExp,
        HeroExp,
        LootTable,
        Reputation,
        UnlockLocation,
        UnlockBuilding,
        Hero,
        Equipment,
        Consumable,
        Recipe,

        // Existing MVP value used by runtime data.
        Currency,
    }

    public enum DropTypeEnum
    {
        Item,
        Resource,
        Gold,
    }

    public enum TriggerTypeEnum
    {
        StartCombat,
        UnlockLocation,
        UnlockBuilding,
        AddReputation,
        StartActivity,
        CompleteQuest,
        GiveItem,
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

    public static class RequirementType
    {
        public const string HeroLevel = "HeroLevel";
        public const string SkillLevel = "SkillLevel";
        public const string Resource = "Resource";
        public const string Building = "Building";
        public const string LocationUnlocked = "LocationUnlocked";
        public const string HeroClass = "HeroClass";
        public const string ItemEquipped = "ItemEquipped";
        public const string QuestCompleted = "QuestCompleted";
        public const string ActivityCompleted = "ActivityCompleted";
        public const string Item = "Item";
        public const string BuildingLevel = "BuildingLevel";
        public const string HeroAvailable = "HeroAvailable";

        // Existing MVP values used by runtime and test data.
        public const string ItemCount = "ItemCount";
        public const string Currency = "Currency";
    }

    public static class RewardType
    {
        public const string Resource = "Resource";
        public const string Gold = "Gold";
        public const string Item = "Item";
        public const string SkillExp = "SkillExp";
        public const string HeroExp = "HeroExp";
        public const string LootTable = "LootTable";
        public const string Reputation = "Reputation";
        public const string UnlockLocation = "UnlockLocation";
        public const string UnlockBuilding = "UnlockBuilding";
        public const string Hero = "Hero";
        public const string Equipment = "Equipment";
        public const string BuildingUnlock = "BuildingUnlock";
        public const string MapAccess = "MapAccess";
        public const string Consumable = "Consumable";
        public const string Recipe = "Recipe";

        // Existing MVP aliases retained at the parser boundary.
        public const string Currency = "Currency";
        public const string Building = "Building";
        public const string Location = "Location";
    }

    public static class DropType
    {
        public const string Item = "Item";
        public const string Resource = "Resource";
        public const string Gold = "Gold";
    }

    public static class TriggerType
    {
        public const string StartCombat = "StartCombat";
        public const string UnlockLocation = "UnlockLocation";
        public const string UnlockBuilding = "UnlockBuilding";
        public const string AddReputation = "AddReputation";
        public const string StartActivity = "StartActivity";
        public const string CompleteQuest = "CompleteQuest";
        public const string GiveItem = "GiveItem";
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

    public static class ActivityTypeParser
    {
        public static bool TryParseRequirementType(string value, out RequirementTypeEnum result)
        {
            return TryParseDefined(value, out result);
        }

        public static bool TryParseRewardType(string value, out RewardTypeEnum result)
        {
            if (MatchesAny(value, RewardType.UnlockBuilding, RewardType.BuildingUnlock, RewardType.Building))
            {
                result = RewardTypeEnum.UnlockBuilding;
                return true;
            }

            if (MatchesAny(value, RewardType.UnlockLocation, RewardType.MapAccess, RewardType.Location))
            {
                result = RewardTypeEnum.UnlockLocation;
                return true;
            }

            return TryParseDefined(value, out result);
        }

        public static bool TryParseDropType(string value, out DropTypeEnum result)
        {
            return TryParseDefined(value, out result);
        }

        public static bool TryParseTriggerType(string value, out TriggerTypeEnum result)
        {
            return TryParseDefined(value, out result);
        }

        public static bool TryParseGrantMoment(string value, out GrantMomentEnum result)
        {
            return TryParseDefined(value, out result);
        }

        public static bool TryParseLootRollMode(string value, out LootRollModeEnum result)
        {
            return TryParseDefined(value, out result);
        }

        private static bool TryParseDefined<TEnum>(string value, out TEnum result)
            where TEnum : struct
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            foreach (var name in Enum.GetNames(typeof(TEnum)))
            {
                if (!string.Equals(normalized, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                return Enum.TryParse(name, false, out result);
            }

            return false;
        }

        private static bool MatchesAny(string value, params string[] candidates)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            foreach (var candidate in candidates)
            {
                if (string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
