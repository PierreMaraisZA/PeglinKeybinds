using System;
using System.Collections.Generic;
using System.Linq;
using PeglinKeybinds.Core;
using Rewired;
using UnityEngine;

namespace PeglinKeybinds
{
    /// <summary>
    /// Reads and changes the player's Rewired bindings for one menu row on one controller. An axis action (e.g.
    /// Horizontal) has a row per direction; a controller stick bound to the whole axis shows in both rows, and
    /// changing one direction keeps the other direction on the stick.
    /// </summary>
    internal static class RewiredBindings
    {
        public static List<BindingRow> BuildRows(Player player)
        {
            // The game has Mac-only copies of a few actions (Cmd instead of Ctrl); only show the ones for this platform.
            bool mac = Application.platform == RuntimePlatform.OSXPlayer;
            // The game's data gives two actions the same id (CustomStartOption and an unused MouseWheel axis); bindings
            // are stored by id, so only one can be shown; prefer the one the game actually uses.
            var actions = ReInput.mapping.Actions
                .Where(a => a.userAssignable && (mac || !a.name.EndsWith("OSX")))
                .GroupBy(a => a.id)
                .Select(g => g.OrderBy(a => ActionNames.Order(a.name)).First())
                .ToList();
            var rows = new List<(int, ActionInfo)>();
            foreach (InputMapCategory category in ReInput.mapping.MapCategories)
            {
                if (!category.userAssignable)
                {
                    continue;
                }
                // Show the actions the keyboard or a controller has bindings for in this category, by default or now.
                var used = new HashSet<int>();
                foreach (ControllerMap map in MapsInCategory(player, category.id))
                {
                    foreach (ActionElementMap aem in map.AllMaps)
                    {
                        used.Add(aem.actionId);
                    }
                }
                rows.AddRange(actions.Where(a => used.Contains(a.id)).Select(a => (category.id, Info(a))));
            }
            return BindingRows.Build(rows);
        }

        /// <summary>
        /// Whether a binding is for another platform: the game's keyboard map has Cmd+key copies of its Ctrl+key
        /// bindings for Macs, which would only clutter the list elsewhere.
        /// </summary>
        public static bool IsOtherPlatform(ActionElementMap aem)
        {
            bool command = (aem.modifierKeyFlags & (ModifierKeyFlags.LeftCommand | ModifierKeyFlags.RightCommand)) != 0;
            return command && Application.platform != RuntimePlatform.OSXPlayer;
        }

        public static ActionInfo Info(InputAction action)
        {
            return new ActionInfo(action.id, action.name, action.descriptiveName, action.type == InputActionType.Axis,
                action.positiveDescriptiveName, action.negativeDescriptiveName);
        }

        private static IEnumerable<ControllerMap> MapsInCategory(Player player, int categoryId)
        {
            foreach (ControllerMap map in player.controllers.maps.GetAllMaps(ControllerType.Keyboard))
            {
                if (map.categoryId == categoryId)
                {
                    yield return map;
                }
            }
            foreach (ControllerMap map in player.controllers.maps.GetAllMaps(ControllerType.Joystick))
            {
                if (map.categoryId == categoryId)
                {
                    yield return map;
                }
            }
            // The game's default keyboard map too, so an action the player unbound still has a row to rebind it.
            foreach (InputLayout layout in ReInput.mapping.MapLayouts(ControllerType.Keyboard))
            {
                ControllerMap defaults = ReInput.mapping.GetKeyboardMapInstance(categoryId, layout.id);
                if (defaults != null)
                {
                    yield return defaults;
                }
            }
            // And the default controller map, so actions only bound on a pad still get a row with no pad plugged in.
            ControllerMap padDefaults = DefaultControllerMap(categoryId).map;
            if (padDefaults != null)
            {
                yield return padDefaults;
            }
        }

        // Rewired's hardware GUIDs for the usual Windows pads: XInput (Xbox 360 / One / Series and most pads through
        // Steam Input), then the Xbox One controller's own profile.
        private static readonly Guid[] CommonControllerTypes =
        {
            new Guid("d74a350e-fe8b-4e9e-bbcd-efff16d34115"),
            new Guid("19002688-7406-4f4a-8340-8d25335406c8"),
        };

        private static readonly Dictionary<int, (Guid type, ControllerMap map)> DefaultControllerMaps = new Dictionary<int, (Guid, ControllerMap)>();

        /// <summary>Forget the cached default controller maps, e.g. after bindings were saved for a new pad model.</summary>
        public static void ForgetDefaultControllerMaps() => DefaultControllerMaps.Clear();

