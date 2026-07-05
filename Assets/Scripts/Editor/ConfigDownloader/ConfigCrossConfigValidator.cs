using System;
using System.Collections.Generic;

namespace GuildIdle.Editor.ConfigDownloader
{
    public static class ConfigCrossConfigValidator
    {
        private const string ActivityConfigId = "activity_configs";
        private const string EnemiesConfigId = "enemies_configs";

        public static ConfigPipelineReport Validate(ConfigSourceSettingsCollection collection)
        {
            var report = new ConfigPipelineReport();
            if (!TryFindSource(collection, ActivityConfigId, out var activitySource) ||
                !TryFindSource(collection, EnemiesConfigId, out var enemiesSource))
            {
                return report;
            }

            if (!TryLoadAvailable(activitySource, out var activityDownload) ||
                !TryLoadAvailable(enemiesSource, out var enemiesDownload))
            {
                return report;
            }

            var activityTables = BuildTables(activityDownload);
            var enemiesTables = BuildTables(enemiesDownload);
            if (!activityTables.TryGetValue("CombatDetails", out var combatDetails) ||
                !combatDetails.HasColumn("enemy_group_id") ||
                !enemiesTables.TryGetValue("EnemyGroups", out var enemyGroups) ||
                !enemyGroups.HasColumn("enemy_group_id"))
            {
                return report;
            }

            var enemyGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in enemyGroups.DataRows)
            {
                var enemyGroupId = row.Get("enemy_group_id");
                if (!string.IsNullOrWhiteSpace(enemyGroupId))
                    enemyGroupIds.Add(enemyGroupId);
            }

            foreach (var row in combatDetails.DataRows)
            {
                var enemyGroupId = row.Get("enemy_group_id");
                if (string.IsNullOrWhiteSpace(enemyGroupId))
                    continue;

                if (!enemyGroupIds.Contains(enemyGroupId))
                {
                    report.Issues.Add(new ConfigValidationIssue(
                        "Activity Configs / CombatDetails",
                        row.RowNumber,
                        "enemy_group_id",
                        enemyGroupId,
                        "Referenced enemy_group_id does not exist in Enemies Configs / EnemyGroups.enemy_group_id."));
                }
            }

            return report;
        }

        public static void ApplyToSources(ConfigSourceSettingsCollection collection)
        {
            var report = Validate(collection);
            if (report.Success)
                return;

            var message = report.ToDisplayMessage();
            ApplyValidationError(collection, ActivityConfigId, message);
            ApplyValidationError(collection, EnemiesConfigId, message);
        }

        private static bool TryFindSource(ConfigSourceSettingsCollection collection, string configId, out ConfigSourceSettings source)
        {
            source = null;
            if (collection?.sources == null)
                return false;

            foreach (var candidate in collection.sources)
            {
                if (candidate != null && string.Equals(candidate.config_id, configId, StringComparison.OrdinalIgnoreCase))
                {
                    source = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryLoadAvailable(ConfigSourceSettings source, out ConfigSheetDownload download)
        {
            var report = new ConfigPipelineReport();
            return ConfigPipelineUtilities.TryLoadDownload(source, report, out download);
        }

        private static Dictionary<string, ConfigSheetTable> BuildTables(ConfigSheetDownload download)
        {
            var tables = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in download.sheets)
            {
                if (sheet == null || string.IsNullOrWhiteSpace(sheet.sheet_name))
                    continue;

                tables[sheet.sheet_name] = new ConfigSheetTable(sheet);
            }

            return tables;
        }

        private static void ApplyValidationError(ConfigSourceSettingsCollection collection, string configId, string message)
        {
            if (!TryFindSource(collection, configId, out var source))
                return;

            source.last_validation_status = ConfigPipelineStatus.ValidationError;
            if (string.IsNullOrWhiteSpace(source.error_message))
            {
                source.error_message = message;
                return;
            }

            if (source.error_message.Contains(message))
                return;

            source.error_message = $"{source.error_message}\n{message}";
        }
    }
}
