using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class QuestConfigsSpreadsheetParserTests
    {
        private const string RawPath = "Temp/ConfigParserTests/quest_configs.raw.json";

        [TearDown]
        public void TearDown()
        {
            var path = FullPath(RawPath);
            if (File.Exists(path)) File.Delete(path);
        }

        [Test]
        public void HeaderOnlyDailyQuestsAndRewardsAreValidAndRuntimeShapeIsStable()
        {
            Write(CreateValidDownload());

            var report = new QuestConfigsSpreadsheetParser().BuildRuntimeJson(Source(), out var json);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(json, Does.Contain("\"stages\""));
            Assert.That(json, Does.Contain("\"storyQuests\""));
            Assert.That(json, Does.Contain("\"dailyQuests\": []"));
            Assert.That(json, Does.Contain("\"questRewards\": []"));
            Assert.That(json, Does.Contain("\"compareOperator\": \"GreaterOrEqual\""));
            Assert.That(json, Does.Contain("\"enumGroup\": \"QuestInstanceStatus\""));
            Assert.That(json, Does.Contain("\"value\": \"RewardPending\""));
            Assert.That(json, Does.Contain("\"closeOnStageComplete\": false"));
            Assert.That(json, Does.Not.Contain("notes"));
        }

        [Test]
        public void CloseOnStageCompleteMustBeBoolean()
        {
            var download = CreateValidDownload();
            download.sheets[2].rows[1].cells[7] = "sometimes";
            Write(download);

            var report = new QuestConfigsSpreadsheetParser().BuildRuntimeJson(Source(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("close_on_stage_complete"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Expected TRUE or FALSE"));
        }

        [Test]
        public void CloseOnStageCompleteRequiresEnabledStageQuestRelation()
        {
            var download = CreateValidDownload();
            download.sheets[2].rows[1].cells[7] = "TRUE";
            download.sheets[1].rows[1].cells[6] = "FALSE";
            Write(download);

            var report = new QuestConfigsSpreadsheetParser().BuildRuntimeJson(Source(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("close_on_stage_complete = TRUE requires at least one enabled StageQuests relation"));
        }

        [TestCase("story:quest_intro")]
        [TestCase("daily:cycle_1:quest_intro")]
        public void QuestCompletedTargetRejectsInstanceIds(string targetId)
        {
            var download = CreateValidDownload();
            download.sheets[4].rows = new[]
            {
                download.sheets[4].rows[0],
                Row("quest_intro", "default", "QuestCompleted", targetId, "GreaterOrEqual", "1", "10", "")
            };
            Write(download);

            var report = new QuestConfigsSpreadsheetParser().BuildRuntimeJson(Source(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("never an instance_id"));
        }

        [Test]
        public void HeaderAliasesAreNotAccepted()
        {
            var download = CreateValidDownload();
            download.sheets[5].rows[0].cells[5] = "compare_operator";
            Write(download);

            var report = new QuestConfigsSpreadsheetParser().BuildRuntimeJson(Source(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Required exact column is missing"));
        }

        [Test]
        public void RewardTypeUsesSharedRegistry()
        {
            var download = CreateValidDownload();
            download.sheets[6].rows = new[]
            {
                download.sheets[6].rows[0],
                Row("quest_intro", "reward_1", "UnknownReward", "wood", "1", "1", "100", "OnComplete", "10", "")
            };
            Write(download);

            var report = new QuestConfigsSpreadsheetParser().BuildRuntimeJson(Source(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Unknown RewardType registry value"));
        }

        [Test]
        public void RewardPendingQuestStatusIsRequired()
        {
            var download = CreateValidDownload();
            download.sheets[7].rows[9].cells[1] = "Active";
            Write(download);

            var report = new QuestConfigsSpreadsheetParser().BuildRuntimeJson(Source(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("QuestInstanceStatus must declare 'RewardPending'."));
        }

        [Test]
        public void ClosedQuestStatusIsRequired()
        {
            var download = CreateValidDownload();
            download.sheets[7].rows[11].cells[1] = "Completed";
            Write(download);

            var report = new QuestConfigsSpreadsheetParser().BuildRuntimeJson(Source(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("QuestInstanceStatus must declare 'Closed'."));
        }

        [Test]
        public void CrossValidatorRejectsQuestCompletedInstanceId()
        {
            var download = CreateValidDownload();
            download.sheets[4].rows = new[]
            {
                download.sheets[4].rows[0],
                Row("quest_intro", "default", "QuestCompleted", "story:quest_intro", "GreaterOrEqual", "1", "10", "")
            };
            Write(download);
            var collection = new ConfigSourceSettingsCollection { sources = new[] { Source() } };

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("QuestCompleted target_id must reference QuestDefinition.quest_id"));
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                sheets = new[]
                {
                    Sheet("Stages",
                        Row("stage_id", "name_id", "description_id", "stage_prefab_id", "target_duration_sec", "completion_rule", "next_stage_id", "sort_order", "enabled", "notes"),
                        Row("stage_arrival", "stage.name", "stage.desc", "stage_prefab", "10", "AllRequired", "stage_2", "10", "TRUE", ""),
                        Row("stage_2", "stage2.name", "stage2.desc", "stage2_prefab", "0", "AllRequired", "", "20", "TRUE", "")),
                    Sheet("StageQuests",
                        Row("stage_id", "quest_id", "weight_percent", "required", "show_in_stage_ui", "sort_order", "enabled", "notes"),
                        Row("stage_arrival", "quest_intro", "100", "TRUE", "TRUE", "10", "TRUE", "")),
                    Sheet("StoryQuests",
                        Row("quest_id", "name_id", "description_id", "icon_id", "journal_category", "sort_order", "is_tutorial", "close_on_stage_complete", "enabled", "notes"),
                        Row("quest_intro", "quest.name", "quest.desc", "quest_icon", "Story", "10", "TRUE", "FALSE", "TRUE", "")),
                    Sheet("DailyQuests", Row("quest_id", "name_id", "description_id", "icon_id", "journal_category", "daily_pool_id", "selection_weight", "sort_order", "enabled", "notes")),
                    Sheet("QuestStartConditions",
                        Row("quest_id", "condition_group", "condition_type", "target_id", "operator", "value", "sort_order", "notes"),
                        Row("quest_intro", "default", "NewGame", "", "GreaterOrEqual", "1", "10", "")),
                    Sheet("QuestSteps",
                        Row("quest_id", "step_id", "step_order", "objective_type", "target_id", "operator", "target_value", "description_id", "required", "notes"),
                        Row("quest_intro", "collect", "10", "ResourceCount", "wood", "GreaterOrEqual", "1", "step.desc", "TRUE", "")),
                    Sheet("QuestRewards", Row("quest_id", "reward_id", "reward_type", "target_id", "min", "max", "chance", "grant_moment", "sort_order", "notes")),
                    Sheet("EnumValues",
                        Row("enum_group", "value", "description"),
                        Row("CompletionRule", "AllRequired", ""),
                        Row("QuestJournalCategory", "Story", ""),
                        Row("ConditionType", "NewGame", ""),
                        Row("ConditionType", "QuestCompleted", ""),
                        Row("ObjectiveType", "ResourceCount", ""),
                        Row("CompareOperator", "GreaterOrEqual", ""),
                        Row("GrantMoment", "OnComplete", ""),
                        Row("QuestInstanceStatus", "Active", ""),
                        Row("QuestInstanceStatus", "RewardPending", ""),
                        Row("QuestInstanceStatus", "Completed", ""),
                        Row("QuestInstanceStatus", "Closed", ""),
                        Row("QuestInstanceStatus", "Expired", ""))
                }
            };
        }

        private static ConfigSourceSettings Source() => new ConfigSourceSettings
        {
            config_id = "quest_configs",
            output_json_path = RawPath,
            runtime_json_path = "Assets/StreamingAssets/Configs/quest_configs.test.runtime.json"
        };

        private static ConfigDownloadedSheet Sheet(string name, params ConfigSheetRow[] rows) => new ConfigDownloadedSheet { sheet_name = name, rows = rows };
        private static ConfigSheetRow Row(params string[] cells) => new ConfigSheetRow { cells = cells };
        private static void Write(ConfigSheetDownload download)
        {
            var path = FullPath(RawPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(download, true), ConfigPipelineUtilities.Utf8NoBom);
        }
        private static string FullPath(string projectPath)
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName;
            return Path.GetFullPath(Path.Combine(root, projectPath.Replace('\\', '/')));
        }
    }
}
