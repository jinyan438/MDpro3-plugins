using System;
using System.IO;
using UnityEngine;

namespace MDPro3.Plugins
{
    /// <summary>One entry of the "features" array in plugins\config.json.</summary>
    [Serializable]
    public sealed class PluginFeatureSetting
    {
        /// <summary>Feature id, e.g. "releaseDateSort".</summary>
        public string id;

        /// <summary>false switches the feature off without removing it from the game.</summary>
        public bool enabled = true;

        /// <summary>Only for the reader of the file, the plugin ignores it.</summary>
        public string note;
    }

    [Serializable]
    internal sealed class PluginConfigFile
    {
        public PluginFeatureSetting[] features;

        /// <summary>Logs how long each feature tick takes (every five seconds).</summary>
        public bool logFeatureTicks;

        /// <summary>Logs the dispatched events.</summary>
        public bool logEvents;
    }

    /// <summary>
    /// Reads plugins\config.json. Changing that file needs no rebuild: it is read when the
    /// game starts and features are switched on or off accordingly.
    ///
    /// The file is looked up by walking up from the Unity data folder, the working directory
    /// and the player folder, so it is found both in the editor and in Build\MDPro3.
    /// A missing file is not an error, every feature then uses its default (enabled).
    /// </summary>
    public static class PluginConfig
    {
        private const int MaxLevels = 6;
        private static readonly string RelativePath = Path.Combine(PluginInfo.ConfigFolderName, PluginInfo.ConfigFileName);

        private static PluginConfigFile data;
        private static string loadedPath;

        /// <summary>Path of the loaded file, or null when the defaults are used.</summary>
        public static string LoadedPath => loadedPath;

        public static bool Found => !string.IsNullOrEmpty(loadedPath);

        public static PluginFeatureSetting[] Settings =>
            data != null && data.features != null ? data.features : Array.Empty<PluginFeatureSetting>();

        public static bool LogFeatureTicks => data != null && data.logFeatureTicks;

        public static bool LogEvents => data != null && data.logEvents;

        public static void Load()
        {
            data = null;
            loadedPath = null;

            string candidate = ResolvePath();
            if (string.IsNullOrEmpty(candidate))
                return;

            try
            {
                data = JsonUtility.FromJson<PluginConfigFile>(File.ReadAllText(candidate));
                loadedPath = candidate;
            }
            catch (Exception e)
            {
                PluginLog.Warn(PluginInfo.ConfigFileName + " could not be read (" + candidate + "): " + e.Message);
            }
        }

        /// <summary>
        /// true when the feature is switched on. A feature without an entry in the file is
        /// enabled, so a new feature works right away.
        /// </summary>
        public static bool IsFeatureEnabled(string featureId, bool defaultEnabled = true)
        {
            foreach (var setting in Settings)
            {
                if (setting == null || string.IsNullOrEmpty(setting.id))
                    continue;
                if (!string.Equals(setting.id, featureId, StringComparison.OrdinalIgnoreCase))
                    continue;
                return setting.enabled;
            }

            return defaultEnabled;
        }

        /// <summary>Short state of a feature for logs.</summary>
        public static string DescribeFeature(string featureId, bool defaultEnabled = true)
        {
            foreach (var setting in Settings)
            {
                if (setting == null || !string.Equals(setting.id, featureId, StringComparison.OrdinalIgnoreCase))
                    continue;
                return setting.enabled ? "on" : "off (config)";
            }

            return defaultEnabled ? "on (default)" : "off (default)";
        }

        private static string ResolvePath()
        {
            string fromData = WalkUp(Application.dataPath);
            if (fromData != null)
                return fromData;

            string fromWorkingDirectory = WalkUp(Directory.GetCurrentDirectory());
            if (fromWorkingDirectory != null)
                return fromWorkingDirectory;

            try
            {
                string playerFolder = Path.GetDirectoryName(Application.dataPath);
                string fromPlayer = WalkUp(playerFolder);
                if (fromPlayer != null)
                    return fromPlayer;
            }
            catch (Exception)
            {
                // Application.dataPath can be empty in unusual contexts, then there is nothing to look up.
            }

            return null;
        }

        private static string WalkUp(string start)
        {
            if (string.IsNullOrEmpty(start))
                return null;

            DirectoryInfo directory;
            try
            {
                directory = new DirectoryInfo(start);
            }
            catch (Exception)
            {
                return null;
            }

            for (int level = 0; level <= MaxLevels && directory != null; level++)
            {
                string candidate = Path.Combine(directory.FullName, RelativePath);
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            return null;
        }
    }
}
