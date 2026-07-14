using System;
using GuildIdle.Core;

namespace GuildIdle.Activities
{
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

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out RequirementTypeEnum result) =>
            ActivityTypeParser.TryParseRequirementType(value, out result);
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

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out RewardTypeEnum result) =>
            ActivityTypeParser.TryParseRewardType(value, out result);

        public static bool TryParseLegacy(string value, out RewardTypeEnum result) =>
            ActivityTypeParser.TryParseRewardTypeLegacy(value, out result);
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

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out DropTypeEnum result) =>
            ActivityTypeParser.TryParseDropType(value, out result);
    }

    public static class TriggerType
    {
        public const string ActivityCompleted = "ActivityCompleted";
        public const string BuildingLevel = "BuildingLevel";
        public const string HeroAvailable = "HeroAvailable";
        public const string LocationUnlocked = "LocationUnlocked";
        public const string ItemCount = "ItemCount";

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out TriggerTypeEnum result) =>
            ActivityTypeParser.TryParseTriggerType(value, out result);
    }

    public static class GrantMoment
    {
        public const string OnStart = "OnStart";
        public const string OnCycle = "OnCycle";
        public const string OnComplete = "OnComplete";
        public const string OnFirstComplete = "OnFirstComplete";

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out GrantMomentEnum result) =>
            ActivityTypeParser.TryParseGrantMoment(value, out result);
    }
}