        /// <summary>
        /// The controller layout to read defaults from: the one a connected pad's map uses, else the game's first.
        /// The game has more than one controller layout, and they put actions on different buttons.
        /// </summary>
        public static int ControllerLayout(Player player, Joystick joystick, int categoryId)
        {
            if (player != null && joystick != null)
            {
                ControllerMap map = player.controllers.maps.GetFirstMapInCategory(ControllerType.Joystick, joystick.id, categoryId);
                if (map != null)
                {
                    return map.layoutId;
                }
            }
            InputLayout first = ReInput.mapping.MapLayouts(ControllerType.Joystick).FirstOrDefault();
            return first != null ? first.id : 0;
        }

        /// <summary>
        /// The game's default controller map for a category when no controller is plugged in, so the screen can still
        /// show what the buttons are. Uses the type of a controller the player has saved bindings for, else a standard
        /// Xbox-layout pad. Null if Rewired has no defaults for any of those.
        /// </summary>
        public static (Guid type, ControllerMap map) DefaultControllerMap(int categoryId)
        {
            if (DefaultControllerMaps.TryGetValue(categoryId, out var cached))
            {
                return cached;
            }
            (Guid, ControllerMap) found = (Guid.Empty, null);
            foreach (Guid type in Plugin.Store.SavedControllerTypes().Concat(CommonControllerTypes).Distinct())
            {
                ControllerMap map = ReInput.mapping.GetJoystickMapInstance(type, categoryId, ControllerLayout(null, null, categoryId));
                if (map != null && map.AllMaps.Any())
                {
                    found = (type, map);
                }
                if (found.Item2 != null)
                {
                    break;
                }
            }
            DefaultControllerMaps[categoryId] = found;
            return found;
        }

        /// <summary>The map a row's bindings live in on this controller, or null if the game gives it none in that category.</summary>
        public static ControllerMap MapFor(Player player, Controller controller, BindingRow row)
        {
            if (controller == null || row.IsHeader)
            {
                return null;
            }
            return player.controllers.maps.GetFirstMapInCategory(controller.type, controller.id, row.CategoryId);
        }

        public static List<ActionElementMap> BindingsFor(ControllerMap map, BindingRow row)
        {
            return row.IsHeader ? new List<ActionElementMap>() : BindingsFor(map, row.ActionId, row.IsAxis, row.Positive);
        }

        /// <summary>The bindings in a map that drive one action, or for an axis action, one direction of it.</summary>
        public static List<ActionElementMap> BindingsFor(ControllerMap map, int actionId, bool isAxis, bool positive)
        {
            var result = new List<ActionElementMap>();
            if (map == null)
            {
                return result;
            }
            foreach (ActionElementMap aem in map.ElementMapsWithAction(actionId))
            {
                if (IsOtherPlatform(aem))
                {
                    continue;
                }
                if (!isAxis || IsFullAxis(aem) || (aem.axisContribution == Pole.Positive) == positive)
                {
                    result.Add(aem);
                }
            }
            return result;
        }

        /// <summary>Every binding the player has for an action (or one direction of it) on a controller, across all its maps.</summary>
        public static List<ActionElementMap> BindingsFor(Player player, Controller controller, int actionId, bool isAxis, bool positive)
        {
            var result = new List<ActionElementMap>();
            if (controller == null)
            {
                return result;
            }
            foreach (ControllerMap map in player.controllers.maps.GetMaps(controller.type, controller.id))
            {
                // Only the categories this screen edits: a binding left in some other category isn't one the player chose.
                InputMapCategory category = ReInput.mapping.GetMapCategory(map.categoryId);
                if (category == null || !category.userAssignable)
                {
                    continue;
                }
                result.AddRange(BindingsFor(map, actionId, isAxis, positive));
            }
            return result;
        }

        /// <summary>The binding's name as Rewired gives it, which already includes any modifier ("Ctrl + Left Arrow").</summary>
        public static string Describe(ActionElementMap aem)
        {
            string name = aem.elementIdentifierName;
            if (string.IsNullOrEmpty(name))
            {
                name = aem.keyCode != KeyCode.None ? aem.keyCode.ToString() : "?";
            }
            return name;
        }

