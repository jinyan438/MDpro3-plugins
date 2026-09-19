using System;
using System.Collections.Generic;

namespace MDPro3.Plugins
{
    /// <summary>
    /// Registry of all features of this plugin package.
    ///
    /// Adding a feature: create a folder below Runtime\Features, write the class, and add it with
    /// one line in CreateAll(). The registration is explicit on purpose:
    ///   * no reflection and no assembly scanning at start up,
    ///   * the class is referenced, so the IL2CPP build (Android) cannot strip it away,
    ///   * the start up order stays predictable.
    /// The on/off switch of a feature is configured in plugins\config.json (see PluginConfig).
    /// </summary>
    public static class PluginRegistry
    {
        private static readonly List<IPluginFeature> all = new List<IPluginFeature>();
        private static readonly List<IPluginFeature> enabled = new List<IPluginFeature>();

        /// <summary>All registered features, enabled or not.</summary>
        public static IReadOnlyList<IPluginFeature> Features => all;

        /// <summary>Only the features that are switched on, this is what the host ticks.</summary>
        public static IReadOnlyList<IPluginFeature> EnabledFeatures => enabled;

        /// <summary>
        /// Every feature of the plugin package, one line per feature.
        /// </summary>
        private static IEnumerable<IPluginFeature> CreateAll()
        {
            yield return new Features.ReleaseDateSort.ReleaseDateSortFeature();
            yield return new Features.PackBrowser.PackBrowserFeature();
            yield return new Features.RpsVisualFix.RpsVisualFixFeature();
            yield return new Features.StoryMode.StoryModeFeature();
        }

        /// <summary>
        /// Registers every feature and starts the ones that are switched on. Called once by the
        /// plugin bootstrap, the editor tools call it too so the setup can be inspected.
        /// </summary>
        public static void Initialize()
        {
            Shutdown();

            foreach (var feature in CreateAll())
            {
                if (feature == null)
                    continue;

                if (Get(feature.Id) != null)
                {
                    PluginLog.Error("feature id '" + feature.Id + "' is registered twice, ignoring the second one");
                    continue;
                }

                all.Add(feature);
            }

            ReportUnknownConfigEntries();

            foreach (var feature in all)
            {
                if (!PluginConfig.IsFeatureEnabled(feature.Id))
                {
                    PluginLog.Info("feature off (config): " + feature.Id + " - " + feature.DisplayName);
                    continue;
                }

                try
                {
                    feature.Enable();
                    enabled.Add(feature);
                    PluginLog.Info("feature on: " + feature.Id + " - " + feature.DisplayName);
                }
                catch (Exception e)
                {
                    PluginLog.Error("feature '" + feature.Id + "' failed to start: " + e);
                }
            }
        }

        /// <summary>Stops every running feature and drops the registry. Called on shutdown.</summary>
        public static void Shutdown()
        {
            foreach (var feature in enabled)
            {
                try
                {
                    feature.Disable();
                }
                catch (Exception e)
                {
                    PluginLog.Error("feature '" + feature.Id + "' failed to stop: " + e);
                }
            }

            enabled.Clear();
            all.Clear();
        }

        public static IPluginFeature Get(string id)
        {
            foreach (var feature in all)
                if (string.Equals(feature.Id, id, StringComparison.OrdinalIgnoreCase))
                    return feature;

            return null;
        }

        public static T Get<T>(string id) where T : class, IPluginFeature
        {
            return Get(id) as T;
        }

        /// <summary>true when the feature exists and was started.</summary>
        public static bool IsRunning(string id)
        {
            foreach (var feature in enabled)
                if (string.Equals(feature.Id, id, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        /// <summary>Warns about entries in config.json that no feature knows.</summary>
        private static void ReportUnknownConfigEntries()
        {
            foreach (var setting in PluginConfig.Settings)
            {
                if (setting == null || string.IsNullOrEmpty(setting.id))
                    continue;
                if (Get(setting.id) != null)
                    continue;

                PluginLog.Warn(PluginInfo.ConfigFileName + " has an entry for an unknown feature: '" + setting.id + "'");
            }
        }
    }
}
