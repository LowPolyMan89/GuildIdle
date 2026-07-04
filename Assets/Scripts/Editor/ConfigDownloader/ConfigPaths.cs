using System;
using System.IO;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public static class ConfigPaths
    {
        public static bool TryGetProjectRelativeFullPath(
            string projectRelativePath,
            out string fullPath,
            out string error,
            bool requireAssetsPath = false,
            bool requireOutsideAssets = false)
        {
            fullPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(projectRelativePath))
            {
                error = "Path is empty.";
                return false;
            }

            var normalized = NormalizeProjectPath(projectRelativePath);
            if (Path.IsPathRooted(normalized))
            {
                error = "Path must be project-relative, not absolute.";
                return false;
            }

            if (requireAssetsPath &&
                !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                error = "Path must be inside Assets/ so Unity includes it in builds.";
                return false;
            }

            if (requireOutsideAssets &&
                (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase)))
            {
                error = "Path must be outside Assets/ so raw downloads are not imported as Unity assets.";
                return false;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                error = "Could not resolve Unity project root.";
                return false;
            }

            var candidate = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            var normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                error = "Path must stay inside the Unity project folder.";
                return false;
            }

            fullPath = candidate;
            return true;
        }

        public static string NormalizeProjectPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim();
        }

        public static bool IsJsonPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }
    }
}
