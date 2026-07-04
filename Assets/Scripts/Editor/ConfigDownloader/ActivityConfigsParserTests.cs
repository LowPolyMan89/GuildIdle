using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class ActivityConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/activity_configs.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/activity_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_UsesHeadersAndExcludesDesignerAndDisabledRows()
        {
            WriteRaw(CreateValidDownload());

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"activities\""));
            Assert.That(runtimeJson, Does.Contain("\"workDetails\""));
            Assert.That(runtimeJson, Does.Contain("\"id\": \"work_active\""));
            Assert.That(runtimeJson, Does.Not.Contain("work_disabled"));
            Assert.That(runtimeJson, Does.Not.Contain("README"));
            Assert.That(runtimeJson, Does.Not.Contain("Название"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("enabled"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
            Assert.That(runtimeJson, Does.Contain("\"tier\": 1"));
            Assert.That(runtimeJson, Does.Contain("\"isRepeatable\": true"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsSheetRowColumnValueForInvalidData()
        {
            var download = CreateValidDownload();
            var activities = FindSheet(download, "Activities");
            activities.rows[1].cells[6] = "BadCategory";
            activities.rows[1].cells[8] = "NaN";
            activities.rows[1].cells[17] = "MAYBE";
            activities.rows = Append(activities.rows, Row("Duplicate active", "work_active", "dup.name", "dup.desc", "icon_dup", "Work", "Gathering", "Common", "1", "village", "Cycle", "0", "30", "1", "skill_gathering", "TRUE", "TRUE", "TRUE", "duplicate", ""));

            WriteRaw(download);

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Activities row 2 column 'category' value 'BadCategory'"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Activities row 2 column 'tier' value 'NaN'"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Activities row 2 column 'enabled' value 'MAYBE'"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Duplicate activity id"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsMissingActivityReference()
        {
            var download = CreateValidDownload();
            var requirements = FindSheet(download, "ActivityRequirements");
            requirements.rows[1].cells[1] = "missing_activity";
            WriteRaw(download);

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("ActivityRequirements row 2 column 'activity_id' value 'missing_activity'"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsMissingRequirementRewardAndTriggerActivityReferences()
        {
            var download = CreateValidDownload();
            FindSheet(download, "ActivityRequirements").rows[1].cells[1] = "missing_requirement_activity";
            FindSheet(download, "ActivityRewards").rows[1].cells[1] = "missing_reward_activity";
            FindSheet(download, "ActivityTriggers").rows = Append(
                FindSheet(download, "ActivityTriggers").rows,
                Row("Trigger", "missing_trigger_activity", "OnComplete", "UnlockLocation", "village", "1", "100", "TRUE", "note"));
            WriteRaw(download);

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("ActivityRequirements row 2 column 'activity_id' value 'missing_requirement_activity'"));
            Assert.That(message, Does.Contain("ActivityRewards row 2 column 'activity_id' value 'missing_reward_activity'"));
            Assert.That(message, Does.Contain("ActivityTriggers row 2 column 'activity_id' value 'missing_trigger_activity'"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsBuildAndCraftActivities()
        {
            var download = CreateValidDownload();
            var activities = FindSheet(download, "Activities");
            activities.rows = Append(activities.rows,
                Row("Build row", "build_hall", "build.name", "build.desc", "icon_build", "Build", "Gathering", "Common", "1", "village", "ProgressBar", "60", "0", "0", "", "FALSE", "FALSE", "TRUE", "note", ""),
                Row("Craft row", "craft_plank", "craft.name", "craft.desc", "icon_craft", "Craft", "Gathering", "Common", "1", "village", "ProgressBar", "30", "0", "0", "", "FALSE", "FALSE", "TRUE", "note", ""));

            var enumValues = FindSheet(download, "EnumValues");
            enumValues.rows = Append(enumValues.rows,
                Row("Build", "ActivityType", "Build", "Legacy build type"),
                Row("Craft", "ActivityType", "Craft", "Legacy craft type"));
            WriteRaw(download);

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("Activities row 4: type 'Build' is not allowed in Activity Configs. Use Buildings Configs or Items Configs instead."));
            Assert.That(message, Does.Contain("Activities row 5: type 'Craft' is not allowed in Activity Configs. Use Buildings Configs or Items Configs instead."));
        }

        [Test]
        public void BuildRuntimeJson_RejectsDetailsThatReferenceWrongActivityType()
        {
            var download = CreateValidDownload();
            FindSheet(download, "WorkDetails").rows[1].cells[1] = "event_manual";
            FindSheet(download, "Activities").rows = Append(
                FindSheet(download, "Activities").rows,
                Row("Event row", "event_manual", "event.name", "event.desc", "icon_event", "Event", "Gathering", "Common", "1", "village", "Trigger", "0", "0", "0", "", "FALSE", "FALSE", "TRUE", "note", ""));
            FindSheet(download, "EnumValues").rows = Append(
                FindSheet(download, "EnumValues").rows,
                Row("Event", "ActivityType", "Event", "Event type"),
                Row("Trigger", "ProgressMode", "Trigger", "Trigger progress"));
            WriteRaw(download);

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("WorkDetails row 2 column 'activity_id' value 'event_manual': Referenced activity type is 'Event', but WorkDetails requires 'Work'."));
        }

        [Test]
        public void BuildRuntimeJson_AllowsCombatDetailsForCombatOrder()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Activities").rows = Append(
                FindSheet(download, "Activities").rows,
                Row("Combat order", "order_warehouse_rats", "order.name", "order.desc", "icon_order", "Order", "CombatOrder", "Common", "1", "village", "ProgressBar", "60", "0", "0", "", "FALSE", "FALSE", "TRUE", "note", ""));
            FindSheet(download, "OrderDetails").rows = Append(
                FindSheet(download, "OrderDetails").rows,
                Row("Combat order details", "order_warehouse_rats", "HallBoard", "guild_reputation", "FALSE", "0", "FALSE", "note"));
            FindSheet(download, "CombatDetails").rows = Append(
                FindSheet(download, "CombatDetails").rows,
                Row("Combat order combat", "order_warehouse_rats", "enemy_group_warehouse_rats", "Queue_1v1", "VictoryExpected", "ActivityRewards", "note"));
            FindSheet(download, "EnumValues").rows = Append(
                FindSheet(download, "EnumValues").rows,
                Row("Order", "ActivityType", "Order", "Order type"),
                Row("CombatOrder", "ActivityCategory", "CombatOrder", "Combat order category"),
                Row("ProgressBar", "ProgressMode", "ProgressBar", "Progress bar mode"));
            WriteRaw(download);

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"activityId\": \"order_warehouse_rats\""));
        }

        [Test]
        public void BuildRuntimeJson_RejectsCombatDetailsForNonCombatOrder()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Activities").rows = Append(
                FindSheet(download, "Activities").rows,
                Row("Regular order", "order_delivery", "order.name", "order.desc", "icon_order", "Order", "Gathering", "Common", "1", "village", "ProgressBar", "60", "0", "0", "", "FALSE", "FALSE", "TRUE", "note", ""));
            FindSheet(download, "CombatDetails").rows = Append(
                FindSheet(download, "CombatDetails").rows,
                Row("Invalid combat", "order_delivery", "enemy_group_delivery", "Queue_1v1", "VictoryExpected", "ActivityRewards", "note"));
            FindSheet(download, "EnumValues").rows = Append(
                FindSheet(download, "EnumValues").rows,
                Row("Order", "ActivityType", "Order", "Order type"),
                Row("ProgressBar", "ProgressMode", "ProgressBar", "Progress bar mode"));
            WriteRaw(download);

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("CombatDetails row 2 column 'activity_id' value 'order_delivery': CombatDetails requires activity type CombatTask or Order with category CombatOrder."));
        }

        [Test]
        public void BuildRuntimeJson_TreatsEnumValuesValueAsString()
        {
            WriteRaw(CreateValidDownload());

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(report.ToDisplayMessage(), Does.Not.Contain("EnumValues row 2 column 'value' value 'Work': Expected a number."));
            Assert.That(runtimeJson, Does.Contain("\"enumGroup\": \"ActivityType\""));
            Assert.That(runtimeJson, Does.Contain("\"value\": \"Work\""));
        }

        [Test]
        public void BuildRuntimeJson_AlwaysWritesRequirementConsumeAndHidden()
        {
            var download = CreateValidDownload();
            FindSheet(download, "ActivityRequirements").rows = Append(
                FindSheet(download, "ActivityRequirements").rows,
                Row("Requirement empty flags", "work_active", "Resource", "resource_stone", "2", "", "", "OnStart", "note"));
            WriteRaw(download);

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"consume\": false"));
            Assert.That(runtimeJson, Does.Contain("\"hidden\": false"));
            Assert.That(CountOccurrences(runtimeJson, "\"consume\": false"), Is.EqualTo(2));
            Assert.That(CountOccurrences(runtimeJson, "\"hidden\": false"), Is.EqualTo(2));
        }

        [Test]
        public void BuildRuntimeJson_ValidatesEnumValuesRequiredFieldsAndUniquePairs()
        {
            var download = CreateValidDownload();
            var enumValues = FindSheet(download, "EnumValues");
            enumValues.rows = Append(enumValues.rows,
                Row("Missing group", "", "FreeTextValue", "description"),
                Row("Missing value", "ActivityType", "", "description"),
                Row("Duplicate work", "ActivityType", "Work", "duplicate"));
            WriteRaw(download);

            var report = new ActivityConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("EnumValues row 13 column 'enum_group'"));
            Assert.That(message, Does.Contain("EnumValues row 14 column 'value'"));
            Assert.That(message, Does.Contain("EnumValues row 15 column 'value' value 'Work': Duplicate enum value in group 'ActivityType'."));
            Assert.That(message, Does.Not.Contain("Expected a number."));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Activities").rows[1].cells[8] = "bad_number";
            WriteRaw(download);

            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new ActivityConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new ActivityConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"activities\""));
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "activity_configs",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "activity_configs",
                display_name = "GuildIdle - Activity Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-04T00:00:00Z",
                sheets = new[]
                {
                    Sheet("Activities",
                        Row("Название", "id", "name_id", "description_id", "icon_id", "type", "category", "rarity_id", "tier", "location_id", "progress_mode", "duration_sec", "cycle_sec", "fatigue_cost", "main_skill_id", "is_repeatable", "offline_enabled", "enabled", "notes", "stat_profile_id"),
                        Row("Active work", "work_active", "work.name", "work.desc", "icon_work", "Work", "Gathering", "Common", "1", "village", "Cycle", "0", "30", "1", "skill_gathering", "TRUE", "TRUE", "TRUE", "designer note", "profile_gathering"),
                        Row("Disabled work", "work_disabled", "disabled.name", "disabled.desc", "icon_disabled", "Work", "Gathering", "Common", "1", "village", "Cycle", "0", "30", "1", "skill_gathering", "TRUE", "TRUE", "FALSE", "designer note", "")),
                    Sheet("WorkDetails",
                        Row("Название", "activity_id", "success_chance", "tool_type", "auto_repeat", "fail_mode", "notes"),
                        Row("Active detail", "work_active", "100", "", "TRUE", "None", "note"),
                        Row("Disabled detail", "work_disabled", "100", "", "TRUE", "None", "note")),
                    Sheet("OrderDetails", Row("Название", "activity_id", "order_source", "reputation_id", "can_repeat", "repeat_cooldown_sec", "consume_requirements_on_start", "notes")),
                    Sheet("EventDetails", Row("Название", "activity_id", "event_kind", "discover_condition_id", "starts_combat", "encounter_id", "one_time", "hidden_until_discovered", "notes")),
                    Sheet("ExploreDetails", Row("Название", "activity_id", "unlock_location_id", "discovery_points_required", "danger_level", "notes")),
                    Sheet("CombatDetails", Row("Название", "activity_id", "enemy_group_id", "combat_mode", "intended_first_result", "completion_reward_rule", "notes")),
                    Sheet("ActivityRequirements",
                        Row("Название", "activity_id", "req_type", "target_id", "value", "consume", "hidden", "check_moment", "notes"),
                        Row("Requirement", "work_active", "Resource", "resource_pine_wood", "1", "FALSE", "FALSE", "OnStart", "note")),
                    Sheet("ActivityRewards",
                        Row("Название", "activity_id", "reward_type", "target_id", "min", "max", "chance", "grant_moment", "notes"),
                        Row("Reward", "work_active", "Resource", "resource_pine_wood", "1", "2", "100", "OnComplete", "note")),
                    Sheet("ActivityTriggers",
                        Row("Название", "activity_id", "trigger_moment", "trigger_type", "target_id", "value", "chance", "once_only", "notes")),
                    Sheet("Rarities",
                        Row("Название", "id", "name_id", "description_id", "icon_id", "color_hex", "reward_mult", "duration_mult", "fatigue_mult", "weight", "notes"),
                        Row("Common", "Common", "rarity.common.name", "rarity.common.desc", "icon_common", "#ffffff", "1", "1", "1", "100", "note")),
                    Sheet("Skills",
                        Row("Название", "skill_id", "skill_name_id", "skill_description_id", "skill_icon_id"),
                        Row("Gathering", "skill_gathering", "skill.gathering.name", "skill.gathering.desc", "icon_skill")),
                    Sheet("SkillsProgression",
                        Row("Название", "level", "exp_to_next_level", "total_exp_required", "notes"),
                        Row("Level 1", "1", "100", "0", "note")),
                    Sheet("EnumValues",
                        Row("Название", "enum_group", "value", "description"),
                        Row("Work", "ActivityType", "Work", "Work activity"),
                        Row("Gathering", "ActivityCategory", "Gathering", "Gathering category"),
                        Row("Common", "RarityId", "Common", "Common rarity"),
                        Row("Cycle", "ProgressMode", "Cycle", "Cycle progress"),
                        Row("skill_gathering", "SkillId", "skill_gathering", "Gathering skill"),
                        Row("None", "FailMode", "None", "No failure"),
                        Row("Resource", "RequirementType", "Resource", "Resource requirement"),
                        Row("Resource", "RewardType", "Resource", "Resource reward"),
                        Row("UnlockLocation", "TriggerType", "UnlockLocation", "Unlock location"),
                        Row("OnStart", "Moment", "OnStart", "On start"),
                        Row("OnComplete", "Moment", "OnComplete", "On complete")),
                    Sheet("README", Row("README"), Row("This sheet must not be emitted"))
                }
            };
        }

        private static ConfigDownloadedSheet Sheet(string name, params ConfigSheetRow[] rows)
        {
            return new ConfigDownloadedSheet
            {
                sheet_name = name,
                rows = rows
            };
        }

        private static ConfigSheetRow Row(params string[] cells)
        {
            return new ConfigSheetRow { cells = cells };
        }

        private static ConfigSheetRow[] Append(ConfigSheetRow[] rows, params ConfigSheetRow[] appendedRows)
        {
            var list = new List<ConfigSheetRow>(rows);
            list.AddRange(appendedRows);
            return list.ToArray();
        }

        private static ConfigDownloadedSheet FindSheet(ConfigSheetDownload download, string sheetName)
        {
            foreach (var sheet in download.sheets)
            {
                if (string.Equals(sheet.sheet_name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return sheet;
            }

            throw new InvalidOperationException($"Missing test sheet {sheetName}.");
        }

        private static int CountOccurrences(string value, string needle)
        {
            var count = 0;
            var index = 0;
            while (index < value.Length)
            {
                index = value.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0)
                    break;

                count++;
                index += needle.Length;
            }

            return count;
        }

        private static void WriteRaw(ConfigSheetDownload download)
        {
            WriteProjectFile(TestRawPath, JsonUtility.ToJson(download, true));
        }

        private static void WriteProjectFile(string projectPath, string text)
        {
            var fullPath = FullProjectPath(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, text, Encoding.UTF8);
        }

        private static string ReadProjectFile(string projectPath)
        {
            return File.ReadAllText(FullProjectPath(projectPath), Encoding.UTF8);
        }

        private static void DeleteProjectFile(string projectPath)
        {
            var fullPath = FullProjectPath(projectPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        private static string FullProjectPath(string projectPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, projectPath.Replace('\\', '/')));
        }
    }
}
