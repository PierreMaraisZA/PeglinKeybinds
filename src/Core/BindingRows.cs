using System.Collections.Generic;
using System.Linq;

namespace PeglinKeybinds.Core
{
    /// <summary>The parts of a game action the menu needs. Built from Rewired's InputAction in the plugin.</summary>
    public sealed class ActionInfo
    {
        public ActionInfo(int id, string name, string descriptiveName, bool isAxis, string positiveName = null, string negativeName = null)
        {
            Id = id;
            Name = name;
            DescriptiveName = descriptiveName;
            IsAxis = isAxis;
            PositiveName = positiveName;
            NegativeName = negativeName;
        }

        public int Id { get; }
        public string Name { get; }
        public string DescriptiveName { get; }
        public bool IsAxis { get; }
        public string PositiveName { get; }
        public string NegativeName { get; }
    }

    /// <summary>One line of the menu: a section heading, or one direction of one action within a map category.</summary>
    public sealed class BindingRow
    {
        private BindingRow(string label, int categoryId, int actionId, bool positive, bool isAxis, bool isHeader)
        {
            Label = label;
            CategoryId = categoryId;
            ActionId = actionId;
            Positive = positive;
            IsAxis = isAxis;
            IsHeader = isHeader;
        }

        public string Label { get; }
        public int CategoryId { get; }
        public int ActionId { get; }

        /// <summary>For axis actions, which direction this row binds. Always true for button actions.</summary>
        public bool Positive { get; }

        public bool IsAxis { get; }
        public bool IsHeader { get; }

        public static BindingRow Header(string label) => new BindingRow(label, -1, -1, true, false, true);

        public static BindingRow Action(string label, int categoryId, int actionId, bool positive, bool isAxis) =>
            new BindingRow(label, categoryId, actionId, positive, isAxis, false);
    }

    public static class BindingRows
    {
        /// <summary>
        /// Lays out the menu: actions grouped into sections (Gameplay, Menus, ...) in a sensible order, each section
        /// under a heading, with a row per button action or two rows (one per direction) per axis action.
        /// </summary>
        public static List<BindingRow> Build(IEnumerable<(int categoryId, ActionInfo action)> actions)
        {
            var ordered = actions
                .Select((a, i) => (a.categoryId, a.action, index: i))
                .OrderBy(a => System.Array.IndexOf(ActionNames.SectionOrder, ActionNames.Section(a.action.Name)))
                .ThenBy(a => ActionNames.Order(a.action.Name))
                .ThenBy(a => a.index)
                .ToList();

            var rows = new List<BindingRow>();
            string section = null;
            foreach (var (categoryId, action, _) in ordered)
            {
                string actionSection = ActionNames.Section(action.Name);
                if (actionSection != section)
                {
                    rows.Add(BindingRow.Header(actionSection));
                    section = actionSection;
                }
                if (action.IsAxis)
                {
                    rows.Add(BindingRow.Action(Label(action, true), categoryId, action.Id, true, true));
                    rows.Add(BindingRow.Action(Label(action, false), categoryId, action.Id, false, true));
                }
                else
                {
                    rows.Add(BindingRow.Action(Label(action, true), categoryId, action.Id, true, false));
                }
            }
            return rows;
        }

        /// <summary>
        /// Our name for the action if we have one, otherwise the game's descriptive name, otherwise the internal name
        /// with spaces added ("UISubmit" -> "UI Submit"). Axis directions get their own names or a +/- suffix.
        /// </summary>
        public static string Label(ActionInfo action, bool positive = true)
        {
            string known = ActionNames.Label(action.Name, positive);
            if (known != null)
            {
                return known;
            }
            string name = !string.IsNullOrWhiteSpace(action.DescriptiveName) ? action.DescriptiveName.Trim() : Humanize(action.Name);
            if (!action.IsAxis)
            {
                return name;
            }
            string direction = positive ? action.PositiveName : action.NegativeName;
            return !string.IsNullOrWhiteSpace(direction) ? direction.Trim() : name + (positive ? " +" : " -");
        }

        public static string Humanize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }
            var chars = new List<char>(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_')
                {
                    c = ' ';
                }
                else if (i > 0 && char.IsUpper(c))
                {
                    char prev = name[i - 1];
                    bool nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    // Break before a capital that starts a word: "deleteOrb", "UISubmit" (the S), "OSX" stays whole.
                    if (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && nextIsLower))
                    {
                        chars.Add(' ');
                    }
                }
                chars.Add(c);
            }
            return new string(chars.ToArray()).Trim();
        }
    }
}
