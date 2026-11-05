using System.Collections.Generic;
using System.Linq;
using PeglinKeybinds.Core;
using Xunit;

namespace PeglinKeybinds.Tests
{
    public class BindingFileTests
    {
        [Fact]
        public void WriteThenRead_RoundTripsXmlWithNewlinesAndEquals()
        {
            var maps = new Dictionary<string, string>
            {
                ["Keyboard||0|0"] = "<map a=\"1\">\n  <key>Space</key>\n</map>",
                ["Joystick|d74a350e-fe8b-4e9e-bbcd-efff16d34115|2|0"] = "<x b='=' />",
            };

            Dictionary<string, string> read = BindingFile.Read(BindingFile.Write(maps));

            Assert.Equal(maps, read);
        }

        [Fact]
        public void Read_SkipsCommentsBlankAndCorruptLines()
        {
            string text = "# comment\n\nnot a pair\n=noKey\nbad=!!!notbase64!!!\ngood=" +
                System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<ok/>")) + "\r\n";

            Dictionary<string, string> read = BindingFile.Read(text);

            Assert.Single(read);
            Assert.Equal("<ok/>", read["good"]);
        }

        [Fact]
        public void Read_EmptyOrNullGivesNoMaps()
        {
            Assert.Empty(BindingFile.Read(null));
            Assert.Empty(BindingFile.Read(""));
        }

        [Fact]
        public void Read_CountsOnlyLinesItHadToSkip()
        {
            string text = "# comment\n\nnot a pair\n=noKey\nbad=!!!notbase64!!!\ngood=" +
                System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<ok/>")) + "\n";

            BindingFile.Read(text, out int skipped);

            Assert.Equal(3, skipped);
        }

        [Fact]
        public void Read_HealthyFileSkipsNothing()
        {
            string text = BindingFile.Write(new Dictionary<string, string> { ["Keyboard||0|0"] = "<map/>" });

            BindingFile.Read(text, out int skipped);

            Assert.Equal(0, skipped);
        }

        [Theory]
        [InlineData("a=b")]
        [InlineData("#a")]
        [InlineData("a\nb")]
        [InlineData("")]
        public void Write_RejectsKeysThatWouldBreakTheFormat(string key)
        {
            Assert.Throws<System.ArgumentException>(() => BindingFile.Write(new Dictionary<string, string> { [key] = "x" }));
        }
    }

    public class MapKeyTests
    {
        [Fact]
        public void ToStringThenTryParse_RoundTrips()
        {
            var key = new MapKey("Joystick", "d74a350e-fe8b-4e9e-bbcd-efff16d34115", 3, 1);

            Assert.True(MapKey.TryParse(key.ToString(), out MapKey parsed));
            Assert.Equal("Joystick", parsed.ControllerType);
            Assert.Equal("d74a350e-fe8b-4e9e-bbcd-efff16d34115", parsed.HardwareId);
            Assert.Equal(3, parsed.CategoryId);
            Assert.Equal(1, parsed.LayoutId);
        }

        [Fact]
        public void HardwareNamesWithSeparatorsAreSanitised()
        {
            var key = new MapKey("Joystick", " Pad|Pro=2#\n ", 0, 0);

            Assert.Equal("Pad_Pro_2_", key.HardwareId);
            Assert.True(MapKey.TryParse(key.ToString(), out _));
        }

        [Fact]
        public void ControllerPrefix_MatchesOnlyThatControllersKeys()
        {
            string prefix = MapKey.ControllerPrefix("Joystick", "abc");

            Assert.StartsWith(prefix, new MapKey("Joystick", "abc", 5, 0).ToString());
            Assert.False(new MapKey("Joystick", "abcd", 5, 0).ToString().StartsWith(prefix));
            Assert.False(new MapKey("Keyboard", "abc", 5, 0).ToString().StartsWith(prefix));
        }

