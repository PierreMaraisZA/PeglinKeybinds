using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PeglinKeybinds.Core;
using PeglinUI.ControllerSupport;
using Rewired;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace PeglinKeybinds
{
    /// <summary>
    /// Makes the game's on-screen button prompts show the key or button the player actually has bound. Each prompt
    /// icon set in the game stands for one action; when that action is bound to something else, the prompt borrows
    /// the icon set the game uses for the new key or button, or an icon drawn in the same style if the game has none.
    /// </summary>
    internal static class PromptIcons
    {
        private static readonly AccessTools.FieldRef<ButtonGraphicMapper, ControllerIconMapping> MapperIconSet =
            AccessTools.FieldRefAccess<ButtonGraphicMapper, ControllerIconMapping>("_controllerIconMapping");

        private static readonly AccessTools.FieldRef<ButtonGraphicMapper, Image> MapperImage =
            AccessTools.FieldRefAccess<ButtonGraphicMapper, Image>("_image");

        private static readonly AccessTools.FieldRef<ButtonGraphicMapper, Sprite> MapperPrimary =
            AccessTools.FieldRefAccess<ButtonGraphicMapper, Sprite>("_primarySprite");

        private static readonly Action<ButtonGraphicMapper> UpdateInputType =
            AccessTools.MethodDelegate<Action<ButtonGraphicMapper>>(AccessTools.Method(typeof(ButtonGraphicMapper), "UpdateInputType"));

        private static readonly Action<ButtonGraphicMapper> SetButtonsForController =
            AccessTools.MethodDelegate<Action<ButtonGraphicMapper>>(AccessTools.Method(typeof(ButtonGraphicMapper), "SetButtonsForController"));

        private static readonly string[] IconSetFields =
        {
            "_keyboardIcon", "_keyboardIconPressed", "_xboxIcon", "_xboxIconPressed", "_psIcon", "_psIconPressed", "_switchIcon", "_switchIconPressed",
        };

        // The game's palette for prompt icons, sampled from its key cap and face button sprites.
        private static readonly Color32 Outline = new Color32(0x13, 0x13, 0x13, 0xFF);
        private static readonly Color32 Side = new Color32(0x1A, 0x19, 0x32, 0xFF);
        private static readonly Color32 SidePressed = new Color32(0x0E, 0x07, 0x1B, 0xFF);
        private static readonly Color32 Face = new Color32(0x42, 0x4C, 0x6E, 0xFF);
        private static readonly Color32 FacePressed = new Color32(0x2A, 0x2F, 0x4E, 0xFF);
        private static readonly Color32 Bottom = new Color32(0x2A, 0x2F, 0x4E, 0xFF);
        private static readonly Color32 Glyph = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Color32 GlyphPressed = new Color32(0xC7, 0xCF, 0xDD, 0xFF);

        private static readonly Dictionary<string, Sprite> GeneratedSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, ControllerIconMapping> GeneratedSets = new Dictionary<string, ControllerIconMapping>();
        private static readonly Dictionary<string, Dictionary<int, ControllerIconMapping>> ElementSetsByController = new Dictionary<string, Dictionary<int, ControllerIconMapping>>();
        private static ControllerIconMapping[] _knownSets = new ControllerIconMapping[0];
        private static ButtonGraphicMapper _platformProbe;

        /// <summary>Redraws every prompt on screen, e.g. after a binding changed.</summary>
        public static void RefreshAll()
        {
            foreach (ButtonGraphicMapper mapper in Resources.FindObjectsOfTypeAll<ButtonGraphicMapper>())
            {
                if (mapper != null && mapper.isActiveAndEnabled && MapperIconSet(mapper) != null)
                {
                    try
                    {
                        UpdateInputType(mapper);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogDebug($"Couldn't refresh prompt {mapper.name}: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// The icon set a prompt should use instead of its own, or null to leave it alone (the action still has the
        /// prompt's own key or button, or isn't one we can identify).
        /// </summary>
        public static ControllerIconMapping ReplacementFor(ButtonGraphicMapper mapper, ControllerIconMapping own)
        {
            Player player = BindingLoader.GamePlayer;
            if (player == null || own == null || !ActionFor(mapper, own, out InputAction action, out bool positive))
            {
                return null;
            }
            bool isAxis = action.type == InputActionType.Axis;
            switch (ControllerInputManager.primaryInputType)
            {
                case InputType.Keyboard:
                case InputType.Mouse:
                {
                    List<ActionElementMap> keys = RewiredBindings.BindingsFor(player, ReInput.controllers.Keyboard, action.id, isAxis, positive);
                    if (keys.Count == 0)
                    {
                        // Unbound: show no key rather than one that does nothing.
                        return GeneratedSet(own, keyboard: true, null);
                    }
                    string ownToken = PromptNames.KeyTokenFromSpriteName(own.keyboardIcon != null ? own.keyboardIcon.name : null);
                    if (keys.Any(k => PromptNames.KeyToken(k.keyCode.ToString()) == ownToken))
                    {
                        return null;
                    }
                    // Prefer a key the game has art for, then draw the first one.
                    foreach (ActionElementMap key in keys)
                    {
                        ControllerIconMapping native = KeyboardSetFor(key.keyCode);
                        if (native != null)
                        {
                            return native;
                        }
                    }
                    return GeneratedSet(own, keyboard: true, PromptNames.KeyLabel(keys[0].keyCode.ToString()));
                }
                case InputType.Controller:
                {
                    Joystick joystick = player.controllers.Joysticks.FirstOrDefault();
                    List<ActionElementMap> buttons = RewiredBindings.BindingsFor(player, joystick, action.id, isAxis, positive);
                    if (joystick == null)
                    {
                        return null;
                    }
                    if (buttons.Count == 0)
                    {
                        return GeneratedSet(own, keyboard: false, null);
                    }
                    Dictionary<int, ControllerIconMapping> elementSets = ElementSets(player, joystick);
                    if (buttons.Any(b => elementSets.TryGetValue(b.elementIdentifierId, out ControllerIconMapping set) && SameButton(set, own)))
                    {
                        return null;
                    }
                    foreach (ActionElementMap button in buttons)
                    {
                        if (button.elementType == ControllerElementType.Button && elementSets.TryGetValue(button.elementIdentifierId, out ControllerIconMapping native))
                        {
                            return native;
                        }
                    }
                    ActionElementMap first = buttons[0];
                    return GeneratedSet(own, keyboard: false, ElementLabel(first, positive));
                }
            }
            return null;
        }

        /// <summary>The icon for one binding, for showing in the controls screen: the game's own art where it has some.</summary>
        public static Sprite IconFor(Player player, ActionElementMap aem, bool rowPositive, Guid? controllerType = null)
        {
            ControllerMap map = aem.controllerMap;
            if (map == null)
            {
                return null;
            }
            if (map.controllerType == ControllerType.Keyboard)
            {
                ControllerIconMapping native = KeyboardSetFor(aem.keyCode);
                return native != null ? native.keyboardIcon : KeyboardSprite(PromptNames.KeyLabel(aem.keyCode.ToString()), pressed: false);
            }
            Joystick joystick = map.controller as Joystick;
            if (map.controllerType == ControllerType.Joystick && (joystick != null || controllerType.HasValue))
            {
                var sets = joystick != null ? ElementSets(player, joystick) : ElementSets(controllerType.Value, null);
                if (aem.elementType == ControllerElementType.Button && sets.TryGetValue(aem.elementIdentifierId, out ControllerIconMapping native))
                {
                    Sprite sprite = PlatformSprite(native);
                    if (sprite != null)
                    {
                        return sprite;
                    }
                }
                return PadSprite(ElementLabel(aem, rowPositive), pressed: false);
            }
            return null;
        }

        private static string ElementLabel(ActionElementMap aem, bool rowPositive)
        {
            bool isAxis = aem.elementType == ControllerElementType.Axis;
            return PromptNames.ElementLabel(aem.elementIdentifierName, isAxis, isAxis && RewiredBindings.PushedPositive(aem, rowPositive));
        }

        /// <summary>
        /// Which action a prompt stands for. A prompt on (or just inside) a button that the game presses with an
        /// action says so directly; otherwise go by the icon set.
        /// </summary>
        private static bool ActionFor(ButtonGraphicMapper mapper, ControllerIconMapping own, out InputAction action, out bool positive)
        {
            positive = true;
            // Prompts the game's own scripts point at (inspect prompts, carousel arrows, ...): the most reliable answer.
            if (OwnedPromptAction(mapper.transform, out action, out positive))
            {
                return true;
            }
            Transform t = mapper.transform;
            for (int depth = 0; depth < 4 && t != null; depth++, t = t.parent)
            {
                // Buttons the game wires to an action by name (MapButtonToInput / MapToggleToInput), set in the scene.
                if (depth < 2 && WiredAction(t, out action, out positive))
                {
                    return true;
                }
                // Battle buttons that read their action in code, so it can only be told from the script they sit under.
                foreach (MonoBehaviour script in t.GetComponents<MonoBehaviour>())
                {
                    string known = script != null ? PromptNames.ActionForScript(script.GetType().Name) : null;
                    if (known != null && (action = ReInput.mapping.GetAction(known)) != null)
                    {
                        return true;
                    }
                }
                if (depth >= 2)
                {
                    continue;
                }
                var trigger = t.GetComponent<PixelCrushers.UIButtonKeyTrigger>();
                if (trigger != null && !string.IsNullOrEmpty(trigger.buttonName))
                {
                    action = ReInput.mapping.GetAction(trigger.buttonName);
                    if (action != null)
                    {
                        return true;
                    }
                }
            }
            if (PromptNames.TryGetIconSetAction(own.name, out string name, out positive))
            {
                action = ReInput.mapping.GetAction(name);
                return action != null;
            }
            action = GuessAction(own);
            return action != null;
        }

        private static bool WiredAction(Transform t, out InputAction action, out bool positive)
        {
            action = null;
            positive = true;
            Component wiring = (Component)t.GetComponent<MapButtonToInput>() ?? t.GetComponent<MapToggleToInput>();
            if (wiring == null)
            {
                return false;
            }
            var traverse = Traverse.Create(wiring);
            string name = traverse.Field("_rewiredAction").GetValue<string>();
            positive = !traverse.Field("_isAxisNegative").GetValue<bool>();
            action = string.IsNullOrEmpty(name) ? null : ReInput.mapping.GetAction(name);
            return action != null;
        }

        private static Dictionary<GameObject, Func<(string action, bool positive)>> _ownedPrompts = new Dictionary<GameObject, Func<(string, bool)>>();
        private static float _ownedPromptsBuiltAt = -100f;

        /// <summary>
        /// The game's scripts that own a prompt object and read its action in code. The prompt often isn't inside the
        /// script's own object (inspect prompts are found through managers), so these are matched by reference.
        /// </summary>
        private static bool OwnedPromptAction(Transform prompt, out InputAction action, out bool positive)
        {
            action = null;
            positive = true;
            // Scenes add and remove these scripts; rebuilding every couple of seconds keeps up without scanning per prompt.
            if (Time.realtimeSinceStartup - _ownedPromptsBuiltAt > 2f)
            {
                _ownedPrompts = BuildOwnedPrompts();
                _ownedPromptsBuiltAt = Time.realtimeSinceStartup;
            }
            for (Transform t = prompt; t != null && t != prompt.parent?.parent?.parent; t = t.parent)
            {
                if (_ownedPrompts.TryGetValue(t.gameObject, out var resolve))
                {
                    var (name, pos) = resolve();
                    action = string.IsNullOrEmpty(name) ? null : ReInput.mapping.GetAction(name);
                    positive = pos;
                    return action != null;
                }
            }
            return false;
        }

        private static IEnumerable<T> Live<T>() where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().Where(c => c != null && c.gameObject.scene.isLoaded);

        private static Dictionary<GameObject, Func<(string, bool)>> BuildOwnedPrompts()
        {
            var map = new Dictionary<GameObject, Func<(string, bool)>>();
            void Add(UnityEngine.Object target, Func<(string, bool)> resolve)
            {
                GameObject go = target is GameObject g ? g : (target as Component)?.gameObject;
                if (go != null)
                {
                    map[go] = resolve;
                }
            }
            void Fixed(UnityEngine.Object target, string action) => Add(target, () => (action, true));

            try
            {
                // The controller "inspect" prompts all step through ControllerInspection (ControllerInspection.cs:350-378).
                foreach (var c in Live<DeckInfoManager>()) Fixed(c.orbInspectionButtonPrompt, "RelicAndOrbInfo");
                foreach (var c in Live<Battle.StatusEffects.PlayerStatusEffectController>()) Fixed(c.statusEffectInspectionPrompt, "RelicAndOrbInfo");
                foreach (var c in Live<Challenges.ChallengeUI>()) Fixed(c.inspectionPrompt, "RelicAndOrbInfo");
                foreach (var c in Live<ControllerInspection>()) Fixed(c._relicButtonPrompt, "RelicAndOrbInfo");
                foreach (var c in Live<Peglin.PegMinigame.PegMinigameManager>()) Fixed(c.rewardInspectionButtonPrompt, "RelicAndOrbInfo");
                foreach (var c in Live<PeglinUI.LoadoutManager.LoadoutEditorRelicItem>()) Fixed(c.deleteContextButton, "CatalogDeleteOrb");
                // Carousels page with their own action names: the up arrow pages back, the down arrow forward.
                foreach (var c in Live<PeglinUI.LoadoutManager.ImageCarousel>())
                {
                    var carousel = c;
                    Add(c.controllerButtonImageUp, () => (carousel.catalogPageLeftAction, true));
                    Add(c.controllerButtonImageDown, () => (carousel.catalogPageRightAction, true));
                }
                foreach (var c in Live<PeglinUI.Encirclepedia.OverallProgressManager>())
                {
                    Fixed(c.leftTabButtonGraphic, "CatalogPageLeft");
                    Fixed(c.rightTabButtonGraphic, "CatalogPageRight");
                }
                // The run history swaps its button's action (and icons) at runtime, so read it each time.
                foreach (var c in Live<PeglinUI.Encirclepedia.RunSummaryList>())
                {
                    var list = c;
                    Add(c.buttonGraphicMapper, () =>
                    {
                        MapButtonToInput wiring = list.moveCameraButton != null ? list.moveCameraButton.GetComponent<MapButtonToInput>() : null;
                        return (wiring?._rewiredAction, true);
                    });
                }
                // The Options screen's swap between Accept and the selected row's own button.
                foreach (var c in Live<PeglinUI.SettingsMenu.OverrideButtonMapWhenSelected>())
                {
                    var swap = c;
                    Add(c.buttonGraphicMapperToEnable, () => (swap.mapperToEnable != null ? swap.mapperToEnable.buttonName : null, true));
                    Add(c.buttonGraphicMapperToDisable, () => (swap.mapperToDisable != null ? swap.mapperToDisable.buttonName : null, true));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"Couldn't read some prompt owners: {e.Message}");
            }
            return map;
        }

        private static readonly Dictionary<string, InputAction> GuessedActions = new Dictionary<string, InputAction>();

        /// <summary>
        /// For an icon set we don't have listed (they load with each scene): match its name to an action
        /// ("SpeedupIcons" -> Speedup), or failing that, the one action whose default key is the set's key.
        /// </summary>
        private static InputAction GuessAction(ControllerIconMapping set)
        {
            if (GuessedActions.TryGetValue(set.name, out InputAction cached))
            {
                return cached;
            }
            var actions = ReInput.mapping.Actions.Where(a => a.userAssignable).ToList();
            InputAction guess = null;
            string stem = PromptNames.IconSetStem(set.name);
            var byName = actions.Where(a => a.name.Equals(stem, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 0 && stem.Length >= 4)
            {
                byName = actions.Where(a => a.name.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0
                    || (a.name.Length >= 4 && stem.IndexOf(a.name, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            }
            if (byName.Count == 1)
            {
                guess = byName[0];
            }
            else
            {
                string token = PromptNames.KeyTokenFromSpriteName(set.keyboardIcon != null ? set.keyboardIcon.name : null);
                var byKey = new HashSet<int>();
                foreach (InputLayout layout in ReInput.mapping.MapLayouts(ControllerType.Keyboard))
                {
                    foreach (InputMapCategory category in ReInput.mapping.MapCategories)
                    {
                        ControllerMap defaults = ReInput.mapping.GetKeyboardMapInstance(category.id, layout.id);
                        foreach (ActionElementMap aem in defaults?.AllMaps ?? Enumerable.Empty<ActionElementMap>())
                        {
                            if (token != null && aem.modifierKeyFlags == ModifierKeyFlags.None && PromptNames.KeyToken(aem.keyCode.ToString()) == token)
                            {
                                byKey.Add(aem.actionId);
                            }
                        }
                    }
                }
                // Menu and gameplay actions often share a key (Space is Menu Select and Fire); a prompt in a
                // set we don't know is more likely a gameplay one.
                var candidates = byKey.Select(id => ReInput.mapping.GetAction(id)).Where(a => a != null).ToList();
                if (candidates.Count > 1)
                {
                    candidates = candidates.Where(a => !a.name.StartsWith("UI")).ToList();
                }
                if (candidates.Count == 1)
                {
                    guess = candidates[0];
                }
            }
            GuessedActions[set.name] = guess;
            Plugin.Log.LogInfo(guess != null
                ? $"Button prompt icons '{set.name}' follow the {guess.name} action."
                : $"Couldn't tell which action button prompt icons '{set.name}' are for; they'll show the game's default.");
            return guess;
        }

        /// <summary>A key cap drawn with any short label, e.g. "LMB" for a mouse button.</summary>
        public static Sprite LabelIcon(string label) => KeyboardSprite(label, pressed: false);

        /// <summary>The game's icon for a keyboard key, or one drawn in its style.</summary>
        public static Sprite IconForKey(KeyCode key)
        {
            ControllerIconMapping native = KeyboardSetFor(key);
            return native != null ? native.keyboardIcon : KeyboardSprite(PromptNames.KeyLabel(key.ToString()), pressed: false);
        }

        /// <summary>The icon for a controller button given by short label ("A", "L3"), using the game's art where the pad has it.</summary>
        public static Sprite IconForPadLabel(Player player, string label)
        {
            Joystick joystick = player?.controllers.Joysticks.FirstOrDefault();
            if (joystick != null)
            {
                Dictionary<int, ControllerIconMapping> sets = ElementSets(player, joystick);
                foreach (ControllerElementIdentifier element in joystick.ElementIdentifiers)
                {
                    if (PromptNames.ElementLabel(element.name) == label && sets.TryGetValue(element.id, out ControllerIconMapping native))
                    {
                        Sprite sprite = PlatformSprite(native);
                        if (sprite != null)
                        {
                            return sprite;
                        }
                    }
                }
            }
            return PadSprite(label, pressed: false);
        }

        private static bool SameButton(ControllerIconMapping a, ControllerIconMapping b)
        {
            return a == b || (a.xboxIcon == b.xboxIcon && a.psIcon == b.psIcon && a.switchIcon == b.switchIcon);
        }

        private static readonly HashSet<ControllerIconMapping> GeneratedSetObjects = new HashSet<ControllerIconMapping>();
        private static float _knownSetsAt = -100f;
        private static int _knownSetsScene = -1;

        private static ControllerIconMapping[] KnownSets()
        {
            // Icon sets load with scenes. Scanning every asset per prompt is slow when a menu full of prompts opens,
            // so look again only after a scene change or a couple of seconds.
            int scene = UnityEngine.SceneManagement.SceneManager.sceneCount * 1000 + UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            if (scene == _knownSetsScene && Time.realtimeSinceStartup - _knownSetsAt < 2f)
            {
                return _knownSets;
            }
            _knownSetsScene = scene;
            _knownSetsAt = Time.realtimeSinceStartup;
            ControllerIconMapping[] sets = Resources.FindObjectsOfTypeAll<ControllerIconMapping>()
                .Where(s => s != null && !GeneratedSetObjects.Contains(s)).ToArray();
            if (!sets.SequenceEqual(_knownSets))
            {
                _knownSets = sets;
                ElementSetsByController.Clear();
            }
            return _knownSets;
        }

        private static ControllerIconMapping KeyboardSetFor(KeyCode key)
        {
            string token = PromptNames.KeyToken(key.ToString());
            if (token == null)
            {
                return null;
            }
            return KnownSets().FirstOrDefault(s => s.keyboardIcon != null && PromptNames.KeyTokenFromSpriteName(s.keyboardIcon.name) == token);
        }

        /// <summary>
        /// Which icon set shows each button of this controller, worked out from the game's default controller bindings:
        /// e.g. Accept's icons show A because UI Submit is on A by default.
        /// </summary>
        private static Dictionary<int, ControllerIconMapping> ElementSets(Player player, Joystick joystick)
        {
            return ElementSets(joystick.hardwareTypeGuid, joystick);
        }

        /// <summary>The same, for a controller type that needn't be plugged in (used to show default controller bindings).</summary>
        private static Dictionary<int, ControllerIconMapping> ElementSets(Guid typeGuid, Joystick joystick)
        {
            ControllerIconMapping[] sets = KnownSets();
            string key = typeGuid + "|" + (joystick != null ? joystick.hardwareName : "");
            if (ElementSetsByController.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var result = new Dictionary<int, ControllerIconMapping>();
            var defaults = new List<ControllerMap>();
            // Only the player's own categories: the game's other maps (dialogue and the like) use buttons differently.
            foreach (InputMapCategory category in ReInput.mapping.MapCategories.Where(c => c.userAssignable))
            {
                // Only the layout in use (the first, unless a connected pad uses another): the others swap buttons around,
                // e.g. confirm on B, and would muddle which art shows which button.
                int layoutId = RewiredBindings.ControllerLayout(BindingLoader.GamePlayer, joystick, category.id);
                ControllerMap map = joystick != null
                    ? ReInput.mapping.GetJoystickMapInstance(joystick, category.id, layoutId)
                    : ReInput.mapping.GetJoystickMapInstance(typeGuid, category.id, layoutId);
                if (map != null)
                {
                    defaults.Add(map);
                }
            }
            // Every icon set votes for the button its action is on by default. Some sets reuse another button's art
            // (Remove Orb shows A but defaults to B), so the core menu prompts count double and the most-voted art
            // wins for each button.
            var votes = new Dictionary<int, Dictionary<Sprite, (int score, ControllerIconMapping set)>>();
            foreach (ControllerIconMapping set in sets)
            {
                if (set.xboxIcon == null || !PromptNames.TryGetIconSetAction(set.name, out string actionName, out bool positive))
                {
                    continue;
                }
                InputAction action = ReInput.mapping.GetAction(actionName);
                if (action == null)
                {
                    continue;
                }
                int weight = PromptNames.IsCoreIconSet(set.name) ? 2 : 1;
                foreach (ControllerMap map in defaults)
                {
                    foreach (ActionElementMap aem in RewiredBindings.BindingsFor(map, action.id, action.type == InputActionType.Axis, positive))
                    {
                        if (aem.elementType != ControllerElementType.Button)
                        {
                            continue;
                        }
                        if (!votes.TryGetValue(aem.elementIdentifierId, out var forButton))
                        {
                            votes[aem.elementIdentifierId] = forButton = new Dictionary<Sprite, (int, ControllerIconMapping)>();
                        }
                        forButton.TryGetValue(set.xboxIcon, out var tally);
                        forButton[set.xboxIcon] = (tally.score + weight, tally.set ?? set);
                    }
                }
            }
            foreach (var button in votes)
            {
                result[button.Key] = button.Value.Values.OrderByDescending(v => v.score).First().set;
            }
            ElementSetsByController[key] = result;
            return result;
        }

        /// <summary>The controller sprite the game would pick from an icon set (Xbox, PlayStation or Switch art).</summary>
        private static Sprite PlatformSprite(ControllerIconMapping set)
        {
            if (_platformProbe == null)
            {
                var probe = new GameObject("PeglinKeybinds_PromptProbe") { hideFlags = HideFlags.HideAndDontSave };
                probe.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(probe);
                _platformProbe = probe.AddComponent<ButtonGraphicMapper>();
            }
            MapperIconSet(_platformProbe) = set;
            try
            {
                SetButtonsForController(_platformProbe);
                return MapperPrimary(_platformProbe);
            }
            catch (Exception)
            {
                return set.xboxIcon;
            }
        }

        /// <summary>A copy of a prompt's icon set with the keyboard or controller icons swapped for drawn ones.</summary>
        private static ControllerIconMapping GeneratedSet(ControllerIconMapping own, bool keyboard, string label)
        {
            string key = own.GetInstanceID() + "|" + (keyboard ? "k" : "p") + "|" + label;
            if (GeneratedSets.TryGetValue(key, out ControllerIconMapping set) && set != null)
            {
                return set;
            }
            set = ScriptableObject.CreateInstance<ControllerIconMapping>();
            set.name = own.name;
            set.hideFlags = HideFlags.HideAndDontSave;
            foreach (string field in IconSetFields)
            {
                Traverse.Create(set).Field(field).SetValue(Traverse.Create(own).Field(field).GetValue());
            }
            if (keyboard)
            {
                Traverse.Create(set).Field("_keyboardIcon").SetValue(KeyboardSprite(label, false));
                Traverse.Create(set).Field("_keyboardIconPressed").SetValue(KeyboardSprite(label, true));
            }
            else
            {
                Sprite up = PadSprite(label, false), down = PadSprite(label, true);
                foreach (string platform in new[] { "_xbox", "_ps", "_switch" })
                {
                    Traverse.Create(set).Field(platform + "Icon").SetValue(up);
                    Traverse.Create(set).Field(platform + "IconPressed").SetValue(down);
                }
            }
            GeneratedSets[key] = set;
            GeneratedSetObjects.Add(set);
            return set;
        }

        private static Sprite KeyboardSprite(string label, bool pressed)
        {
            if (label == null)
            {
                return Generated("blank", () => new PixelGrid(1, 1), false);
            }
            return Generated("k|" + label + "|" + pressed, () => PixelArt.Keycap(label, pressed), pressed);
        }

        private static Sprite PadSprite(string label, bool pressed)
        {
            if (label == null)
            {
                return Generated("blank", () => new PixelGrid(1, 1), false);
            }
            return Generated("p|" + label + "|" + pressed, () => PixelArt.PadButton(label), pressed);
        }

        private static Sprite Generated(string key, Func<PixelGrid> draw, bool pressed)
        {
            if (GeneratedSprites.TryGetValue(key, out Sprite sprite) && sprite != null)
            {
                return sprite;
            }
            PixelGrid grid = draw();
            var texture = new Texture2D(grid.Width, grid.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[grid.Width * grid.Height];
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    // Textures are stored bottom row first.
                    pixels[(grid.Height - 1 - y) * grid.Width + x] = Colour(grid[x, y], pressed);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            // Same pixels-per-unit as the game's prompt sprites, so drawn icons come out the same size.
            sprite = Sprite.Create(texture, new Rect(0, 0, grid.Width, grid.Height), new Vector2(0.5f, 0.5f), 16f);
            sprite.name = "PeglinKeybinds_" + key;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            GeneratedSprites[key] = sprite;
            return sprite;
        }

        private static Color32 Colour(Ink ink, bool pressed)
        {
            switch (ink)
            {
                case Ink.Outline: return Outline;
                case Ink.Side: return pressed ? SidePressed : Side;
                case Ink.Face: return pressed ? FacePressed : Face;
                case Ink.Bottom: return Bottom;
                case Ink.Glyph: return pressed ? GlyphPressed : Glyph;
                default: return new Color32(0, 0, 0, 0);
            }
        }

        [HarmonyPatch(typeof(ButtonGraphicMapper), "UpdateInputType")]
        private static class UpdateInputTypePatch
        {
            private static void Prefix(ButtonGraphicMapper __instance, out ControllerIconMapping __state)
            {
                __state = null;
                ControllerIconMapping own = MapperIconSet(__instance);
                if (own == null)
                {
                    return;
                }
                try
                {
                    ControllerIconMapping replacement = ReplacementFor(__instance, own);
                    if (replacement != null && replacement != own)
                    {
                        __state = own;
                        MapperIconSet(__instance) = replacement;
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogDebug($"Prompt lookup failed for {__instance.name}: {e.Message}");
                }
            }

            private static void Postfix(ButtonGraphicMapper __instance, ControllerIconMapping __state)
            {
                if (__state == null)
                {
                    return;
                }
                // Put the prompt's own icon set back, so the next lookup starts from what the game intended.
                MapperIconSet(__instance) = __state;
                Image image = MapperImage(__instance);
                if (image != null)
                {
                    // Swapped-in icons can be wider or narrower than the original; keep them from stretching.
                    image.preserveAspect = true;
                }
            }
        }
    }
}
