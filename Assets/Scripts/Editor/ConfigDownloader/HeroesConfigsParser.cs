using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class HeroesConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "heroes_configs";
        private const string HeroesSheet = "Heroes";
        private const string GrowthProfilesSheet = "HeroGrowthProfiles";
        private const string GrowthMilestonesSheet = "HeroGrowthMilestones";
        private const string UniqueSkillsSheet = "HeroUniqueSkills";
        private const string SkillEffectsSheet = "HeroSkillEffects";
        private const string PeriodicPlusMilestonesMode = "PeriodicPlusMilestones";
        private const string AddToProfileMode = "AddToProfile";

        private static readonly string[] HeroColumns =
        {
            "SortOrder", "HeroId", "Enabled", "RarityId", "TypeId", "GrowthProfileId",
            "ProfessionIds", "FullSpriteId", "IconSpriteId", "BattleSpriteId",
            "NameId", "DescriptionId", "BaseStrength", "BaseAgility", "BaseIntelligence",
            "BaseLuck", "BaseEndurance"
        };

        private static readonly string[] GrowthProfileColumns =
        {
            "GrowthProfileId", "MaxLevel", "AddStrengthEvery",
            "AddAgilityEvery", "AddIntelligenceEvery", "AddLuckEvery", "AddEnduranceEvery",
            "GenerationMode"
        };

        private static readonly string[] GrowthMilestoneColumns =
        {
            "GrowthProfileId", "Level", "ApplyMode", "RequiredSkillPointsOverride",
            "AddStrength", "AddAgility", "AddIntelligence", "AddLuck", "AddEndurance"
        };

        private static readonly string[] UniqueSkillColumns =
        {
            "HeroId", "SkillId", "Type", "NameId", "DescriptionId", "IconId", "Enabled"
        };

        private static readonly string[] SkillEffectColumns =
        {
            "SkillId", "EffectId", "Trigger", "Condition", "ChancePercent", "Interval",
            "Effect", "Target", "Value", "StackMode", "CooldownSeconds"
        };

        public bool Supports(ConfigSourceSettings source)
        {
            return source != null && string.Equals(source.config_id, ConfigId, StringComparison.OrdinalIgnoreCase);
        }

        public ConfigPipelineReport ParseAndWrite(ConfigSourceSettings source)
        {
            var report = BuildRuntimeJson(source, out var runtimeJson);
            if (!report.Success)
                return report;

            if (!ConfigPipelineUtilities.TryValidateRuntimeOutputPath(source.runtime_json_path, out var fullPath, out var pathError))
            {
                report.ErrorMessage = pathError;
                return report;
            }

            try
            {
                ConfigPipelineUtilities.WriteRuntimeJson(fullPath, runtimeJson);

                AssetDatabase.ImportAsset(ConfigPaths.NormalizeProjectPath(source.runtime_json_path));
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                report.ErrorMessage = $"Could not write runtime JSON '{source.runtime_json_path}': {exception.Message}";
            }

            return report;
        }

        public ConfigPipelineReport Validate(ConfigSourceSettings source)
        {
            return BuildRuntimeJson(source, out _);
        }

        public ConfigPipelineReport BuildRuntimeJson(ConfigSourceSettings source, out string runtimeJson)
        {
            runtimeJson = null;
            var report = new ConfigPipelineReport();

            if (!ConfigPipelineUtilities.TryLoadDownload(source, report, out var download))
                return report;

            var context = new HeroesConfigContext(download, report);
            context.ValidateAndBuild();

            if (!report.Success)
                return report;

            runtimeJson = ConfigRuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private sealed class HeroesConfigContext
        {
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigDownloadedSheet> _sheets = new Dictionary<string, ConfigDownloadedSheet>(StringComparer.OrdinalIgnoreCase);
            private readonly List<HeroConfig> _heroes = new List<HeroConfig>();
            private readonly Dictionary<string, HeroConfig> _heroesById = new Dictionary<string, HeroConfig>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, GrowthProfile> _growthProfiles = new Dictionary<string, GrowthProfile>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<GrowthMilestone>> _milestonesByProfile = new Dictionary<string, List<GrowthMilestone>>(StringComparer.OrdinalIgnoreCase);
            private readonly List<HeroUniqueSkill> _uniqueSkills = new List<HeroUniqueSkill>();
            private readonly List<HeroSkillEffect> _skillEffects = new List<HeroSkillEffect>();
            private readonly HashSet<string> _uniqueSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<string>> _uniqueSkillIdsByHero = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            public HeroesConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
            {
                _report = report;
                foreach (var sheet in download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.sheet_name))
                        continue;

                    _sheets[sheet.sheet_name] = sheet;
                }
            }

            public void ValidateAndBuild()
            {
                ParseGrowthProfiles();
                ParseGrowthMilestones();
                ParseHeroes();
                ParseUniqueSkills();
                ParseSkillEffects();
                ValidateHeroReferences();
            }

            public Dictionary<string, List<Dictionary<string, object>>> BuildRuntimeArrays()
            {
                return new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal)
                {
                    ["heroes"] = BuildHeroes(),
                    ["heroGrowth"] = BuildHeroGrowth(),
                    ["heroUniqueSkills"] = BuildHeroUniqueSkills(),
                    ["heroSkillEffects"] = BuildHeroSkillEffects()
                };
            }

            private void ParseHeroes()
            {
                if (!TryGetRequiredSheet(HeroesSheet, out var sheet) ||
                    !TryReadHeader(sheet, 0, HeroColumns, out var headers))
                    return;

                var seenSortOrders = new Dictionary<int, int>();
                for (var rowIndex = 1; rowIndex < RowCount(sheet); rowIndex++)
                {
                    if (IsBlankRow(sheet, rowIndex))
                        continue;

                    var heroId = Get(sheet, rowIndex, headers, "HeroId");
                    ValidateRequired(HeroesSheet, rowIndex, "HeroId", heroId);
                    ValidateRequired(HeroesSheet, rowIndex, "RarityId", Get(sheet, rowIndex, headers, "RarityId"));
                    ValidateRequired(HeroesSheet, rowIndex, "TypeId", Get(sheet, rowIndex, headers, "TypeId"));
                    ValidateRequired(HeroesSheet, rowIndex, "GrowthProfileId", Get(sheet, rowIndex, headers, "GrowthProfileId"));

                    var sortOrder = ParsePositiveInt(HeroesSheet, rowIndex, headers, "SortOrder");
                    if (seenSortOrders.TryGetValue(sortOrder, out var firstSortRow))
                        AddIssue(HeroesSheet, rowIndex + 1, "SortOrder", sortOrder.ToString(CultureInfo.InvariantCulture), $"Duplicate SortOrder; first declared at row {firstSortRow}.");
                    else if (sortOrder > 0)
                        seenSortOrders[sortOrder] = rowIndex + 1;

                    var enabled = ParseBool(HeroesSheet, rowIndex, headers, "Enabled");
                    var hero = new HeroConfig
                    {
                        RowNumber = rowIndex + 1,
                        SortOrder = sortOrder,
                        HeroId = heroId,
                        Enabled = enabled,
                        RarityId = Get(sheet, rowIndex, headers, "RarityId"),
                        TypeId = Get(sheet, rowIndex, headers, "TypeId"),
                        GrowthProfileId = Get(sheet, rowIndex, headers, "GrowthProfileId"),
                        ProfessionIds = SplitPipe(Get(sheet, rowIndex, headers, "ProfessionIds")),
                        FullSpriteId = Get(sheet, rowIndex, headers, "FullSpriteId"),
                        IconSpriteId = Get(sheet, rowIndex, headers, "IconSpriteId"),
                        BattleSpriteId = Get(sheet, rowIndex, headers, "BattleSpriteId"),
                        NameId = Get(sheet, rowIndex, headers, "NameId"),
                        DescriptionId = Get(sheet, rowIndex, headers, "DescriptionId"),
                        BaseStrength = ParseNonNegativeInt(HeroesSheet, rowIndex, headers, "BaseStrength"),
                        BaseAgility = ParseNonNegativeInt(HeroesSheet, rowIndex, headers, "BaseAgility"),
                        BaseIntelligence = ParseNonNegativeInt(HeroesSheet, rowIndex, headers, "BaseIntelligence"),
                        BaseLuck = ParseNonNegativeInt(HeroesSheet, rowIndex, headers, "BaseLuck"),
                        BaseEndurance = ParseNonNegativeInt(HeroesSheet, rowIndex, headers, "BaseEndurance")
                    };

                    if (!string.IsNullOrWhiteSpace(heroId))
                    {
                        if (_heroesById.TryGetValue(heroId, out var firstHero))
                            AddIssue(HeroesSheet, rowIndex + 1, "HeroId", heroId, $"Duplicate HeroId; first declared at row {firstHero.RowNumber}.");
                        else
                            _heroesById[heroId] = hero;
                    }

                    _heroes.Add(hero);
                }
            }

            private void ParseGrowthProfiles()
            {
                if (!TryGetRequiredSheet(GrowthProfilesSheet, out var sheet) ||
                    !TryReadHeader(sheet, 0, GrowthProfileColumns, out var headers))
                    return;

                for (var rowIndex = 1; rowIndex < RowCount(sheet); rowIndex++)
                {
                    if (IsBlankRow(sheet, rowIndex))
                        continue;

                    var growthProfileId = Get(sheet, rowIndex, headers, "GrowthProfileId");
                    ValidateRequired(GrowthProfilesSheet, rowIndex, "GrowthProfileId", growthProfileId);
                    ValidateRequired(GrowthProfilesSheet, rowIndex, "GenerationMode", Get(sheet, rowIndex, headers, "GenerationMode"));

                    var generationMode = Get(sheet, rowIndex, headers, "GenerationMode");
                    if (!string.IsNullOrWhiteSpace(generationMode) &&
                        !string.Equals(generationMode, PeriodicPlusMilestonesMode, StringComparison.OrdinalIgnoreCase))
                    {
                        AddIssue(GrowthProfilesSheet, rowIndex + 1, "GenerationMode", generationMode, $"Unsupported GenerationMode. Expected {PeriodicPlusMilestonesMode}.");
                    }

                    var profile = new GrowthProfile
                    {
                        RowNumber = rowIndex + 1,
                        GrowthProfileId = growthProfileId,
                        MaxLevel = ParseMinInt(GrowthProfilesSheet, rowIndex, headers, "MaxLevel", 1),
                        AddStrengthEvery = ParseOptionalPeriod(GrowthProfilesSheet, rowIndex, headers, "AddStrengthEvery"),
                        AddAgilityEvery = ParseOptionalPeriod(GrowthProfilesSheet, rowIndex, headers, "AddAgilityEvery"),
                        AddIntelligenceEvery = ParseOptionalPeriod(GrowthProfilesSheet, rowIndex, headers, "AddIntelligenceEvery"),
                        AddLuckEvery = ParseOptionalPeriod(GrowthProfilesSheet, rowIndex, headers, "AddLuckEvery"),
                        AddEnduranceEvery = ParseOptionalPeriod(GrowthProfilesSheet, rowIndex, headers, "AddEnduranceEvery")
                    };

                    if (!string.IsNullOrWhiteSpace(growthProfileId))
                    {
                        if (_growthProfiles.TryGetValue(growthProfileId, out var firstProfile))
                            AddIssue(GrowthProfilesSheet, rowIndex + 1, "GrowthProfileId", growthProfileId, $"Duplicate GrowthProfileId; first declared at row {firstProfile.RowNumber}.");
                        else
                            _growthProfiles[growthProfileId] = profile;
                    }
                }
            }

            private void ParseGrowthMilestones()
            {
                if (!TryGetRequiredSheet(GrowthMilestonesSheet, out var sheet) ||
                    !TryReadHeader(sheet, 0, GrowthMilestoneColumns, out var headers))
                    return;

                var seenByProfileAndLevel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var rowIndex = 1; rowIndex < RowCount(sheet); rowIndex++)
                {
                    if (IsBlankRow(sheet, rowIndex))
                        continue;

                    var growthProfileId = Get(sheet, rowIndex, headers, "GrowthProfileId");
                    var applyMode = Get(sheet, rowIndex, headers, "ApplyMode");
                    ValidateRequired(GrowthMilestonesSheet, rowIndex, "GrowthProfileId", growthProfileId);
                    ValidateRequired(GrowthMilestonesSheet, rowIndex, "ApplyMode", applyMode);

                    if (!string.IsNullOrWhiteSpace(applyMode) &&
                        !string.Equals(applyMode, AddToProfileMode, StringComparison.OrdinalIgnoreCase))
                    {
                        AddIssue(GrowthMilestonesSheet, rowIndex + 1, "ApplyMode", applyMode, $"Unsupported ApplyMode. Expected {AddToProfileMode}.");
                    }

                    var level = ParseMinInt(GrowthMilestonesSheet, rowIndex, headers, "Level", 2);
                    var key = $"{growthProfileId}:{level}";
                    if (!string.IsNullOrWhiteSpace(growthProfileId) && seenByProfileAndLevel.TryGetValue(key, out var firstRow))
                        AddIssue(GrowthMilestonesSheet, rowIndex + 1, "Level", level.ToString(CultureInfo.InvariantCulture), $"Duplicate GrowthProfileId + Level; first declared at row {firstRow}.");
                    else if (!string.IsNullOrWhiteSpace(growthProfileId) && level >= 2)
                        seenByProfileAndLevel[key] = rowIndex + 1;

                    var milestone = new GrowthMilestone
                    {
                        RowNumber = rowIndex + 1,
                        GrowthProfileId = growthProfileId,
                        Level = level,
                        RequiredSkillPointsOverride = ParseOptionalNonNegativeInt(GrowthMilestonesSheet, rowIndex, headers, "RequiredSkillPointsOverride"),
                        AddStrength = ParseNonNegativeInt(GrowthMilestonesSheet, rowIndex, headers, "AddStrength"),
                        AddAgility = ParseNonNegativeInt(GrowthMilestonesSheet, rowIndex, headers, "AddAgility"),
                        AddIntelligence = ParseNonNegativeInt(GrowthMilestonesSheet, rowIndex, headers, "AddIntelligence"),
                        AddLuck = ParseNonNegativeInt(GrowthMilestonesSheet, rowIndex, headers, "AddLuck"),
                        AddEndurance = ParseNonNegativeInt(GrowthMilestonesSheet, rowIndex, headers, "AddEndurance")
                    };

                    if (!_milestonesByProfile.TryGetValue(growthProfileId, out var milestones))
                    {
                        milestones = new List<GrowthMilestone>();
                        _milestonesByProfile[growthProfileId] = milestones;
                    }

                    milestones.Add(milestone);
                }
            }

            private void ParseUniqueSkills()
            {
                if (!TryGetRequiredSheet(UniqueSkillsSheet, out var sheet) ||
                    !TryReadHeader(sheet, 0, UniqueSkillColumns, out var headers))
                    return;

                var seenByHeroAndSkill = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var rowIndex = 1; rowIndex < RowCount(sheet); rowIndex++)
                {
                    if (IsBlankRow(sheet, rowIndex))
                        continue;

                    var heroId = Get(sheet, rowIndex, headers, "HeroId");
                    var skillId = Get(sheet, rowIndex, headers, "SkillId");
                    ValidateRequired(UniqueSkillsSheet, rowIndex, "HeroId", heroId);
                    ValidateRequired(UniqueSkillsSheet, rowIndex, "SkillId", skillId);
                    ValidateRequired(UniqueSkillsSheet, rowIndex, "Type", Get(sheet, rowIndex, headers, "Type"));
                    ValidateRequired(UniqueSkillsSheet, rowIndex, "NameId", Get(sheet, rowIndex, headers, "NameId"));
                    ValidateRequired(UniqueSkillsSheet, rowIndex, "DescriptionId", Get(sheet, rowIndex, headers, "DescriptionId"));
                    ValidateRequired(UniqueSkillsSheet, rowIndex, "Enabled", Get(sheet, rowIndex, headers, "Enabled"));

                    var enabled = ParseBool(UniqueSkillsSheet, rowIndex, headers, "Enabled");
                    if (enabled)
                        ValidateRequired(UniqueSkillsSheet, rowIndex, "IconId", Get(sheet, rowIndex, headers, "IconId"));

                    var key = $"{heroId}:{skillId}";
                    if (!string.IsNullOrWhiteSpace(heroId) && !string.IsNullOrWhiteSpace(skillId))
                    {
                        if (seenByHeroAndSkill.TryGetValue(key, out var firstRow))
                            AddIssue(UniqueSkillsSheet, rowIndex + 1, "SkillId", skillId, $"Duplicate HeroId + SkillId; first declared at row {firstRow}.");
                        else
                            seenByHeroAndSkill[key] = rowIndex + 1;
                    }

                    if (!_uniqueSkillIdsByHero.TryGetValue(heroId, out var ids))
                    {
                        ids = new List<string>();
                        _uniqueSkillIdsByHero[heroId] = ids;
                    }

                    if (!string.IsNullOrWhiteSpace(skillId))
                    {
                        _uniqueSkillIds.Add(skillId);
                        if (!ids.Contains(skillId))
                            ids.Add(skillId);
                    }

                    _uniqueSkills.Add(new HeroUniqueSkill
                    {
                        RowNumber = rowIndex + 1,
                        HeroId = heroId,
                        SkillId = skillId,
                        Type = Get(sheet, rowIndex, headers, "Type"),
                        NameId = Get(sheet, rowIndex, headers, "NameId"),
                        DescriptionId = Get(sheet, rowIndex, headers, "DescriptionId"),
                        IconId = Get(sheet, rowIndex, headers, "IconId"),
                        Enabled = enabled
                    });
                }
            }

            private void ParseSkillEffects()
            {
                if (!TryGetRequiredSheet(SkillEffectsSheet, out var sheet) ||
                    !TryReadHeader(sheet, 0, SkillEffectColumns, out var headers))
                    return;

                var seenBySkillAndEffect = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var rowIndex = 1; rowIndex < RowCount(sheet); rowIndex++)
                {
                    if (IsBlankRow(sheet, rowIndex))
                        continue;

                    var skillId = Get(sheet, rowIndex, headers, "SkillId");
                    var effectId = Get(sheet, rowIndex, headers, "EffectId");
                    ValidateRequired(SkillEffectsSheet, rowIndex, "SkillId", skillId);
                    ValidateRequired(SkillEffectsSheet, rowIndex, "EffectId", effectId);
                    ValidateRequired(SkillEffectsSheet, rowIndex, "Trigger", Get(sheet, rowIndex, headers, "Trigger"));
                    ValidateRequired(SkillEffectsSheet, rowIndex, "Effect", Get(sheet, rowIndex, headers, "Effect"));
                    ValidateRequired(SkillEffectsSheet, rowIndex, "Target", Get(sheet, rowIndex, headers, "Target"));
                    ValidateRequired(SkillEffectsSheet, rowIndex, "StackMode", Get(sheet, rowIndex, headers, "StackMode"));

                    var key = $"{skillId}:{effectId}";
                    if (!string.IsNullOrWhiteSpace(skillId) && !string.IsNullOrWhiteSpace(effectId))
                    {
                        if (seenBySkillAndEffect.TryGetValue(key, out var firstRow))
                            AddIssue(SkillEffectsSheet, rowIndex + 1, "EffectId", effectId, $"Duplicate SkillId + EffectId; first declared at row {firstRow}.");
                        else
                            seenBySkillAndEffect[key] = rowIndex + 1;
                    }

                    _skillEffects.Add(new HeroSkillEffect
                    {
                        RowNumber = rowIndex + 1,
                        SkillId = skillId,
                        EffectId = effectId,
                        Trigger = Get(sheet, rowIndex, headers, "Trigger"),
                        Condition = Get(sheet, rowIndex, headers, "Condition"),
                        ChancePercent = ParseOptionalPercent(SkillEffectsSheet, rowIndex, headers, "ChancePercent"),
                        Interval = ParseOptionalNonNegativeInt(SkillEffectsSheet, rowIndex, headers, "Interval") ?? 0,
                        Effect = Get(sheet, rowIndex, headers, "Effect"),
                        Target = Get(sheet, rowIndex, headers, "Target"),
                        Value = Get(sheet, rowIndex, headers, "Value"),
                        StackMode = Get(sheet, rowIndex, headers, "StackMode"),
                        CooldownSeconds = ParseOptionalNonNegativeInt(SkillEffectsSheet, rowIndex, headers, "CooldownSeconds") ?? 0
                    });
                }
            }

            private void ValidateHeroReferences()
            {
                foreach (var hero in _heroes)
                {
                    if (!string.IsNullOrWhiteSpace(hero.GrowthProfileId) && !_growthProfiles.ContainsKey(hero.GrowthProfileId))
                        AddIssue(HeroesSheet, hero.RowNumber, "GrowthProfileId", hero.GrowthProfileId, "GrowthProfileId does not exist in HeroGrowthProfiles.");
                }

                foreach (var pair in _milestonesByProfile)
                {
                    if (!_growthProfiles.TryGetValue(pair.Key, out var profile))
                    {
                        foreach (var milestone in pair.Value)
                            AddIssue(GrowthMilestonesSheet, milestone.RowNumber, "GrowthProfileId", milestone.GrowthProfileId, "GrowthProfileId does not exist in HeroGrowthProfiles.");

                        continue;
                    }

                    foreach (var milestone in pair.Value)
                    {
                        if (milestone.Level > profile.MaxLevel)
                            AddIssue(GrowthMilestonesSheet, milestone.RowNumber, "Level", milestone.Level.ToString(CultureInfo.InvariantCulture), "Milestone level exceeds profile MaxLevel.");
                    }
                }

                foreach (var skill in _uniqueSkills)
                {
                    if (!string.IsNullOrWhiteSpace(skill.HeroId) && !_heroesById.ContainsKey(skill.HeroId))
                        AddIssue(UniqueSkillsSheet, skill.RowNumber, "HeroId", skill.HeroId, "HeroId does not exist in Heroes.");
                }

                foreach (var effect in _skillEffects)
                {
                    if (!string.IsNullOrWhiteSpace(effect.SkillId) && !_uniqueSkillIds.Contains(effect.SkillId))
                        AddIssue(SkillEffectsSheet, effect.RowNumber, "SkillId", effect.SkillId, "SkillId does not exist in HeroUniqueSkills.");
                }
            }

            private List<Dictionary<string, object>> BuildHeroes()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var hero in _heroes)
                {
                    var uniqueSkillIds = _uniqueSkillIdsByHero.TryGetValue(hero.HeroId, out var skillIds)
                        ? skillIds
                        : new List<string>();

                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["heroId"] = hero.HeroId,
                        ["sortOrder"] = hero.SortOrder,
                        ["rarityId"] = hero.RarityId,
                        ["typeId"] = hero.TypeId,
                        ["enabled"] = hero.Enabled,
                        ["professionIds"] = hero.ProfessionIds,
                        ["uniqueSkillIds"] = uniqueSkillIds,
                        ["fullSpriteId"] = hero.FullSpriteId,
                        ["iconSpriteId"] = hero.IconSpriteId,
                        ["battleSpriteId"] = hero.BattleSpriteId,
                        ["nameId"] = string.IsNullOrWhiteSpace(hero.NameId) ? $"hero.{hero.HeroId}.name" : hero.NameId,
                        ["descriptionId"] = string.IsNullOrWhiteSpace(hero.DescriptionId) ? $"hero.{hero.HeroId}.description" : hero.DescriptionId,
                        ["baseStats"] = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["strength"] = hero.BaseStrength,
                            ["agility"] = hero.BaseAgility,
                            ["intelligence"] = hero.BaseIntelligence,
                            ["luck"] = hero.BaseLuck,
                            ["endurance"] = hero.BaseEndurance
                        }
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildHeroGrowth()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var hero in _heroes)
                {
                    if (!hero.Enabled || !_growthProfiles.TryGetValue(hero.GrowthProfileId, out var profile))
                        continue;

                    var milestones = _milestonesByProfile.TryGetValue(profile.GrowthProfileId, out var foundMilestones)
                        ? foundMilestones
                        : new List<GrowthMilestone>();

                    var milestonesByLevel = new Dictionary<int, GrowthMilestone>();
                    foreach (var milestone in milestones)
                        milestonesByLevel[milestone.Level] = milestone;

                    for (var level = 2; level <= profile.MaxLevel; level++)
                    {
                        var row = GenerateGrowthRow(hero.HeroId, profile, level);
                        if (milestonesByLevel.TryGetValue(level, out var milestone))
                            ApplyMilestone(row, milestone);

                        rows.Add(row.ToRuntimeRow());
                    }
                }

                return rows;
            }

            private static HeroGrowth GenerateGrowthRow(string heroId, GrowthProfile profile, int level)
            {
                return new HeroGrowth
                {
                    HeroId = heroId,
                    Level = level,
                    RequiredSkillPoints = CalculateRequiredSkillPoints(level),
                    AddStrength = PeriodicAdd(level, profile.AddStrengthEvery),
                    AddAgility = PeriodicAdd(level, profile.AddAgilityEvery),
                    AddIntelligence = PeriodicAdd(level, profile.AddIntelligenceEvery),
                    AddLuck = PeriodicAdd(level, profile.AddLuckEvery),
                    AddEndurance = PeriodicAdd(level, profile.AddEnduranceEvery)
                };
            }

            private static void ApplyMilestone(HeroGrowth growth, GrowthMilestone milestone)
            {
                if (milestone.RequiredSkillPointsOverride.HasValue)
                    growth.RequiredSkillPoints = milestone.RequiredSkillPointsOverride.Value;

                growth.AddStrength += milestone.AddStrength;
                growth.AddAgility += milestone.AddAgility;
                growth.AddIntelligence += milestone.AddIntelligence;
                growth.AddLuck += milestone.AddLuck;
                growth.AddEndurance += milestone.AddEndurance;
            }

            private static int CalculateRequiredSkillPoints(int level)
            {
                return (level - 1) * 5;
            }

            private static int PeriodicAdd(int level, int period)
            {
                return period > 0 && level % period == 0 ? 1 : 0;
            }

            private List<Dictionary<string, object>> BuildHeroUniqueSkills()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var skill in _uniqueSkills)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["heroId"] = skill.HeroId,
                        ["skillId"] = skill.SkillId,
                        ["type"] = skill.Type,
                        ["nameId"] = skill.NameId,
                        ["descriptionId"] = skill.DescriptionId,
                        ["iconId"] = skill.IconId,
                        ["enabled"] = skill.Enabled
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildHeroSkillEffects()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var effect in _skillEffects)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["skillId"] = effect.SkillId,
                        ["effectId"] = effect.EffectId,
                        ["trigger"] = effect.Trigger,
                        ["condition"] = effect.Condition,
                        ["chancePercent"] = effect.ChancePercent,
                        ["interval"] = effect.Interval,
                        ["effect"] = effect.Effect,
                        ["target"] = effect.Target,
                        ["value"] = effect.Value,
                        ["stackMode"] = effect.StackMode,
                        ["cooldownSeconds"] = effect.CooldownSeconds
                    });
                }

                return rows;
            }

            private bool TryGetRequiredSheet(string sheetName, out ConfigDownloadedSheet sheet)
            {
                if (_sheets.TryGetValue(sheetName, out sheet))
                    return true;

                AddIssue(sheetName, 0, string.Empty, string.Empty, "Required sheet is missing.");
                return false;
            }

            private bool TryReadHeader(ConfigDownloadedSheet sheet, int rowIndex, string[] columns, out Dictionary<string, int> headers)
            {
                headers = BuildHeaderIndex(sheet, rowIndex);
                var success = true;
                foreach (var column in columns)
                {
                    if (!headers.ContainsKey(column))
                    {
                        AddIssue(sheet.sheet_name, rowIndex + 1, column, string.Empty, "Required column is missing.");
                        success = false;
                    }
                }

                return success;
            }

            private int ParsePositiveInt(string sheetName, int rowIndex, Dictionary<string, int> headers, string column)
            {
                var value = Get(sheetName, rowIndex, headers, column);
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number <= 0)
                    AddIssue(sheetName, rowIndex + 1, column, value, "Expected an integer greater than 0.");

                return number;
            }

            private int ParseMinInt(string sheetName, int rowIndex, Dictionary<string, int> headers, string column, int min)
            {
                var value = Get(sheetName, rowIndex, headers, column);
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < min)
                    AddIssue(sheetName, rowIndex + 1, column, value, $"Expected an integer greater than or equal to {min}.");

                return number;
            }

            private int ParseNonNegativeInt(string sheetName, int rowIndex, Dictionary<string, int> headers, string column)
            {
                var value = Get(sheetName, rowIndex, headers, column);
                if (!TryParseNonNegativeInt(value, out var number))
                    AddIssue(sheetName, rowIndex + 1, column, value, "Expected an integer greater than or equal to 0.");

                return number;
            }

            private int ParseOptionalPeriod(string sheetName, int rowIndex, Dictionary<string, int> headers, string column)
            {
                var value = Get(sheetName, rowIndex, headers, column);
                if (string.IsNullOrWhiteSpace(value))
                    return 0;

                if (!TryParseNonNegativeInt(value, out var number))
                    AddIssue(sheetName, rowIndex + 1, column, value, "Expected an integer greater than or equal to 0.");

                return number;
            }

            private int? ParseOptionalNonNegativeInt(string sheetName, int rowIndex, Dictionary<string, int> headers, string column)
            {
                var value = Get(sheetName, rowIndex, headers, column);
                if (string.IsNullOrWhiteSpace(value))
                    return null;

                if (!TryParseNonNegativeInt(value, out var number))
                    AddIssue(sheetName, rowIndex + 1, column, value, "Expected an integer greater than or equal to 0.");

                return number;
            }

            private double ParseOptionalPercent(string sheetName, int rowIndex, Dictionary<string, int> headers, string column)
            {
                var value = Get(sheetName, rowIndex, headers, column);
                if (string.IsNullOrWhiteSpace(value))
                    return 0d;

                if (!ConfigPipelineUtilities.TryParseNumber(value, out var number) || number < 0d || number > 100d)
                    AddIssue(sheetName, rowIndex + 1, column, value, "Expected a number between 0 and 100.");

                return number;
            }

            private bool ParseBool(string sheetName, int rowIndex, Dictionary<string, int> headers, string column)
            {
                var value = Get(sheetName, rowIndex, headers, column);
                if (string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase))
                    return false;

                AddIssue(sheetName, rowIndex + 1, column, value, "Expected TRUE or FALSE.");
                return false;
            }

            private string Get(string sheetName, int rowIndex, Dictionary<string, int> headers, string column)
            {
                return _sheets.TryGetValue(sheetName, out var sheet)
                    ? Get(sheet, rowIndex, headers, column)
                    : string.Empty;
            }

            private void ValidateRequired(string sheet, int rowIndex, string column, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    AddIssue(sheet, rowIndex + 1, column, value, $"{column} is required.");
            }

            private void AddIssue(string sheet, int row, string column, string value, string message)
            {
                _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
            }

            private static Dictionary<string, int> BuildHeaderIndex(ConfigDownloadedSheet sheet, int rowIndex)
            {
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var row = Row(sheet, rowIndex);
                if (row?.cells == null)
                    return headers;

                for (var column = 0; column < row.cells.Length; column++)
                {
                    var header = (row.cells[column] ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(header) && !headers.ContainsKey(header))
                        headers[header] = column;
                }

                return headers;
            }

            private static string Get(ConfigDownloadedSheet sheet, int rowIndex, Dictionary<string, int> headers, string column)
            {
                return headers.TryGetValue(column, out var columnIndex)
                    ? Cell(sheet, rowIndex, columnIndex)
                    : string.Empty;
            }

            private static bool IsBlankRow(ConfigDownloadedSheet sheet, int rowIndex)
            {
                var row = Row(sheet, rowIndex);
                if (row?.cells == null)
                    return true;

                foreach (var cell in row.cells)
                {
                    if (!string.IsNullOrWhiteSpace(cell))
                        return false;
                }

                return true;
            }

            private static ConfigSheetRow Row(ConfigDownloadedSheet sheet, int rowIndex)
            {
                var rows = sheet.rows ?? Array.Empty<ConfigSheetRow>();
                return rowIndex >= 0 && rowIndex < rows.Length ? rows[rowIndex] : null;
            }

            private static int RowCount(ConfigDownloadedSheet sheet)
            {
                return sheet.rows?.Length ?? 0;
            }

            private static string Cell(ConfigDownloadedSheet sheet, int rowIndex, int columnIndex)
            {
                var row = Row(sheet, rowIndex);
                if (row?.cells == null || columnIndex < 0 || columnIndex >= row.cells.Length)
                    return string.Empty;

                return (row.cells[columnIndex] ?? string.Empty).Trim();
            }

            private static bool TryParseNonNegativeInt(string value, out int result)
            {
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result >= 0;
            }

            private static List<string> SplitPipe(string raw)
            {
                var values = new List<string>();
                if (string.IsNullOrWhiteSpace(raw))
                    return values;

                foreach (var part in raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var value = part.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value);
                }

                return values;
            }
        }

        private sealed class HeroConfig
        {
            public int RowNumber;
            public int SortOrder;
            public string HeroId;
            public bool Enabled;
            public string RarityId;
            public string TypeId;
            public string GrowthProfileId;
            public List<string> ProfessionIds = new List<string>();
            public string FullSpriteId;
            public string IconSpriteId;
            public string BattleSpriteId;
            public string NameId;
            public string DescriptionId;
            public int BaseStrength;
            public int BaseAgility;
            public int BaseIntelligence;
            public int BaseLuck;
            public int BaseEndurance;
        }

        private sealed class GrowthProfile
        {
            public int RowNumber;
            public string GrowthProfileId;
            public int MaxLevel;
            public int AddStrengthEvery;
            public int AddAgilityEvery;
            public int AddIntelligenceEvery;
            public int AddLuckEvery;
            public int AddEnduranceEvery;
        }

        private sealed class GrowthMilestone
        {
            public int RowNumber;
            public string GrowthProfileId;
            public int Level;
            public int? RequiredSkillPointsOverride;
            public int AddStrength;
            public int AddAgility;
            public int AddIntelligence;
            public int AddLuck;
            public int AddEndurance;
        }

        private sealed class HeroGrowth
        {
            public string HeroId;
            public int Level;
            public int RequiredSkillPoints;
            public int AddStrength;
            public int AddAgility;
            public int AddIntelligence;
            public int AddLuck;
            public int AddEndurance;

            public Dictionary<string, object> ToRuntimeRow()
            {
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["heroId"] = HeroId,
                    ["level"] = Level,
                    ["requiredSkillPoints"] = RequiredSkillPoints,
                    ["addStrength"] = AddStrength,
                    ["addAgility"] = AddAgility,
                    ["addIntelligence"] = AddIntelligence,
                    ["addLuck"] = AddLuck,
                    ["addEndurance"] = AddEndurance
                };
            }
        }

        private sealed class HeroUniqueSkill
        {
            public int RowNumber;
            public string HeroId;
            public string SkillId;
            public string Type;
            public string NameId;
            public string DescriptionId;
            public string IconId;
            public bool Enabled;
        }

        private sealed class HeroSkillEffect
        {
            public int RowNumber;
            public string SkillId;
            public string EffectId;
            public string Trigger;
            public string Condition;
            public double ChancePercent;
            public int Interval;
            public string Effect;
            public string Target;
            public string Value;
            public string StackMode;
            public int CooldownSeconds;
        }
    }
}