        [Theory]
        [InlineData("")]
        [InlineData("Keyboard||0")]
        [InlineData("Keyboard||x|0")]
        [InlineData("|hw|0|0")]
        public void TryParse_RejectsMalformedKeys(string text)
        {
            Assert.False(MapKey.TryParse(text, out _));
        }
    }

    public class BindingRowsTests
    {
        [Theory]
        [InlineData("UISubmit", "UI Submit")]
        [InlineData("CopySeedOSX", "Copy Seed OSX")]
        [InlineData("HorizontalSlow", "Horizontal Slow")]
        [InlineData("Pause", "Pause")]
        [InlineData("catalog_add_orb", "catalog add orb")]
        [InlineData("", "")]
        public void Humanize_SplitsInternalNames(string name, string expected)
        {
            Assert.Equal(expected, BindingRows.Humanize(name));
        }

        [Fact]
        public void Build_GroupsIntoSectionsInDisplayOrder_WithARowPerAxisDirection()
        {
            var rows = BindingRows.Build(new[]
            {
                (0, new ActionInfo(9, "UISubmit", "Menu Confirm", false)),
                (0, new ActionInfo(2, "Horizontal", "Horizontal", true, "Right", "Left")),
                (0, new ActionInfo(0, "Confirm", "Confirm", false)),
            });

            Assert.Equal(new[] { "Gameplay", "Fire / End Shot", "Aim Right", "Aim Left", "Menus", "Select" }, rows.Select(r => r.Label));
            Assert.True(rows[0].IsHeader);
            Assert.True(rows[2].Positive && rows[2].IsAxis);
            Assert.False(rows[3].Positive);
            Assert.Equal(2, rows[3].ActionId);
            Assert.False(rows[5].IsAxis);
        }

        [Fact]
        public void Build_UnknownActionsGoUnderOtherWithTheGameName()
        {
            var rows = BindingRows.Build(new[]
            {
                (0, new ActionInfo(50, "NewThing", "Do The New Thing", false)),
                (0, new ActionInfo(51, "NewAxis", "", true)),
            });

            Assert.Equal(new[] { "Other", "Do The New Thing", "New Axis +", "New Axis -" }, rows.Select(r => r.Label));
        }
    }

    public class PromptNamesTests
    {
        [Theory]
        [InlineData("keyboard_enter_up", "enter")]
        [InlineData("keyboard_back_alt_down", "back")]
        [InlineData("keyboard_plus_0", "plus")]
        [InlineData("keyboard_light_q_up", "q")]
        [InlineData("xboxone_a_up", null)]
        [InlineData(null, null)]
        public void KeyTokenFromSpriteName(string sprite, string expected)
        {
            Assert.Equal(expected, PromptNames.KeyTokenFromSpriteName(sprite));
        }

        [Theory]
        [InlineData("Return", "enter")]
        [InlineData("KeypadEnter", "enter")]
        [InlineData("RightShift", "shift")]
        [InlineData("Q", "q")]
        [InlineData("KeypadMinus", "minus")]
        [InlineData("Space", "space")]
        [InlineData("None", null)]
        public void KeyToken_MatchesTheGamesSpriteNames(string keyCode, string expected)
        {
            Assert.Equal(expected, PromptNames.KeyToken(keyCode));
        }

        [Theory]
        [InlineData("K", "K")]
        [InlineData("Alpha7", "7")]
        [InlineData("Keypad3", "N3")]
        [InlineData("Space", "SPACE")]
        [InlineData("LeftControl", "CTRL")]
        [InlineData("UpArrow", "↑")]
        [InlineData("F11", "F11")]
        [InlineData("Backslash", "\\")]
        [InlineData("Mouse3", "MOUSE")]
        public void KeyLabel_IsShortEnoughForAKeyCap(string keyCode, string expected)
        {
            Assert.Equal(expected, PromptNames.KeyLabel(keyCode));
        }

