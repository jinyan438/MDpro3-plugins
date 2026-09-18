namespace MDPro3.Plugins
{
    /// <summary>
    /// One plugin feature. Every feature lives in its own folder below Runtime\Features and is
    /// registered with one line in PluginRegistry.CreateAll().
    ///
    /// Life cycle: Enable() once at start up (subscribe to PluginEvents here), Tick() every frame
    /// while the game runs (keep it cheap, prefer events), Disable() on shutdown.
    /// </summary>
    public interface IPluginFeature
    {
        /// <summary>Config key used in plugins\config.json.</summary>
        string Id { get; }

        /// <summary>Name used in logs.</summary>
        string DisplayName { get; }

        void Enable();

        void Disable();

        void Tick();
    }

    /// <summary>
    /// Convenience base class. A feature only overrides what it needs; nothing overrides Tick()
    /// when it is purely event driven, and then it costs nothing per frame.
    /// </summary>
    public abstract class PluginFeature : IPluginFeature
    {
        public abstract string Id { get; }

        public virtual string DisplayName => Id;

        /// <summary>Called once when the feature is switched on. Subscribe to PluginEvents here.</summary>
        public virtual void Enable()
        {
        }

        /// <summary>Called on shutdown. Unsubscribe from PluginEvents here.</summary>
        public virtual void Disable()
        {
        }

        /// <summary>
        /// Called every frame while the feature is enabled. Leave it empty when the feature works
        /// with PluginEvents, or return as early as possible.
        /// </summary>
        public virtual void Tick()
        {
        }

        protected void Log(string message)
        {
            PluginLog.Feature(Id, message);
        }
    }
}
