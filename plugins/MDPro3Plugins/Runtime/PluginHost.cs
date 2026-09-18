using MDPro3.UI;
using MDPro3.UI.Popup;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using GameServant = MDPro3.Servant.Servant;

namespace MDPro3.Plugins
{
    /// <summary>
    /// Entry point of the plugin. Unity calls it once after the first scene was loaded, the host
    /// object then lives for the whole session.
    /// </summary>
    public static class PluginBootstrap
    {
        private static GameObject hostObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (hostObject != null)
                return;

            // Static state can survive a play mode session while the editor keeps the domain loaded.
            PluginEvents.Clear();

            PluginConfig.Load();

            PluginLog.Info(PluginInfo.Name + " " + PluginInfo.Version + " starting");
            PluginLog.Info(PluginConfig.Found
                ? "config: " + PluginConfig.LoadedPath
                : "config: " + PluginInfo.ConfigFolderName + "\\" + PluginInfo.ConfigFileName
                    + " not found, every feature uses its default (enabled)");

            PluginRegistry.Initialize();

            hostObject = new GameObject("MDPro3Plugins");
            UnityEngine.Object.DontDestroyOnLoad(hostObject);
            hostObject.AddComponent<PluginHost>();
        }
    }

    /// <summary>
    /// The single per frame object of the plugin package.
    ///
    /// It watches a handful of game singletons with cheap reference comparisons and dispatches a
    /// PluginEvents event only when something really changed. Features therefore do not have to
    /// poll the game: they subscribe, and only the features that ask for Tick() get called.
    /// Cost per frame: a few field reads, plus the Tick() of the features that need it.
    /// </summary>
    public sealed class PluginHost : MonoBehaviour
    {
        private const float CostLogInterval = 5f;

        private Popup watchedPopup;
        private GameServant watchedServant;
        private CardCollectionView watchedView;
        private bool watchedDeckEditorShown;
        private bool deckEditorStateKnown;
        private int watchedPrintedCount = int.MinValue;
        private int watchedPrintedFirst;

        private readonly Dictionary<string, double> featureCost = new Dictionary<string, double>();
        private readonly Dictionary<string, int> featureTickCount = new Dictionary<string, int>();
        private float costLogTimer;
        private int costLogFrames;

        private static bool updateErrorReported;

        private void OnDestroy()
        {
            PluginRegistry.Shutdown();
        }

        private void Update()
        {
            try
            {
                WatchEnvironment();
                TickFeatures();
                PluginSelfTestTick();
            }
            catch (Exception e)
            {
                if (updateErrorReported)
                    return;

                updateErrorReported = true;
                PluginLog.Error("host update failed, the plugin is disabled for this session: " + e);
                PluginRegistry.Shutdown();
            }
        }

        private void PluginSelfTestTick()
        {
            Diagnostics.PluginSelfTest.Tick();
        }

        #region Feature ticks

        private void TickFeatures()
        {
            var features = PluginRegistry.EnabledFeatures;
            if (features.Count == 0)
                return;

            bool measure = PluginConfig.LogFeatureTicks;

            for (int i = 0; i < features.Count; i++)
            {
                var feature = features[i];

                if (!measure)
                {
                    feature.Tick();
                    continue;
                }

                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                feature.Tick();
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                AddFeatureCost(feature.Id, elapsed);
            }

            if (measure)
                ReportFeatureCost();
        }

        private void AddFeatureCost(string featureId, long elapsedTicks)
        {
            double milliseconds = elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            featureCost.TryGetValue(featureId, out double total);
            featureCost[featureId] = total + milliseconds;

            featureTickCount.TryGetValue(featureId, out int count);
            featureTickCount[featureId] = count + 1;
        }

        /// <summary>Only used when config.json sets "logFeatureTicks": true.</summary>
        private void ReportFeatureCost()
        {
            costLogTimer += Time.unscaledDeltaTime;
            costLogFrames++;

            if (costLogTimer < CostLogInterval)
                return;

            var report = new StringBuilder();
            foreach (var pair in featureCost)
            {
                featureTickCount.TryGetValue(pair.Key, out int count);
                report.Append(pair.Key)
                    .Append(": ")
                    .Append(count)
                    .Append(" ticks, ")
                    .Append(pair.Value.ToString("0.###"))
                    .Append(" ms; ");
            }

            PluginLog.Info("feature tick cost over " + costLogFrames + " frames: "
                + (report.Length == 0 ? "none" : report.ToString()));

            featureCost.Clear();
            featureTickCount.Clear();
            costLogTimer = 0f;
            costLogFrames = 0;
        }

        #endregion

        #region Environment watching

        /// <summary>
        /// Cheap per frame state watch. Every block only runs when the observed value changed, so
        /// the features get their events without polling the game themselves.
        /// </summary>
        private void WatchEnvironment()
        {
            if (!PluginGame.IsReady)
                return;

            var popup = PluginGame.CurrentPopup;
            if (!ReferenceEquals(popup, watchedPopup))
            {
                watchedPopup = popup;
                LogEvent("PopupChanged: " + (popup == null ? "none" : popup.GetType().Name));
                PluginEvents.RaisePopupChanged(popup);
            }

            var servant = PluginGame.CurrentServant;
            if (!ReferenceEquals(servant, watchedServant))
            {
                watchedServant = servant;
                LogEvent("ServantChanged: " + (servant == null ? "none" : servant.GetType().Name));
                PluginEvents.RaiseServantChanged(servant);
            }

            bool deckEditorShown = PluginGame.DeckEditorShown;
            if (!deckEditorStateKnown || deckEditorShown != watchedDeckEditorShown)
            {
                deckEditorStateKnown = true;
                watchedDeckEditorShown = deckEditorShown;
                LogEvent("DeckEditorVisibilityChanged: " + deckEditorShown);
                PluginEvents.RaiseDeckEditorVisibilityChanged(deckEditorShown);

                if (!deckEditorShown)
                {
                    watchedView = null;
                    watchedPrintedCount = int.MinValue;
                    PluginEvents.RaiseCardCollectionChanged(null);
                }
            }

            if (!deckEditorShown)
                return;

            var view = PluginGame.CardCollectionView;
            if (!ReferenceEquals(view, watchedView))
            {
                watchedView = view;
                watchedPrintedCount = int.MinValue;
                LogEvent("CardCollectionChanged: " + (view == null ? "no view" : "new view"));
                PluginEvents.RaiseCardCollectionChanged(view);
            }

            if (view == null)
                return;

            // A printed card list is always a fresh list, only the history tab reuses its list and
            // inserts the clicked card in front. Comparing the count and the first entry catches
            // both cases with two field reads.
            var printed = view.printedCards;
            int count = printed != null ? printed.Count : -1;
            int first = count > 0 ? printed[0] : 0;
            if (count == watchedPrintedCount && first == watchedPrintedFirst)
                return;

            watchedPrintedCount = count;
            watchedPrintedFirst = first;
            LogEvent("CardCollectionChanged: printed list, " + count + " cards, first entry " + first);
            PluginEvents.RaiseCardCollectionChanged(view);
        }

        private static void LogEvent(string message)
        {
            if (PluginConfig.LogEvents)
                PluginLog.Info("event " + message);
        }

        #endregion
    }
}
