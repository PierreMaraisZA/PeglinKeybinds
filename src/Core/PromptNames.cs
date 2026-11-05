using System.Collections.Generic;

namespace PeglinKeybinds.Core
{
    /// <summary>
    /// Names that link the game's button prompt icons to inputs: which game action each prompt icon set stands for,
    /// which keys the game has key cap art for, and short labels for drawing icons of everything else.
    /// </summary>
    public static class PromptNames
    {
        /// <summary>
        /// The game's prompt icon sets (ControllerIconMapping assets) and the action each one shows the button for.
        /// Worked out from each set's default key and button against the game's default bindings.
        /// </summary>
        private static readonly Dictionary<string, (string action, bool positive)> IconSetActions = new Dictionary<string, (string, bool)>
        {
            ["AcceptIcons"] = ("UISubmit", true),
            ["AddOrbMapping"] = ("CatalogAddOrb", true),
            ["ApplyIcons"] = ("UIApply", true),
            ["BackIcons"] = ("UICancel", true),
            ["CruciballToggleButtons"] = ("CruciballToggle", true),
            ["CustomStartIcons"] = ("CustomStartOption", true),
            ["CycleOrbLevelButtons"] = ("CatalogCycleOrbLevel", true),
            ["DeleteOrbButtons"] = ("CatalogDeleteOrb", true),
            ["DPadLeftButton"] = ("UIHorizontal", false),
            ["DPadRightButton"] = ("UIHorizontal", true),
            ["LeftBumperIconsDark"] = ("MenuTabLeft", true),
            ["LeftPageIcons"] = ("CatalogPageLeft", true),
            ["LeftPanelCatalogButtonMapping"] = ("CatalogPanelTabLeft", true),
            ["MapIcons"] = ("MapButton", true),
            ["RemoveOrbButtons"] = ("CatalogRemoveOrb", true),
            ["RightBumperIconsDark"] = ("MenuTabRight", true),
            ["RightPageIcons"] = ("CatalogPageRight", true),
            ["RightPanelCatalogButtonMapping"] = ("CatalogPanelTabRight", true),
            ["StartButtons"] = ("Pause", true),
            // The arrows either side of the battle's target reticle (Shift / Tab, LB / RB by default).
            ["TargetMoveLeftIconsDark"] = ("TargetLeft", true),
            ["TargetMoveRightIconsDark"] = ("TargetRight", true),
        };

        /// <summary>
        /// Game scripts whose prompt icon belongs to an action the script reads in code: the battle Fire button
        /// (Confirm) and the orb discard bin (Back). Their icon sets are shared with menu prompts, so the set alone
        /// would point at the wrong action.
        /// </summary>
        public static string ActionForScript(string scriptTypeName)
        {
            switch (scriptTypeName)
            {
                case "PachinkoFireButton": return "Confirm";
                case "OrbDiscardButton": return "Back";
                default: return null;
            }
        }

        private static readonly HashSet<string> CoreIconSets = new HashSet<string>
        {
            "AcceptIcons", "BackIcons", "ApplyIcons", "StartButtons", "LeftBumperIconsDark", "RightBumperIconsDark", "DPadLeftButton", "DPadRightButton",
        };

        /// <summary>
        /// The game's main menu prompts, whose art always matches their action's default button. Other sets sometimes
        /// borrow another button's art, so these are trusted more when working out which art shows which button.
        /// </summary>
        public static bool IsCoreIconSet(string iconSetName) => iconSetName != null && CoreIconSets.Contains(iconSetName);

        public static bool TryGetIconSetAction(string iconSetName, out string action, out bool positive)
        {
            if (iconSetName != null && IconSetActions.TryGetValue(iconSetName, out var entry))
            {
                action = entry.action;
                positive = entry.positive;
                return true;
            }
            action = null;
            positive = true;
            return false;
        }

        private static readonly string[] IconSetSuffixes = { "Dark", "Light", "Icons", "Icon", "Buttons", "Button", "Mapping", "Prompts", "Prompt" };

        /// <summary>
        /// An icon set's name without the words the game tacks on: "SpeedupIcons" -> "Speedup",
        /// "LeftBumperIconsDark" -> "LeftBumper".
        /// </summary>
        public static string IconSetStem(string iconSetName)
        {
            string stem = iconSetName ?? "";
            bool trimmed = true;
            while (trimmed)
            {
                trimmed = false;
                foreach (string suffix in IconSetSuffixes)
                {
                    if (stem.Length > suffix.Length && stem.EndsWith(suffix, System.StringComparison.Ordinal))
                    {
                        stem = stem.Substring(0, stem.Length - suffix.Length);
                        trimmed = true;
                    }
                }
            }
            return stem;
        }

        /// <summary>
        /// The key a game key cap sprite shows, from its name: "keyboard_enter_up" -> "enter", "keyboard_back_alt_down"
        /// -> "back", "keyboard_plus_0" -> "plus". Null if it isn't a keyboard sprite.
        /// </summary>
        public static string KeyTokenFromSpriteName(string spriteName)
        {
            const string prefix = "keyboard_";
            if (string.IsNullOrEmpty(spriteName) || !spriteName.StartsWith(prefix))
            {
                return null;
            }
            string rest = spriteName.Substring(prefix.Length);
            if (rest.StartsWith("light_"))
            {
                rest = rest.Substring("light_".Length);
            }
            int underscore = rest.IndexOf('_');
            return underscore < 0 ? rest : rest.Substring(0, underscore);
        }