        /// <summary>
        /// Binds the polled key, button or axis to the row, replacing what the row had. Other actions on the same key
        /// are left alone: the game deliberately shares keys between menu and gameplay actions (Enter is both
        /// UI Submit and Confirm).
        /// </summary>
        public static void Assign(ControllerMap map, BindingRow row, ControllerPollingInfo polled, bool add = false)
        {
            Pole pole = row.Positive ? Pole.Positive : Pole.Negative;
            ElementAssignment assignment;
            if (polled.controllerType == ControllerType.Keyboard)
            {
                assignment = ElementAssignment.KeyboardKeyAssignment(polled.keyboardKey, ModifierKeyFlags.None, row.ActionId, pole);
            }
            else if (polled.elementType == ControllerElementType.Axis)
            {
                // Only the half of the axis that was pushed, so a stick can drive one direction or one button action.
                AxisRange half = polled.axisPole == Pole.Positive ? AxisRange.Positive : AxisRange.Negative;
                assignment = ElementAssignment.SplitAxisAssignment(polled.elementIdentifierId, half, row.ActionId, pole);
            }
            else
            {
                assignment = ElementAssignment.ButtonAssignment(polled.elementIdentifierId, row.ActionId, pole);
            }

            if (add)
            {
                // Adding a key the row already has would just duplicate it.
                if (AlreadyHas(BindingsFor(map, row), polled))
                {
                    return;
                }
            }
            else
            {
                Clear(map, row);
            }
            if (!map.CreateElementMap(assignment))
            {
                Plugin.Log.LogWarning($"Rewired refused to bind {polled.elementIdentifierName} to action {row.ActionId}.");
                return;
            }
            Plugin.Log.LogInfo($"{(add ? "Added" : "Bound")} {polled.elementIdentifierName} for {row.Label}.");
        }

        /// <summary>Whether the row already has the key, button or half-axis this press would add.</summary>
        private static bool AlreadyHas(List<ActionElementMap> bindings, ControllerPollingInfo polled)
        {
            return bindings.Any(b => polled.controllerType == ControllerType.Keyboard
                ? b.keyCode == polled.keyboardKey && b.modifierKeyFlags == ModifierKeyFlags.None
                : b.elementIdentifierId == polled.elementIdentifierId
                    && (polled.elementType != ControllerElementType.Axis
                        || AxisDirections.SamePush(IsFullAxis(b), b.axisRange == AxisRange.Positive, polled.axisPole == Pole.Positive)));
        }

        /// <summary>
        /// Puts back the game's default bindings for one row on one controller. A default stick bound to the whole
        /// axis comes back as just this row's direction, so the other row is left as the player set it.
        /// </summary>
        public static void RestoreDefault(Player player, Controller controller, ControllerMap map, BindingRow row)
        {
            ControllerMap defaults = controller is Joystick joystick
                ? ReInput.mapping.GetJoystickMapInstance(joystick, map.categoryId, map.layoutId)
                : ReInput.mapping.GetKeyboardMapInstance(map.categoryId, map.layoutId);
            Clear(map, row);
            if (defaults == null)
            {
                return;
            }
            Pole pole = row.Positive ? Pole.Positive : Pole.Negative;
            foreach (ActionElementMap aem in BindingsFor(defaults, row))
            {
                ElementAssignment assignment = IsFullAxis(aem)
                    ? ElementAssignment.SplitAxisAssignment(aem.elementIdentifierId,
                        PushedPositive(aem, row.Positive) ? AxisRange.Positive : AxisRange.Negative, row.ActionId, pole)
                    : ElementAssignment.CompleteAssignment(map.controllerType, aem.elementType, aem.elementIdentifierId, aem.axisRange,
                        aem.keyCode, aem.modifierKeyFlags, aem.actionId, aem.axisContribution, aem.invert);
                map.CreateElementMap(assignment);
            }
        }

        /// <summary>Unbinds the row. A stick bound to the whole axis keeps driving the row's other direction.</summary>
        public static void Clear(ControllerMap map, BindingRow row)
        {
            foreach (ActionElementMap aem in BindingsFor(map, row))
            {
                // Only an axis action has an "other direction" to keep; a button action on a whole axis just goes.
                if (row.IsAxis && IsFullAxis(aem))
                {
                    // Positive input on an un-inverted full axis drives the positive direction; inverting swaps them.
                    bool otherSideIsPositiveInput = AxisDirections.OtherHalfPositive(row.Positive, aem.invert);
                    int element = aem.elementIdentifierId;
                    map.DeleteElementMap(aem.id);
                    map.CreateElementMap(ElementAssignment.SplitAxisAssignment(element,
                        otherSideIsPositiveInput ? AxisRange.Positive : AxisRange.Negative, row.ActionId,
                        row.Positive ? Pole.Negative : Pole.Positive));
                }
                else
                {
                    map.DeleteElementMap(aem.id);
                }
            }
        }

        public static bool IsFullAxis(ActionElementMap aem)
        {
            return aem.elementType == ControllerElementType.Axis && aem.axisRange == AxisRange.Full;
        }

        /// <summary>For a binding on a stick, which way the stick is pushed to trigger it.</summary>
        public static bool PushedPositive(ActionElementMap aem, bool rowPositive)
        {
            return AxisDirections.PushedPositive(IsFullAxis(aem), aem.axisRange == AxisRange.Positive, aem.invert, rowPositive);
        }
    }
}