        [Theory]
        [InlineData("Left Stick Button", false, true, "L3")]
        [InlineData("Right Stick X", true, false, "RS←")]
        [InlineData("Left Stick Y", true, true, "LS↑")]
        [InlineData("D-Pad Down", false, true, "↓")]
        [InlineData("Left Trigger", true, true, "LT")]
        [InlineData("Right Shoulder", false, true, "RB")]
        [InlineData("Start", false, true, "ST")]
        [InlineData("A", false, true, "A")]
        [InlineData("Button 12", false, true, "B12")]
        public void ElementLabel_ShortNamesForControllerParts(string element, bool axis, bool positive, string expected)
        {
            Assert.Equal(expected, PromptNames.ElementLabel(element, axis, positive));
        }

        [Theory]
        [InlineData("SpeedupIcons", "Speedup")]
        [InlineData("LeftBumperIconsDark", "LeftBumper")]
        [InlineData("AddOrbMapping", "AddOrb")]
        [InlineData("Icons", "Icons")]
        public void IconSetStem_DropsTheGamesSuffixes(string name, string expected)
        {
            Assert.Equal(expected, PromptNames.IconSetStem(name));
        }

        [Fact]
        public void IconSets_MapToTheActionsTheyShow()
        {
            Assert.True(PromptNames.TryGetIconSetAction("BackIcons", out string action, out bool positive));
            Assert.Equal("UICancel", action);
            Assert.True(positive);
            Assert.True(PromptNames.TryGetIconSetAction("DPadLeftButton", out action, out positive));
            Assert.Equal("UIHorizontal", action);
            Assert.False(positive);
            Assert.True(PromptNames.TryGetIconSetAction("TargetMoveLeftIconsDark", out action, out _));
            Assert.Equal("TargetLeft", action);
            Assert.True(PromptNames.TryGetIconSetAction("TargetMoveRightIconsDark", out action, out _));
            Assert.Equal("TargetRight", action);
            Assert.False(PromptNames.TryGetIconSetAction("SomethingNew", out _, out _));
        }
    }

    public class ModKeysTests
    {
        // The settings of the mods actually installed alongside this one.
        private static readonly ModKeySetting[] Installed =
        {
            new ModKeySetting("qr", "PeglinQuickRestart", "General", "RestartKey",
                "Hold this key to abandon the current run and start a new one. A run started with a custom seed restarts on that seed.", "X", "X"),
            new ModKeySetting("qr", "PeglinQuickRestart", "General", "ControllerRestartButton",
                "Controller button to hold for a restart. Set to None to disable.", "JoystickButton8", "JoystickButton8"),
            new ModKeySetting("rr", "PeglinRelicRerolls", "General", "RerollHotkey",
                "Keyboard shortcut for the reroll button on relic screens and in the shop. Key names: https://docs.unity3d.com/ScriptReference/KeyCode.html", "T", "T"),
            new ModKeySetting("rr", "PeglinRelicRerolls", "General", "RerollControllerButton",
                "Controller button for rerolling.", "None", "JoystickButton9"),
            new ModKeySetting("tf", "TestingFloor", "Key Bindings", "Toggle GUI Key", "Key to toggle the Testing Floor GUI visibility", "F11", "F11"),
            new ModKeySetting("tf", "TestingFloor", "Key Bindings", "Reload Scene Key", "Press while the GUI is open to reload the current scene", "F5", "F5"),
        };

        [Fact]
        public void BuildSections_PairsKeyboardWithControllerAndHidesDevTools()
        {
            var sections = ModKeys.BuildSections(Installed);

            Assert.Equal(new[] { "Peglin Quick Restart", "Peglin Relic Rerolls" }, sections.Select(s => s.plugin));
            ModKeyRow restart = Assert.Single(sections[0].rows);
            Assert.Equal("Restart", restart.Label);
            Assert.Equal("RestartKey", restart.Keyboard.Key);
            Assert.Equal("ControllerRestartButton", restart.Controller.Key);
            Assert.Equal("Reroll", Assert.Single(sections[1].rows).Label);
        }

