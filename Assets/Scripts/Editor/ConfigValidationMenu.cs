using GuildIdle;
using UnityEditor;
using UnityEngine;

namespace GuildIdle.Editor
{
    public static class ConfigValidationMenu
    {
        [MenuItem("Tools/Build/Validate Configs")]
        public static void ValidateConfigs()
        {
            ConfigDatabase.Reload();
            var report = ConfigDatabase.Validate();

            foreach (var warning in report.Warnings)
                Debug.LogWarning(warning);

            foreach (var error in report.Errors)
                Debug.LogError(error);

            if (report.IsValid)
                Debug.Log("Config validation completed successfully.");
            else
                Debug.LogError($"Config validation failed with {report.Errors.Count} error(s).");
        }
    }
}
