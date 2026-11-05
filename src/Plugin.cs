using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PeglinKeybinds
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        // BepInEx names the config file after the GUID, so this gives BepInEx/config/PeglinKeybinds.cfg.
        public const string Guid = "PeglinKeybinds";
        public const string Name = "PeglinKeybinds";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<float> ListenSeconds;
        internal static ConfigEntry<KeyboardShortcut> OpenControlsKey;

        internal static BindingStore Store;

        private void Awake()
        {
            Log = Logger;

            ListenSeconds = Config.Bind("General", "ListenSeconds", 5f,
                "How long the controls screen waits for a key or button when you pick a binding to change, before giving up.");
            OpenControlsKey = Config.Bind("General", "OpenControlsKey", new KeyboardShortcut(KeyCode.F5, KeyCode.LeftControl),
                "Opens Options > Controls from the main menu (not during a run). Set to None to only open it from the Options menu.");
            RemoveOldSettings("OpenMenuKey", "AddOptionsButton");

            // Saved next to the .cfg so it's easy to find and delete: BepInEx/config/PeglinKeybinds.bindings.
            Store = new BindingStore(System.IO.Path.Combine(Paths.ConfigPath, Guid + ".bindings"));

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            // Our own object rather than BepInEx's manager object, which some setups hide or destroy on scene loads.
            var host = new GameObject(Name) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<BindingLoader>().Init(Store);
#if DEBUG
            // Development only: BepInEx/config/PeglinKeybinds.dump turns on the UI test harness (see DevDump.cs).
            if (System.IO.File.Exists(System.IO.Path.Combine(Paths.ConfigPath, Guid + ".dump")))
            {
                host.AddComponent<DevDump>();
            }
#endif

            Log.LogInfo($"{Name} {Version} loaded. Change controls from Options > Controls, or press {OpenControlsKey.Value}.");
        }

        /// <summary>Drops settings from earlier builds (e.g. the old F8 key) so they don't linger in the .cfg looking live.</summary>
        private void RemoveOldSettings(params string[] keys)
        {
            var orphans = Traverse.Create(Config).Property("OrphanedEntries").GetValue<System.Collections.Generic.Dictionary<ConfigDefinition, string>>();
            if (orphans == null)
            {
                return;
            }
            bool removed = false;
            foreach (string key in keys)
            {
                removed |= orphans.Remove(new ConfigDefinition("General", key));
            }
            if (removed)
            {
                Config.Save();
            }
        }
    }
}