        [Fact]
        public void BuildSections_PairsByActionNameWhenThereAreOtherKeys()
        {
            var settings = new[]
            {
                new ModKeySetting("f", "PeglinFonts", "General", "CycleFontKey", "", "F9", "F9"),
                new ModKeySetting("f", "PeglinFonts", "General", "CycleWeightKey", "", "F8", "F8"),
                new ModKeySetting("f", "PeglinFonts", "General", "ControllerCycleFontButton", "", "None", "JoystickButton8"),
            };

            ModKeyRow[] rows = ModKeys.BuildSections(settings).Single().rows.ToArray();

            Assert.Equal(new[] { "Cycle Font", "Cycle Weight" }, rows.Select(r => r.Label));
            Assert.Equal("CycleFontKey", rows[0].Keyboard.Key);
            Assert.Equal("ControllerCycleFontButton", rows[0].Controller.Key);
            Assert.Equal("CycleWeightKey", rows[1].Keyboard.Key);
        }

        [Fact]
        public void BuildSections_UnpairedControllerSaysController()
        {
            var settings = new[]
            {
                new ModKeySetting("m", "Mod", "S", "OpenMapKey", "", "M", "M"),
                new ModKeySetting("m", "Mod", "S", "PadZoomButton", "", "JoystickButton2", "JoystickButton2"),
            };

            ModKeyRow[] rows = ModKeys.BuildSections(settings).Single().rows.ToArray();

            Assert.Equal(new[] { "Open Map", "Zoom (Controller)" }, rows.Select(r => r.Label));
            Assert.Null(rows[1].Keyboard);
            Assert.NotNull(rows[1].Controller);
        }

        [Theory]
        [InlineData("DebugMenuKey", "", false)]
        [InlineData("OpenKey", "Opens the developer console", false)]
        [InlineData("Toggle", "Toggles the shop", true)]
        [InlineData("DeviceKey", "Picks an input device", true)]
        public void IsDevSetting_ByWords(string key, string description, bool shown)
        {
            Assert.Equal(!shown, ModKeys.IsDevSetting(new ModKeySetting("m", "SomeMod", "General", key, description, "K", "K")));
        }

        [Theory]
        [InlineData("RestartKey", "Restart")]
        [InlineData("ControllerRestartButton", "Restart")]
        [InlineData("RerollControllerButton", "Reroll")]
        [InlineData("Toggle GUI Key", "Toggle GUI")]
        [InlineData("Key", "Key")]
        public void Label_DropsKeyWords(string setting, string expected)
        {
            Assert.Equal(expected, ModKeys.Label(setting));
        }

        [Fact]
        public void Summary_TakesTheFirstSentenceWithoutLinks()
        {
            Assert.Equal("Keyboard shortcut for the reroll button on relic screens and in the shop.", Installed[2].Description.Length > 0 ? ModKeys.Summary(Installed[2].Description) : null);
            Assert.Equal("Key to toggle the GUI", ModKeys.Summary("Key to toggle the GUI"));
            Assert.Equal("", ModKeys.Summary(null));
        }

        [Theory]
        [InlineData("JoystickButton0", "A")]
        [InlineData("JoystickButton8", "L3")]
        [InlineData("Joystick2Button9", "R3")]
        [InlineData("JoystickButton14", "B14")]
        [InlineData("X", null)]
        public void JoystickButtonLabel_UsesTheXboxLayout(string key, string expected)
        {
            Assert.Equal(expected, ModKeys.JoystickButtonLabel(key));
        }

        [Theory]
        [InlineData("JoystickButton0", true, 0)]
        [InlineData("JoystickButton8", true, 8)]
        [InlineData("Joystick1Button5", true, 5)]
        [InlineData("JoystickButton", false, -1)]
        [InlineData("Escape", false, -1)]
        [InlineData(null, false, -1)]
        public void JoystickButtonNames_AreRecognisedAndNumbered(string name, bool isJoystick, int number)
        {
            // Another mod's controller setting is a Unity joystick key; it must be spotted and named like the game's.
            Assert.Equal(isJoystick, ModKeys.IsJoystickKey(name));
            Assert.Equal(number, ModKeys.JoystickButtonNumber(name));
        }
    }

