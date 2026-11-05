using System.Globalization;
using System.Text;

namespace PeglinKeybinds.Core
{
    /// <summary>
    /// Names a saved controller map: which kind of controller (keyboard, or a specific controller model), and which
    /// map category and layout. Controllers are keyed by model rather than by connection, so bindings made on one
    /// Xbox pad apply to any Xbox pad, and survive unplugging it.
    /// </summary>
    public readonly struct MapKey
    {
        public MapKey(string controllerType, string hardwareId, int categoryId, int layoutId)
        {
            ControllerType = controllerType;
            HardwareId = Sanitize(hardwareId);
            CategoryId = categoryId;
            LayoutId = layoutId;
        }

        public string ControllerType { get; }
        public string HardwareId { get; }
        public int CategoryId { get; }
        public int LayoutId { get; }

        /// <summary>Prefix shared by every map of one controller, for finding or removing all of its saved maps.</summary>
        public static string ControllerPrefix(string controllerType, string hardwareId)
        {
            return controllerType + "|" + Sanitize(hardwareId) + "|";
        }

        public override string ToString()
        {
            return ControllerPrefix(ControllerType, HardwareId) +
                CategoryId.ToString(CultureInfo.InvariantCulture) + "|" + LayoutId.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string text, out MapKey key)
        {
            key = default;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            string[] parts = text.Split('|');
            if (parts.Length != 4 || parts[0].Length == 0 ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int category) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int layout))
            {
                return false;
            }
            key = new MapKey(parts[0], parts[1], category, layout);
            return true;
        }

        /// <summary>Hardware names come from the controller driver, so strip anything that would break the key or file format.</summary>
        private static string Sanitize(string hardwareId)
        {
            if (string.IsNullOrEmpty(hardwareId))
            {
                return "";
            }
            var sb = new StringBuilder(hardwareId.Length);
            foreach (char c in hardwareId.Trim())
            {
                sb.Append(c == '|' || c == '=' || c == '#' || char.IsControl(c) ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
