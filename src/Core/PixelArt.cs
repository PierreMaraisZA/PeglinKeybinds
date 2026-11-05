using System;
using System.Collections.Generic;

namespace PeglinKeybinds.Core
{
    /// <summary>Which of a prompt icon's colours a pixel is. The plugin turns these into the game's own palette.</summary>
    public enum Ink
    {
        Clear = 0,
        Outline = 1,
        Side = 2,
        Face = 3,
        Bottom = 4,
        Glyph = 5,
    }

    /// <summary>A small image as rows of <see cref="Ink"/>, top row first.</summary>
    public sealed class PixelGrid
    {
        private readonly Ink[] _pixels;

        public PixelGrid(int width, int height)
        {
            Width = width;
            Height = height;
            _pixels = new Ink[width * height];
        }

        public int Width { get; }
        public int Height { get; }

        public Ink this[int x, int y]
        {
            get => _pixels[y * Width + x];
            set => _pixels[y * Width + x] = value;
        }

        /// <summary>One string per row, using the Ink numbers (space for clear). Handy for tests.</summary>
        public string[] ToRows()
        {
            var rows = new string[Height];
            for (int y = 0; y < Height; y++)
            {
                var chars = new char[Width];
                for (int x = 0; x < Width; x++)
                {
                    Ink ink = this[x, y];
                    chars[x] = ink == Ink.Clear ? ' ' : (char)('0' + (int)ink);
                }
                rows[y] = new string(chars);
            }
            return rows;
        }
    }

    /// <summary>
    /// Draws button prompt icons in the style of Peglin's own: keyboard keys (a dark key cap with a 3x5 pixel label,
    /// like the game's "ESC" and "TAB" keys) and round controller buttons (like the game's A button). Used for keys and
    /// buttons the game has no artwork for.
    /// </summary>
    public static class PixelArt
    {
        public const int GlyphWidth = 3;
        public const int GlyphHeight = 5;

        // Traced from the game's xboxone_a_up with the letter removed: 1 outline, 2 side, 3 face, 4 bottom.
        private static readonly string[] PadTemplate =
        {
            "   11111   ",
            " 111444111 ",
            " 143333341 ",
            "11333333311",
            "14333333341",
            "13333333331",
            "14333333341",
            "12333333321",
            "11433333411",
            " 122444221 ",
            " 111222111 ",
            "   11111   ",
        };

        private const int PadGlyphTop = 3;
        private const int PadStretchColumn = 5;

        private static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            ['A'] = new[] { "010", "101", "111", "101", "101" },
            ['B'] = new[] { "110", "101", "110", "101", "110" },
            ['C'] = new[] { "011", "100", "100", "100", "011" },
            ['D'] = new[] { "110", "101", "101", "101", "110" },
            ['E'] = new[] { "111", "100", "110", "100", "111" },
            ['F'] = new[] { "111", "100", "110", "100", "100" },
            ['G'] = new[] { "011", "100", "101", "101", "011" },
            ['H'] = new[] { "101", "101", "111", "101", "101" },
            ['I'] = new[] { "111", "010", "010", "010", "111" },
            ['J'] = new[] { "001", "001", "001", "101", "010" },
            ['K'] = new[] { "101", "101", "110", "101", "101" },
            ['L'] = new[] { "100", "100", "100", "100", "111" },
            ['M'] = new[] { "101", "111", "111", "101", "101" },
            ['N'] = new[] { "110", "101", "101", "101", "101" },
            ['O'] = new[] { "010", "101", "101", "101", "010" },
            ['P'] = new[] { "110", "101", "110", "100", "100" },
            ['Q'] = new[] { "010", "101", "101", "110", "011" },
            ['R'] = new[] { "110", "101", "110", "101", "101" },
            ['S'] = new[] { "011", "100", "010", "001", "110" },
            ['T'] = new[] { "111", "010", "010", "010", "010" },
            ['U'] = new[] { "101", "101", "101", "101", "111" },
            ['V'] = new[] { "101", "101", "101", "101", "010" },
            ['W'] = new[] { "101", "101", "111", "111", "101" },
            ['X'] = new[] { "101", "101", "010", "101", "101" },
            ['Y'] = new[] { "101", "101", "010", "010", "010" },
            ['Z'] = new[] { "111", "001", "010", "100", "111" },
            ['0'] = new[] { "111", "101", "101", "101", "111" },
            ['1'] = new[] { "010", "110", "010", "010", "111" },
            ['2'] = new[] { "110", "001", "010", "100", "111" },
            ['3'] = new[] { "110", "001", "010", "001", "110" },
            ['4'] = new[] { "101", "101", "111", "001", "001" },
            ['5'] = new[] { "111", "100", "110", "001", "110" },
            ['6'] = new[] { "011", "100", "111", "101", "111" },
            ['7'] = new[] { "111", "001", "010", "010", "010" },
            ['8'] = new[] { "111", "101", "111", "101", "111" },
            ['9'] = new[] { "111", "101", "111", "001", "110" },
            ['+'] = new[] { "000", "010", "111", "010", "000" },
            ['-'] = new[] { "000", "000", "111", "000", "000" },
            ['='] = new[] { "000", "111", "000", "111", "000" },
            ['/'] = new[] { "001", "001", "010", "100", "100" },
            ['\\'] = new[] { "100", "100", "010", "001", "001" },
            ['.'] = new[] { "000", "000", "000", "000", "010" },
            [','] = new[] { "000", "000", "000", "010", "100" },
            [';'] = new[] { "000", "010", "000", "010", "100" },
            ['\''] = new[] { "010", "010", "000", "000", "000" },
            ['`'] = new[] { "100", "010", "000", "000", "000" },
            ['['] = new[] { "110", "100", "100", "100", "110" },
            [']'] = new[] { "011", "001", "001", "001", "011" },
            ['?'] = new[] { "110", "001", "010", "000", "010" },
            ['*'] = new[] { "000", "101", "010", "101", "000" },
            [' '] = new[] { "000", "000", "000", "000", "000" },
            ['↑'] = new[] { "010", "111", "010", "010", "010" },
            ['↓'] = new[] { "010", "010", "010", "111", "010" },
            ['←'] = new[] { "001", "011", "111", "011", "001" },
            ['→'] = new[] { "100", "110", "111", "110", "100" },
        };

