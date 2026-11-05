using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using PeglinKeybinds.Core;
using UnityEngine;

namespace PeglinKeybinds
{
    /// <summary>
    /// Reads and changes key settings in other mods' BepInEx configs. Changes go through the config entry, so BepInEx
    /// saves the mod's .cfg straight away and mods that read the setting each time pick up the new key at once.
    /// </summary>
    internal static class ModBindings
    {
        /// <summary>Every KeyboardShortcut and KeyCode setting of every other loaded mod, dev ones included.</summary>
        public static List<ModKeySetting> Discover()
        {
            var settings = new List<ModKeySetting>();
            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                if (info?.Instance == null || info.Metadata.GUID == Plugin.Guid)
                {
                    continue;
                }
                ConfigFile config;
                try
                {
                    config = info.Instance.Config;
                }
                catch (Exception)
                {
                    continue;
                }
                if (config == null)
                {
                    continue;
                }
                foreach (ConfigEntryBase entry in ((IDictionary<ConfigDefinition, ConfigEntryBase>)config).Values)
                {
                    if (entry.SettingType != typeof(KeyboardShortcut) && entry.SettingType != typeof(KeyCode))
                    {
                        continue;
                    }
                    settings.Add(new ModKeySetting(info.Metadata.GUID, info.Metadata.Name, entry.Definition.Section, entry.Definition.Key,
                        entry.Description?.Description, MainKey(entry.BoxedValue).ToString(), MainKey(entry.DefaultValue).ToString(), entry));
                }
            }
            return settings;
        }

        public static ConfigEntryBase Entry(ModKeySetting setting) => setting?.Handle as ConfigEntryBase;

        public static KeyCode MainKey(object value)
        {
            switch (value)
            {
                case KeyboardShortcut shortcut: return shortcut.MainKey;
                case KeyCode key: return key;
                default: return KeyCode.None;
            }
        }

        public static IEnumerable<KeyCode> Modifiers(object value)
        {
            return value is KeyboardShortcut shortcut ? shortcut.Modifiers : Enumerable.Empty<KeyCode>();
        }

        public static KeyCode Current(ModKeySetting setting) => MainKey(Entry(setting)?.BoxedValue);

        /// <summary>How the key reads in the screen: "X", "Ctrl + F5", or for a controller button "L3".</summary>
        public static string Describe(ModKeySetting setting)
        {
            ConfigEntryBase entry = Entry(setting);
            KeyCode key = MainKey(entry?.BoxedValue);
            if (key == KeyCode.None)
            {
                return null;
            }
            string label = ModKeys.JoystickButtonLabel(key.ToString());
            if (label != null)
            {
                return label;
            }
            var parts = Modifiers(entry.BoxedValue).Select(m => PromptNames.KeyLabel(m.ToString())).ToList();
            parts.Add(key.ToString());
            return string.Join(" + ", parts.Select(Pretty).ToArray());
        }

        /// <summary>The clash identity of the setting's current key, or null when unbound.</summary>
        public static string ClashId(ModKeySetting setting)
        {
            ConfigEntryBase entry = Entry(setting);
            KeyCode key = MainKey(entry?.BoxedValue);
            if (key == KeyCode.None)
            {
                return null;
            }
            string pad = ModKeys.JoystickButtonLabel(key.ToString());
            return pad != null ? ClashIndex.PadId(pad) : ClashIndex.KeyboardId(key.ToString(), Modifiers(entry.BoxedValue).Select(m => m.ToString()));
        }

        /// <summary>
        /// Sets the setting to a single key (any modifiers it had are dropped), or None to unbind. BepInEx writes the
        /// other mod's .cfg here, which can fail; that is logged rather than thrown into the UI callback.
        /// </summary>
        public static bool Set(ModKeySetting setting, KeyCode key)
        {
            ConfigEntryBase entry = Entry(setting);
            if (entry == null)
            {
                return false;
            }
            object value = entry.SettingType == typeof(KeyCode) ? (object)key : new KeyboardShortcut(key);
            if (Equals(entry.BoxedValue, value))
            {
                // The setting already has this key: don't rewrite the other mod's file for nothing.
                return true;
            }
            if (!Assign(entry, value))
            {
                return false;
            }
            Plugin.Log.LogInfo($"{setting.PluginName}: {setting.Key} set to {key}.");
            return true;
        }

        public static bool ResetToDefault(ModKeySetting setting)
        {
            ConfigEntryBase entry = Entry(setting);
            if (entry == null || Equals(entry.BoxedValue, entry.DefaultValue))
            {
                // Already the default: don't rewrite the other mod's file for nothing, which would also drop hand edits.
                return true;
            }
            return Assign(entry, entry.DefaultValue);
        }

        private static bool Assign(ConfigEntryBase entry, object value)
        {
            try
            {
                entry.BoxedValue = value;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't change the key {entry.Definition.Section}/{entry.Definition.Key} in {entry.ConfigFile?.ConfigFilePath}: {e.Message}");
                return false;
            }
        }

        /// <summary>Owner name for clash messages: "Peglin Quick Restart: Restart".</summary>
        public static string Owner(ModKeyRow row) => BindingRows.Humanize((row.Keyboard ?? row.Controller).PluginName) + ": " + row.Label;

        private static string Pretty(string keyName)
        {
            if (keyName.StartsWith("Alpha") && keyName.Length == 6)
            {
                return keyName.Substring(5);
            }
            return BindingRows.Humanize(keyName);
        }
    }
}
