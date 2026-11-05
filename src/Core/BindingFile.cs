using System;
using System.Collections.Generic;
using System.Text;

namespace PeglinKeybinds.Core
{
    /// <summary>
    /// Reads and writes the saved bindings file: one map per line as <c>key=base64(xml)</c>. The XML is Rewired's own
    /// controller map format, base64'd so its newlines and '=' signs can't break the line format. Lines that don't
    /// parse are skipped, so a hand-edited or truncated file loses only the broken entries.
    /// </summary>
    public static class BindingFile
    {
        private const string Header = "# PeglinKeybinds saved bindings. Delete this file to go back to the game's defaults.";

        public static string Write(IDictionary<string, string> maps)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            var keys = new List<string>(maps.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                if (!IsValidKey(key))
                {
                    throw new ArgumentException($"Key '{key}' can't contain '=', '#' at the start, or line breaks.", nameof(maps));
                }
                sb.Append(key).Append('=').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(maps[key] ?? ""))).Append('\n');
            }
            return sb.ToString();
        }

        public static Dictionary<string, string> Read(string text) => Read(text, out _);

        /// <summary>As <see cref="Read(string)"/>, and reports how many lines couldn't be read so they can be mentioned.</summary>
        public static Dictionary<string, string> Read(string text, out int skipped)
        {
            skipped = 0;
            var maps = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text))
            {
                return maps;
            }
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    skipped++;
                    continue;
                }
                try
                {
                    maps[line.Substring(0, eq)] = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(eq + 1)));
                }
                catch (FormatException)
                {
                    // Corrupt entry: skip it and keep the rest.
                    skipped++;
                }
            }
            return maps;
        }

        private static bool IsValidKey(string key)
        {
            return !string.IsNullOrEmpty(key) && key[0] != '#' && key.IndexOfAny(new[] { '=', '\n', '\r' }) < 0 && key.Trim() == key;
        }
    }
}