        /// <summary>The token a key's sprite would have (see <see cref="KeyTokenFromSpriteName"/>), from Unity's KeyCode name.</summary>
        public static string KeyToken(string keyCodeName)
        {
            switch (keyCodeName)
            {
                case "Return":
                case "KeypadEnter": return "enter";
                case "Backspace": return "back";
                case "Escape": return "esc";
                case "LeftArrow": return "left";
                case "RightArrow": return "right";
                case "LeftShift":
                case "RightShift": return "shift";
                case "Tab": return "tab";
                case "Delete": return "del";
                case "Plus":
                case "KeypadPlus": return "plus";
                case "Minus":
                case "KeypadMinus": return "minus";
            }
            // Otherwise the game names key sprites after the key: keyboard_space_up, keyboard_f_up.
            return string.IsNullOrEmpty(keyCodeName) || keyCodeName == "None" ? null : keyCodeName.ToLowerInvariant();
        }

        /// <summary>A short label to draw on a key cap, from Unity's KeyCode name.</summary>
        public static string KeyLabel(string keyCodeName)
        {
            if (string.IsNullOrEmpty(keyCodeName))
            {
                return "?";
            }
            switch (keyCodeName)
            {
                case "Return": return "ENTER";
                case "KeypadEnter": return "ENTER";
                case "Space": return "SPACE";
                case "Backspace": return "BKSP";
                case "Escape": return "ESC";
                case "Delete": return "DEL";
                case "Insert": return "INS";
                case "PageUp": return "PGUP";
                case "PageDown": return "PGDN";
                case "CapsLock": return "CAPS";
                case "LeftShift":
                case "RightShift": return "SHIFT";
                case "LeftControl":
                case "RightControl": return "CTRL";
                case "LeftAlt":
                case "RightAlt":
                case "AltGr": return "ALT";
                case "LeftCommand":
                case "RightCommand": return "CMD";
                case "LeftWindows":
                case "RightWindows": return "WIN";
                case "UpArrow": return "↑";
                case "DownArrow": return "↓";
                case "LeftArrow": return "←";
                case "RightArrow": return "→";
                case "Plus": return "+";
                case "Minus": return "-";
                case "Equals": return "=";
                case "Slash": return "/";
                case "Backslash": return "\\";
                case "Period": return ".";
                case "Comma": return ",";
                case "Semicolon": return ";";
                case "Quote": return "'";
                case "BackQuote": return "`";
                case "LeftBracket": return "[";
                case "RightBracket": return "]";
                case "Numlock": return "NUM";
                case "ScrollLock": return "SCRL";
                case "Print": return "PRT";
            }
            if (keyCodeName.StartsWith("Alpha") && keyCodeName.Length == 6)
            {
                return keyCodeName.Substring(5);
            }
            if (keyCodeName.StartsWith("Keypad"))
            {
                string rest = keyCodeName.Substring("Keypad".Length);
                switch (rest)
                {
                    case "Plus": rest = "+"; break;
                    case "Minus": rest = "-"; break;
                    case "Multiply": rest = "*"; break;
                    case "Divide": rest = "/"; break;
                    case "Period": rest = "."; break;
                    case "Equals": rest = "="; break;
                }
                return "N" + rest;
            }
            string upper = keyCodeName.ToUpperInvariant();
            return upper.Length <= 5 ? upper : upper.Substring(0, 5);
        }

        /// <summary>
        /// A short label to draw on a controller button icon, from Rewired's element name ("Left Stick Button" -> "L3").
        /// For a stick, <paramref name="positive"/> says which way it was pushed, and the label gets an arrow.
        /// </summary>
        public static string ElementLabel(string elementName, bool isAxis = false, bool positive = true)
        {
            if (string.IsNullOrWhiteSpace(elementName))
            {
                return "?";
            }
            string name = elementName.Trim();
            string lower = name.ToLowerInvariant();
            if (lower.Contains("d-pad") || lower.Contains("dpad"))
            {
                if (lower.Contains("up")) return "↑";
                if (lower.Contains("down")) return "↓";
                if (lower.Contains("left")) return "←";
                if (lower.Contains("right")) return "→";
            }
            bool left = lower.StartsWith("left");
            bool right = lower.StartsWith("right");
            if ((left || right) && lower.Contains("stick"))
            {
                string stick = left ? "L" : "R";
                if (lower.Contains("button") || lower.Contains("click") || lower.Contains("press"))
                {
                    return stick + "3";
                }
                if (lower.EndsWith(" x"))
                {
                    return stick + "S" + (positive ? "→" : "←");
                }
                if (lower.EndsWith(" y"))
                {
                    // Rewired's stick Y axes are positive when pushed up.
                    return stick + "S" + (positive ? "↑" : "↓");
                }
                return stick + "S";
            }
            if ((left || right) && lower.Contains("trigger")) return left ? "LT" : "RT";
            if ((left || right) && (lower.Contains("shoulder") || lower.Contains("bumper"))) return left ? "LB" : "RB";
            switch (lower)
            {
                case "back":
                case "view":
                case "select":
                case "share":
                case "create":
                case "minus": return "BK";
                case "start":
                case "menu":
                case "options":
                case "plus": return "ST";
                case "guide":
                case "home": return "HM";
            }
            if (name.Length <= 2)
            {
                return name.ToUpperInvariant();
            }
            // Initials of the words, e.g. "Button 12" -> "B12".
            var label = new System.Text.StringBuilder();
            foreach (string word in name.Split(' ', '-', '_'))
            {
                if (word.Length == 0)
                {
                    continue;
                }
                bool number = true;
                foreach (char c in word)
                {
                    number &= char.IsDigit(c);
                }
                label.Append(number ? word : word.Substring(0, 1).ToUpperInvariant());
            }
            string result = label.ToString();
            return result.Length <= 3 ? result : result.Substring(0, 3);
        }
    }
}
