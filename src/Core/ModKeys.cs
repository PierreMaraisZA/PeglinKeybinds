using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PeglinKeybinds.Core
{
    /// <summary>
    /// A key setting found in another mod's BepInEx config: a KeyboardShortcut or KeyCode entry. Plain data, so the
    /// rules below can be unit tested without BepInEx.
    /// </summary>
    public sealed class ModKeySetting
    {
        public ModKeySetting(string pluginGuid, string pluginName, string section, string key, string description,
            string currentKey, string defaultKey, object handle = null)
        {
            PluginGuid = pluginGuid;
            PluginName = pluginName;
            Section = section ?? "";
            Key = key ?? "";
            Description = description ?? "";
            CurrentKey = currentKey ?? "None";
            DefaultKey = defaultKey ?? "None";
            Handle = handle;
        }

        public string PluginGuid { get; }
        public string PluginName { get; }
        public string Section { get; }
        public string Key { get; }
        public string Description { get; }

        /// <summary>Unity KeyCode name of the main key now ("None" when unbound).</summary>
        public string CurrentKey { get; }

        public string DefaultKey { get; }

        /// <summary>The plugin side's own object for this setting (the BepInEx config entry).</summary>
        public object Handle { get; }
    }

    /// <summary>One row in a mod's section: a keyboard setting, a controller setting, or both when they're a pair.</summary>
    public sealed class ModKeyRow
    {
        public ModKeyRow(string label, ModKeySetting keyboard, ModKeySetting controller)
        {
            Label = label;
            Keyboard = keyboard;
            Controller = controller;
        }

        public string Label { get; }
        public ModKeySetting Keyboard { get; }
        public ModKeySetting Controller { get; }

        /// <summary>The first sentence of the mod's description, saying what the key is for.</summary>
        public string Summary => ModKeys.Summary((Keyboard ?? Controller).Description);
    }

    public static class ModKeys
    {
        private static readonly Regex DevWords = new Regex(@"\b(debug|debugging|dev|developer|testing|test|tests|cheat|cheats|reload|hot reload)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ControllerWords = new Regex(@"\b(controller|joystick|gamepad|pad)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex JoystickKey = new Regex(@"^Joystick\d*Button(\d+)$", RegexOptions.CultureInvariant);

        private static readonly string[] TrailingWords = { "key", "keys", "hotkey", "shortcut", "button", "binding", "keybind", "bind" };
        private static readonly string[] LeadingWords = { "keyboard", "controller", "joystick", "gamepad", "pad" };

        /// <summary>
        /// The usual Xbox-layout meaning of Unity's JoystickButton numbers on Windows, as short labels matching
        /// <see cref="PromptNames.ElementLabel"/> so they can be compared with the game's own controller bindings.
        /// </summary>
        private static readonly string[] JoystickButtonLabels = { "A", "B", "X", "Y", "LB", "RB", "BK", "ST", "L3", "R3" };

        /// <summary>
        /// Developer and testing keys (debug menus, scene reloads, test tools) aren't for players, so they're left out.
        /// Judged on the mod's name and the setting's section, name and description.
        /// </summary>
        public static bool IsDevSetting(ModKeySetting setting)
        {
            string text = string.Join(" | ", BindingRows.Humanize(setting.PluginName), BindingRows.Humanize(setting.Section),
                BindingRows.Humanize(setting.Key), setting.Description);
            return DevWords.IsMatch(text);
        }

        /// <summary>A controller setting holds a joystick button, or says so in its name.</summary>
        public static bool IsController(ModKeySetting setting)
        {
            return IsJoystickKey(setting.CurrentKey) || IsJoystickKey(setting.DefaultKey)
                || ControllerWords.IsMatch(BindingRows.Humanize(setting.Key));
        }

        public static bool IsJoystickKey(string keyCodeName)
        {
            return keyCodeName != null && JoystickKey.IsMatch(keyCodeName);
        }

        /// <summary>Unity button number of a JoystickButtonN key name, or -1.</summary>
        public static int JoystickButtonNumber(string keyCodeName)
        {
            Match m = keyCodeName == null ? Match.Empty : JoystickKey.Match(keyCodeName);
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : -1;
        }

        /// <summary>Short label for a JoystickButtonN key ("JoystickButton8" -> "L3"), or "B12" past the standard ten.</summary>
        public static string JoystickButtonLabel(string keyCodeName)
        {
            int n = JoystickButtonNumber(keyCodeName);
            if (n < 0)
            {
                return null;
            }
            return n < JoystickButtonLabels.Length ? JoystickButtonLabels[n] : "B" + n;
        }

        /// <summary>
        /// Rows for every mod key players should see, grouped by mod in the order given. Within a mod and config
        /// section, a keyboard setting and a controller setting with the same action name share a row, even when the
        /// mod has other keys in that section (PeglinFonts has both F9 and F8 in General).
        /// </summary>
        public static List<(string plugin, List<ModKeyRow> rows)> BuildSections(IEnumerable<ModKeySetting> settings)
        {
            var result = new List<(string, List<ModKeyRow>)>();
            foreach (var plugin in settings.Where(s => !IsDevSetting(s)).GroupBy(s => s.PluginGuid))
            {
                var rows = new List<ModKeyRow>();
                foreach (var section in plugin.GroupBy(s => s.Section))
                {
                    var sectionSettings = section.ToList();
                    var pairs = new Dictionary<ModKeySetting, ModKeySetting>();
                    foreach (ModKeySetting controller in sectionSettings.Where(IsController))
                    {
                        ModKeySetting keyboard = sectionSettings.FirstOrDefault(s =>
                            !IsController(s) && !pairs.ContainsValue(s) && Label(s.Key) == Label(controller.Key));
                        if (keyboard != null)
                        {
                            pairs[controller] = keyboard;
                        }
                    }
                    foreach (ModKeySetting setting in sectionSettings)
                    {
                        if (pairs.ContainsKey(setting))
                        {
                            continue;
                        }
                        if (pairs.ContainsValue(setting))
                        {
                            ModKeySetting controller = pairs.First(p => p.Value == setting).Key;
                            rows.Add(new ModKeyRow(Label(setting.Key), setting, controller));
                        }
                        else if (IsController(setting))
                        {
                            // No keyboard partner: say so, or it would look like a duplicate of a keyboard action.
                            rows.Add(new ModKeyRow(Label(setting.Key) + " (Controller)", null, setting));
                        }
                        else
                        {
                            rows.Add(new ModKeyRow(Label(setting.Key), setting, null));
                        }
                    }
                }
                if (rows.Count > 0)
                {
                    result.Add((BindingRows.Humanize(plugin.First().PluginName), rows));
                }
            }
            return result;
        }

        /// <summary>
        /// A row name from the setting name: "RestartKey" -> "Restart", "ControllerRestartButton" -> "Restart",
        /// "Toggle GUI Key" -> "Toggle GUI".
        /// </summary>
        public static string Label(string settingKey)
        {
            var words = BindingRows.Humanize(settingKey).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            while (words.Count > 1 && TrailingWords.Contains(words[words.Count - 1].ToLowerInvariant()))
            {
                words.RemoveAt(words.Count - 1);
            }
            // "ControllerRestartButton" and "RerollControllerButton" both mean the Restart/Reroll action.
            words.RemoveAll(w => LeadingWords.Contains(w.ToLowerInvariant()));
            if (words.Count == 0)
            {
                return BindingRows.Humanize(settingKey);
            }
            words[0] = char.ToUpperInvariant(words[0][0]) + words[0].Substring(1);
            return string.Join(" ", words);
        }

        /// <summary>First sentence of a description, without trailing links ("Key names: https://...").</summary>
        public static string Summary(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return "";
            }
            string text = Regex.Replace(description, @"\S*https?://\S+", "").Trim();
            int end = text.IndexOf(". ", StringComparison.Ordinal);
            if (end >= 0)
            {
                text = text.Substring(0, end + 1);
            }
            return text.Trim();
        }
    }

    /// <summary>
    /// Finds keys used by more than one owner. The game shares some keys between its own actions on purpose (Enter is
    /// Menu Select and Fire), so two game actions only clash on a key they don't share in the game's defaults, like
    /// Esc on both Pause and Discard Orb. Anything involving a mod key always clashes.
    /// </summary>
    public sealed class ClashIndex
    {
        private readonly Dictionary<string, List<(string owner, bool isMod)>> _users = new Dictionary<string, List<(string, bool)>>();
        private readonly Dictionary<string, HashSet<string>> _defaultUsers = new Dictionary<string, HashSet<string>>();

        /// <summary>Records that a game action has this key in the game's default bindings.</summary>
        public void AddDefault(string keyId, string owner)
        {
            if (keyId == null || owner == null)
            {
                return;
            }
            if (!_defaultUsers.TryGetValue(keyId, out var set))
            {
                _defaultUsers[keyId] = set = new HashSet<string>();
            }
            set.Add(owner);
        }

        private bool SharedByDefault(string keyId, string a, string b)
        {
            return _defaultUsers.TryGetValue(keyId, out var set) && set.Contains(a) && set.Contains(b);
        }

        /// <summary>Identity of a keyboard key with modifiers: left/right variants of a modifier count as the same.</summary>
        public static string KeyboardId(string keyCodeName, IEnumerable<string> modifierNames = null)
        {
            var mods = (modifierNames ?? Enumerable.Empty<string>()).Select(NormalizeModifier).Where(m => m != null).Distinct().OrderBy(m => m);
            return "K:" + keyCodeName + "|" + string.Join("+", mods);
        }

        /// <summary>Identity of a controller button, by its short label ("A", "L3").</summary>
        public static string PadId(string label) => "P:" + label;

        public void Add(string keyId, string owner, bool isMod)
        {
            if (keyId == null || keyId.StartsWith("K:None|") || owner == null)
            {
                return;
            }
            if (!_users.TryGetValue(keyId, out var list))
            {
                _users[keyId] = list = new List<(string, bool)>();
            }
            if (!list.Contains((owner, isMod)))
            {
                list.Add((owner, isMod));
            }
        }

        /// <summary>The other owners of a key that clash with <paramref name="owner"/>.</summary>
        public List<string> ClashesFor(string keyId, string owner, bool isMod)
        {
            if (keyId == null || !_users.TryGetValue(keyId, out var list))
            {
                return new List<string>();
            }
            return list.Where(u => u.owner != owner && (isMod || u.isMod || !SharedByDefault(keyId, owner, u.owner)))
                .Select(u => u.owner).Distinct().ToList();
        }

        private static string NormalizeModifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            string n = name.ToLowerInvariant();
            if (n.Contains("control") || n == "ctrl") return "Ctrl";
            if (n.Contains("shift")) return "Shift";
            if (n.Contains("alt")) return "Alt";
            if (n.Contains("command") || n.Contains("apple") || n == "cmd") return "Cmd";
            if (n.Contains("windows")) return "Win";
            return null;
        }
    }
}
