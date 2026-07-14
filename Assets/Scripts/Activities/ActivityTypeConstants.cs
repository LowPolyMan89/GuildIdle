using System;
using GuildIdle.Core;
using Core = GuildIdle.Core;

namespace GuildIdle.Activities
{
    public static class RequirementType
    {
        public const string HeroLevel = Core.RequirementType.HeroLevel;
        public const string SkillLevel = Core.RequirementType.SkillLevel;
        public const string Resource = Core.RequirementType.Resource;
        public const string Building = Core.RequirementType.Building;
        public const string LocationUnlocked = Core.RequirementType.LocationUnlocked;
        public const string HeroClass = Core.RequirementType.HeroClass;
        public const string ItemEquipped = Core.RequirementType.ItemEquipped;
        public const string QuestCompleted = Core.RequirementType.QuestCompleted;
        public const string ActivityCompleted = Core.RequirementType.ActivityCompleted;
        public const string Item = Core.RequirementType.Item;
        public const string BuildingLevel = Core.RequirementType.BuildingLevel;
        public const string HeroAvailable = Core.RequirementType.HeroAvailable;
        public const string ItemCount = Core.RequirementType.ItemCount;
        public const string Currency = Core.RequirementType.Currency;

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out RequirementTypeEnum result) =>
            ActivityTypeParser.TryParseRequirementType(value, out result);
    }

    public static class RewardType
    {
        public const string Resource = Core.RewardType.Resource;
        public const string Gold = Core.RewardType.Gold;
        public const string Item = Core.RewardType.Item;
        public const string SkillExp = Core.RewardType.SkillExp;
        public const string HeroExp = Core.RewardType.HeroExp;
        public const string LootTable = Core.RewardType.LootTable;
        public const string Reputation = Core.RewardType.Reputation;
        public const string UnlockLocation = Core.RewardType.UnlockLocation;
        public const string UnlockBuilding = Core.RewardType.UnlockBuilding;
        public const string Hero = Core.RewardType.Hero;
        public const string Equipment = Core.RewardType.Equipment;
        public const string BuildingUnlock = Core.RewardType.BuildingUnlock;
        public const string MapAccess = Core.RewardType.MapAccess;
        public const string Consumable = Core.RewardType.Consumable;
        public const string Recipe = Core.RewardType.Recipe;
        public const string Currency = Core.RewardType.Currency;
        public const string Building = Core.RewardType.Building;
        public const string Location = Core.RewardType.Location;

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out RewardTypeEnum result) =>
            ActivityTypeParser.TryParseRewardType(value, out result);

        public static bool TryParseLegacy(string value, out RewardTypeEnum result) =>
            ActivityTypeParser.TryParseRewardTypeLegacy(value, out result);
    }

    public static class DropType
    {
        public const string Item = Core.DropType.Item;
        public const string Resource = Core.DropType.Resource;
        public const string Gold = Core.DropType.Gold;

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out DropTypeEnum result) =>
            ActivityTypeParser.TryParseDropType(value, out result);
    }

    public static class TriggerType
    {
        public const string StartCombat = Core.TriggerType.StartCombat;
        public const string UnlockLocation = Core.TriggerType.UnlockLocation;
        public const string UnlockBuilding = Core.TriggerType.UnlockBuilding;
        public const string AddReputation = Core.TriggerType.AddReputation;
        public const string StartActivity = Core.TriggerType.StartActivity;
        public const string CompleteQuest = Core.TriggerType.CompleteQuest;
        public const string GiveItem = Core.TriggerType.GiveItem;

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out TriggerTypeEnum result) =>
            ActivityTypeParser.TryParseTriggerType(value, out result);
    }

    public static class GrantMoment
    {
        public const string OnStart = Core.GrantMoment.OnStart;
        public const string OnCycle = Core.GrantMoment.OnCycle;
        public const string OnComplete = Core.GrantMoment.OnComplete;
        public const string OnFirstComplete = Core.GrantMoment.OnFirstComplete;

        public static bool Matches(string value, string constant) =>
            string.Equals(value, constant, StringComparison.OrdinalIgnoreCase);

        public static bool TryParse(string value, out GrantMomentEnum result) =>
            ActivityTypeParser.TryParseGrantMoment(value, out result);
    }
}