        /// <summary>Width in pixels of a label in the 3x5 font, with one pixel between letters.</summary>
        public static int TextWidth(string label)
        {
            int n = Normalize(label).Length;
            return n == 0 ? 0 : n * (GlyphWidth + 1) - 1;
        }

        /// <summary>
        /// A keyboard key cap, 11 pixels tall like the game's. Pressed keys are drawn 2 pixels lower with the bottom
        /// edge hidden, as in the game's "_down" sprites.
        /// </summary>
        public static PixelGrid Keycap(string label, bool pressed)
        {
            string text = Normalize(label);
            int textWidth = TextWidth(text);
            int width = Math.Max(11, textWidth + 6);
            var grid = new PixelGrid(width, 11);
            int top = pressed ? 2 : 0;

            for (int x = 1; x < width - 1; x++)
            {
                grid[x, top] = Ink.Outline;
            }
            for (int x = 0; x < width; x++)
            {
                grid[x, top + 1] = x < 2 || x >= width - 2 ? Ink.Outline : Ink.Face;
            }
            // Five rows for the label plus one below it.
            for (int y = top + 2; y < top + 8; y++)
            {
                FaceRow(grid, y, width);
            }
            if (!pressed)
            {
                grid[0, 8] = Ink.Outline;
                grid[1, 8] = Ink.Side;
                for (int x = 2; x < width - 2; x++)
                {
                    grid[x, 8] = Ink.Bottom;
                }
                grid[width - 2, 8] = Ink.Side;
                grid[width - 1, 8] = Ink.Outline;
                grid[0, 9] = Ink.Outline;
                for (int x = 1; x < width - 1; x++)
                {
                    grid[x, 9] = Ink.Bottom;
                }
                grid[width - 1, 9] = Ink.Outline;
            }
            for (int x = 0; x < width; x++)
            {
                grid[x, 10] = Ink.Outline;
            }
            DrawText(grid, text, (width - textWidth) / 2, top + 2);
            return grid;
        }

        /// <summary>A round controller button like the game's face buttons, widened to fit longer labels.</summary>
        public static PixelGrid PadButton(string label)
        {
            string text = Normalize(label);
            int textWidth = TextWidth(text);
            int extra = Math.Max(0, textWidth - GlyphWidth);
            int baseWidth = PadTemplate[0].Length;
            var grid = new PixelGrid(baseWidth + extra, PadTemplate.Length);
            for (int y = 0; y < PadTemplate.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    // Columns left of the stretch column come straight from the template; the stretch column repeats.
                    int source = x <= PadStretchColumn ? x : x < PadStretchColumn + extra ? PadStretchColumn : x - extra;
                    char c = PadTemplate[y][source];
                    grid[x, y] = c == ' ' ? Ink.Clear : (Ink)(c - '0');
                }
            }
            DrawText(grid, text, (grid.Width - textWidth) / 2, PadGlyphTop);
            return grid;
        }

        /// <summary>Upper-cases the label and swaps letters the font doesn't have for '?'.</summary>
        public static string Normalize(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return "";
            }
            var chars = label.ToUpperInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!Font.ContainsKey(chars[i]))
                {
                    chars[i] = '?';
                }
            }
            return new string(chars);
        }

        private static void FaceRow(PixelGrid grid, int y, int width)
        {
            grid[0, y] = Ink.Outline;
            grid[1, y] = Ink.Side;
            for (int x = 2; x < width - 2; x++)
            {
                grid[x, y] = Ink.Face;
            }
            grid[width - 2, y] = Ink.Side;
            grid[width - 1, y] = Ink.Outline;
        }

        private static void DrawText(PixelGrid grid, string text, int left, int top)
        {
            for (int i = 0; i < text.Length; i++)
            {
                string[] glyph = Font[text[i]];
                int gx = left + i * (GlyphWidth + 1);
                for (int y = 0; y < GlyphHeight; y++)
                {
                    for (int x = 0; x < GlyphWidth; x++)
                    {
                        if (glyph[y][x] == '1' && gx + x >= 0 && gx + x < grid.Width && top + y < grid.Height)
                        {
                            grid[gx + x, top + y] = Ink.Glyph;
                        }
                    }
                }
            }
        }
    }
}
