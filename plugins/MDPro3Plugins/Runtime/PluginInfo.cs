namespace MDPro3.Plugins
{
    /// <summary>
    /// Basic identity of the MDPro3 plugin package.
    ///
    /// The plugin sources live in &lt;repo&gt;\plugins\MDPro3Plugins and are copied into
    /// &lt;project&gt;\Assets\MDPro3Plugins by Run_MDPro3.bat. Unity compiles that folder into
    /// Assembly-CSharp, the game assembly, so the plugin can use the game API directly.
    /// </summary>
    public static class PluginInfo
    {
        public const string Name = "MDPro3Plugins";
        public const string Version = "0.3.0";

        /// <summary>Folder (next to this plugin) that holds the configuration file.</summary>
        public const string ConfigFolderName = "plugins";

        /// <summary>Feature switches of the plugin package.</summary>
        public const string ConfigFileName = "config.json";
    }
}
