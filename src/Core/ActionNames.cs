using System.Collections.Generic;

namespace PeglinKeybinds.Core
{
    /// <summary>
    /// Player-facing names and grouping for Peglin's input actions. The game's own action names are internal
    /// ("UISubmit", "HorizontalSlow") and several share a display name, so the controls screen uses these instead.
    /// Actions not listed here still show, under "Other", with a name made from the internal one.
    /// </summary>
    public static class ActionNames
    {
        public const string Gameplay = "Gameplay";
        public const string Menus = "Menus";
        public const string Loadout = "Loadout & Encirclepedia";
        public const string Other = "Other";

        private sealed class Entry
        {
            public Entry(string section, string label, string negativeLabel = null)
            {
                Section = section;
                Label = label;
                NegativeLabel = negativeLabel;
            }

            public string Section { get; }
            public string Label { get; }
            public string NegativeLabel { get; }
        }

        // In display order. Axis actions give the positive direction's name first.
        private static readonly List<KeyValuePair<string, Entry>> Entries = new List<KeyValuePair<string, Entry>>
        {
            Pair("Confirm", Gameplay, "Fire / End Shot"),
            Pair("Horizontal", Gameplay, "Aim Right", "Aim Left"),
            Pair("Vertical", Gameplay, "Aim Up (Aim Limiter)", "Aim Down (Aim Limiter)"),
            Pair("HorizontalSlow", Gameplay, "Aim Right (Fine)", "Aim Left (Fine)"),
            Pair("VerticalSlow", Gameplay, "Aim Up (Fine)", "Aim Down (Fine)"),
            Pair("TargetLeft", Gameplay, "Previous Target"),
            Pair("TargetRight", Gameplay, "Next Target"),
            Pair("Speedup", Gameplay, "Speed Up"),
            Pair("MapButton", Gameplay, "Map / Item Info"),
            Pair("DeckButton", Gameplay, "Orb Bag"),
            Pair("RelicAndOrbInfo", Gameplay, "Inspect"),
            Pair("EnemyInfo", Gameplay, "Enemy Info"),
            Pair("RunSummaryOrbsLeft", Menus, "Run Summary: Previous Orbs"),
            Pair("RunSummaryOrbsRight", Menus, "Run Summary: Next Orbs"),
            Pair("ShowAllUpgrades", Menus, "Show All Upgrade Levels"),
            Pair("Back", Gameplay, "Discard Orb / Close Inspect"),
            Pair("Pause", Gameplay, "Pause"),
            Pair("PauseMenu", Gameplay, "Pause Menu"),
            Pair("UISubmit", Menus, "Select"),
            Pair("UICancel", Menus, "Back"),
            Pair("UIApply", Menus, "Apply"),
            Pair("UIVertical", Menus, "Up", "Down"),
            Pair("UIHorizontal", Menus, "Right", "Left"),
            Pair("MenuTabLeft", Menus, "Previous Tab"),
            Pair("MenuTabRight", Menus, "Next Tab"),
            Pair("CruciballToggle", Menus, "Cruciball"),
            Pair("CustomStartOption", Menus, "Custom Start"),
            Pair("CopySeed", Menus, "Copy Seed"),
            Pair("PasteSeed", Menus, "Paste Seed"),
            Pair("CopySeedOSX", Menus, "Copy Seed"),
            Pair("PasteSeedOSX", Menus, "Paste Seed"),
            Pair("CatalogAddOrb", Loadout, "Add Orb"),
            Pair("CatalogRemoveOrb", Loadout, "Remove Orb"),
            Pair("CatalogDeleteOrb", Loadout, "Delete from Loadout"),
            Pair("CatalogCycleOrbLevel", Loadout, "Cycle Orb Level"),
            Pair("CatalogPageLeft", Loadout, "Previous Page"),
            Pair("CatalogPageRight", Loadout, "Next Page"),
            Pair("CatalogPanelTabLeft", Loadout, "Previous Panel"),
            Pair("CatalogPanelTabRight", Loadout, "Next Panel"),
        };

        private static readonly Dictionary<string, (Entry entry, int order)> ByName = BuildIndex();

        public static readonly string[] SectionOrder = { Gameplay, Menus, Loadout, Other };

        public static string Section(string actionName)
        {
            return actionName != null && ByName.TryGetValue(actionName, out var e) ? e.entry.Section : Other;
        }

        /// <summary>Position in the list; actions we don't know about go last.</summary>
        public static int Order(string actionName)
        {
            return actionName != null && ByName.TryGetValue(actionName, out var e) ? e.order : int.MaxValue;
        }

        /// <summary>The name to show, or null if the action isn't one we have a name for.</summary>
        public static string Label(string actionName, bool positive)
        {
            if (actionName == null || !ByName.TryGetValue(actionName, out var e))
            {
                return null;
            }
            return positive || e.entry.NegativeLabel == null ? e.entry.Label : e.entry.NegativeLabel;
        }

        private static KeyValuePair<string, Entry> Pair(string name, string section, string label, string negativeLabel = null)
        {
            return new KeyValuePair<string, Entry>(name, new Entry(section, label, negativeLabel));
        }

        private static Dictionary<string, (Entry, int)> BuildIndex()
        {
            var index = new Dictionary<string, (Entry, int)>();
            for (int i = 0; i < Entries.Count; i++)
            {
                index[Entries[i].Key] = (Entries[i].Value, i);
            }
            return index;
        }
    }
}