    public class ClashIndexTests
    {
        [Fact]
        public void ModAgainstGame_Clashes_GameAgainstGameOnlyWhenNotSharedByDefault()
        {
            var index = new ClashIndex();
            // The game's defaults put Enter on both Select and Fire, and Esc only on Pause.
            index.AddDefault(ClashIndex.KeyboardId("Return"), "Select");
            index.AddDefault(ClashIndex.KeyboardId("Return"), "Fire / Confirm");
            index.AddDefault(ClashIndex.KeyboardId("Escape"), "Pause");
            index.Add(ClashIndex.KeyboardId("Escape"), "Pause", false);
            index.Add(ClashIndex.KeyboardId("Escape"), "Discard Orb", false);
            index.Add(ClashIndex.KeyboardId("Return"), "Select", false);
            index.Add(ClashIndex.KeyboardId("Return"), "Fire / Confirm", false);
            index.Add(ClashIndex.KeyboardId("M"), "Map", false);
            index.Add(ClashIndex.KeyboardId("M"), "Some Mod: Mute", true);

            Assert.Empty(index.ClashesFor(ClashIndex.KeyboardId("Return"), "Select", false));
            Assert.Equal(new[] { "Some Mod: Mute" }, index.ClashesFor(ClashIndex.KeyboardId("M"), "Map", false));
            Assert.Equal(new[] { "Map" }, index.ClashesFor(ClashIndex.KeyboardId("M"), "Some Mod: Mute", true));
            Assert.Equal(new[] { "Pause" }, index.ClashesFor(ClashIndex.KeyboardId("Escape"), "Discard Orb", false));
            Assert.Equal(new[] { "Discard Orb" }, index.ClashesFor(ClashIndex.KeyboardId("Escape"), "Pause", false));
        }

        [Fact]
        public void Modifiers_MustMatch_LeftAndRightCountTheSame()
        {
            Assert.Equal(ClashIndex.KeyboardId("V", new[] { "LeftControl", "RightControl" }), ClashIndex.KeyboardId("V", new[] { "LeftControl" }));
            Assert.NotEqual(ClashIndex.KeyboardId("V", new[] { "LeftControl" }), ClashIndex.KeyboardId("V"));
        }

        [Fact]
        public void UnboundKeysNeverClash()
        {
            var index = new ClashIndex();
            index.Add(ClashIndex.KeyboardId("None"), "A", true);
            index.Add(ClashIndex.KeyboardId("None"), "B", true);

            Assert.Empty(index.ClashesFor(ClashIndex.KeyboardId("None"), "A", true));
        }

        [Fact]
        public void PadButtons_ClashByLabel()
        {
            var index = new ClashIndex();
            index.Add(ClashIndex.PadId("L3"), "Quick Restart: Restart", true);
            index.Add(ClashIndex.PadId("L3"), "Speed Up", false);

            Assert.Equal(new[] { "Speed Up" }, index.ClashesFor(ClashIndex.PadId("L3"), "Quick Restart: Restart", true));
        }
    }

    public class PixelArtTests
    {
        [Fact]
        public void Keycap_MatchesTheGamesKeyShape()
        {
            // The game's "esc" key is 17x11: two-pixel border each side, a pixel of face, then 11 pixels of letters.
            PixelGrid grid = PixelArt.Keycap("ESC", pressed: false);

            Assert.Equal(17, grid.Width);
            Assert.Equal(11, grid.Height);
            string[] rows = grid.ToRows();
            Assert.Equal(" 111111111111111 ", rows[0]);
            Assert.Equal("11" + new string('3', 13) + "11", rows[1]);
            Assert.StartsWith("123555", rows[2]);
            Assert.Equal("12" + new string('4', 13) + "21", rows[8]);
            Assert.Equal(new string('1', 17), rows[10]);
        }

