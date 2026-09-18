using UnityEngine;

namespace MDPro3.Plugins
{
    /// <summary>
    /// All plugin messages use one prefix, so they can be found in the game log
    /// (plugins\.state\player-selftest.log) and in UnityEditor logs.
    /// </summary>
    public static class PluginLog
    {
        public const string Prefix = "[MDPro3Plugins]";

        public static void Info(string message)
        {
            Debug.Log(Prefix + " " + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + " " + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + " " + message);
        }

        public static void Feature(string featureId, string message)
        {
            Debug.Log(Prefix + " [" + featureId + "] " + message);
        }
    }
}