        [Fact]
        public void Keycap_SingleLetterIsAtLeastTheGamesSquareKeySize()
        {
            PixelGrid grid = PixelArt.Keycap("k", pressed: false);

            Assert.Equal(11, grid.Width);
            Assert.Contains(grid.ToRows().Skip(2).Take(5), r => r.Contains('5'));
        }

        [Fact]
        public void Keycap_PressedSitsTwoPixelsLower()
        {
            string[] up = PixelArt.Keycap("K", pressed: false).ToRows();
            string[] down = PixelArt.Keycap("K", pressed: true).ToRows();

            Assert.Equal(new string(' ', 11), down[0]);
            Assert.Equal(new string(' ', 11), down[1]);
            Assert.Equal(up[0], down[2]);
            Assert.Equal(up[2], down[4]);
            Assert.Equal(new string('1', 11), down[10]);
        }

        [Fact]
        public void PadButton_WidensForLongerLabels()
        {
            PixelGrid single = PixelArt.PadButton("A");
            PixelGrid wide = PixelArt.PadButton("LS↑");

            Assert.Equal(11, single.Width);
            Assert.Equal(12, single.Height);
            Assert.Equal(11 + PixelArt.TextWidth("LS↑") - PixelArt.GlyphWidth, wide.Width);
            // Outline is symmetric after widening.
            foreach (string row in wide.ToRows())
            {
                string outline = new string(row.Select(c => c == '1' ? '1' : '.').ToArray());
                Assert.Equal(outline, new string(outline.Reverse().ToArray()));
            }
        }

        [Fact]
        public void Normalize_UppercasesAndReplacesUnknownCharacters()
        {
            Assert.Equal("AB?", PixelArt.Normalize("ab~"));
            Assert.Equal(0, PixelArt.TextWidth(""));
            Assert.Equal(7, PixelArt.TextWidth("LS"));
        }
    }

    /// <summary>
    /// The direction rules behind binding a controller stick. Polling the pad and writing the Rewired map need the
    /// running game and a controller; these are the decisions around those calls, which a pad test would cover.
    /// </summary>
    public class AxisDirectionTests
    {
        [Fact]
        public void WholeAxis_NotInverted_PositiveDirectionIsPushedPositive()
        {
            Assert.True(AxisDirections.PushedPositive(true, false, false, true));
            Assert.False(AxisDirections.PushedPositive(true, false, false, false));
        }

        [Fact]
        public void WholeAxis_Inverted_SwapsWhichHalfDrivesEachDirection()
        {
            Assert.False(AxisDirections.PushedPositive(true, false, true, true));
            Assert.True(AxisDirections.PushedPositive(true, false, true, false));
        }

        [Fact]
        public void HalfAxis_UsesItsOwnHalf_WhateverTheDirection()
        {
            Assert.True(AxisDirections.PushedPositive(false, true, false, true));
            Assert.True(AxisDirections.PushedPositive(false, true, true, false));
            Assert.False(AxisDirections.PushedPositive(false, false, false, false));
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        public void OtherHalf_IsTheOppositeDirection_AndInversionSwapsTheHalves(bool rowPositive, bool inverted, bool otherHalfPositive)
        {
            // Clearing one direction of a whole-axis binding leaves the other one on this half.
            Assert.Equal(otherHalfPositive, AxisDirections.OtherHalfPositive(rowPositive, inverted));
        }

        [Fact]
        public void SamePush_WholeAxisAlreadyCoversBothDirections()
        {
            Assert.True(AxisDirections.SamePush(true, false, true));
            Assert.True(AxisDirections.SamePush(true, false, false));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void SamePush_APushOnTheBoundHalfIsTheSame(bool positiveHalf, bool positivePush)
        {
            Assert.True(AxisDirections.SamePush(false, positiveHalf, positivePush));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void SamePush_TheOtherHalfIsStillFree(bool positiveHalf, bool positivePush)
        {
            Assert.False(AxisDirections.SamePush(false, positiveHalf, positivePush));
        }
    }
}
