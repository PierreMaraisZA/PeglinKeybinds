using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PeglinKeybinds.Core;
using PeglinUI.SettingsMenu;
using PeglinUI.SettingsMenu.Options;
using Rewired;
using TMPro;
using UI;
using UI.ControllerSupport;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeglinKeybinds
{
    /// <summary>
    /// The Controls screen, opened from the Options menu. It's built from a copy of the Options panel itself (same
    /// frame, title scroll, rows, buttons and pop-up), with a row per game action and a keyboard and a controller
    /// button on each. Navigation is the game's own: mouse, keyboard or controller, through its event system.
    /// </summary>
    internal class ControlsScreen : MonoBehaviour
    {
        private const string ScreenName = "PeglinKeybinds_ControlsScreen";
        private const int KeyboardColumn = 0;
        private const int ControllerColumn = 1;
        private const float CellWidth = 250f;
        private const float LabelWidth = 330f;
        private static readonly float[] ColumnX = { 25f, 290f };

        // A warm brown that stands apart from the dark row labels, for section headings.
        private static readonly Color SectionColour = new Color(0.45f, 0.29f, 0.18f, 1f);

        private static readonly Action<OptionsMenu, bool> SetOptionsButtonsActive =
            AccessTools.MethodDelegate<Action<OptionsMenu, bool>>(AccessTools.Method(typeof(OptionsMenu), "SetButtonsActive"));

        private static int _blockUntilFrame = -1;

        /// <summary>A row on screen: a game action (<see cref="Row"/>) or another mod's key setting(s) (<see cref="Mod"/>).</summary>
        private sealed class RowView
        {
            public BindingRow Row;
            public ModKeyRow Mod;

            /// <summary>For a mouse row: Rewired's id for the mouse button, its name, and Unity's KeyCode name (for clashes).</summary>
            public int MouseElement = -1;
            public string MouseLabel;
            public string MouseKeyName;
            public Image Arrow;
            public readonly Button[] Cells = new Button[2];
            public readonly Image[] Icons = new Image[2];
            public readonly TMP_Text[] Texts = new TMP_Text[2];
            public readonly List<string>[] Clashes = { new List<string>(), new List<string>() };

            public string Label => Row != null ? Row.Label : Mod != null ? Mod.Label : MouseLabel;

            public bool IsMouse => MouseElement >= 0;

            public ModKeySetting ModSetting(int column) => Mod == null ? null : column == KeyboardColumn ? Mod.Keyboard : Mod.Controller;
        }

        // Amber, like a warning light, for a key that something else also uses.
        private static readonly Color ClashColour = new Color(0.86f, 0.58f, 0.18f, 1f);

        private List<ModKeySetting> _modSettings = new List<ModKeySetting>();

        // The map category the game keeps its actions in (it has just one); mouse bindings go in the same one.
        private int _gameCategory;

        private OptionsMenu _menu;
        private GameObject _returnTo;
        private readonly List<RowView> _rows = new List<RowView>();
        private TMP_Text _controllerHeader;
        private Button _backButton;

        /// <summary>A section heading in the list, and what the sticky strip shows while you're in that section.</summary>
        private sealed class Heading
        {
            public RectTransform Rect;
            public string StickyText;
            public string FirstColumn = "Keyboard";
            public bool ShowController = true;
        }

        // The column titles (and, once its heading has scrolled away, the current section's name) sit in a strip
        // above the list so they're always on screen, however far down the controller has moved the selection.
        // Tall enough that its text, centred in it, sits below the bottom of the title banner.
        private const float StickyHeight = 72f;
        private readonly List<Heading> _headings = new List<Heading>();
        private ScrollRect _scroll;
        private TMP_Text _stickyLabel;
        private TMP_Text _stickyFirstColumn;
        private GameObject _scrolledFor;
        private float? _scrollGoal;
        private Button _resetButton;
        private readonly List<Behaviour> _bottomTriggers = new List<Behaviour>();
        private GameObject _lastSelected;

        private GameObject _modal;
        private TMP_Text _modalText;
        private Button _yesButton, _noButton, _okayButton;
        private Button[] _modalButtons = new Button[0];
        private readonly List<Button> _extraModalButtons = new List<Button>();
        private int _modalCancelIndex;
        private Action<int> _modalCallback;

        private bool _listening;
        private bool _waitForRelease;
        private bool _restoreMapsOnRelease;
        private float _restoreMapsAt;
        private Controller _listenController;
        private RowView _listenRow;
        private int _listenColumn;
        private bool _listenAdd;
        private ModKeySetting _pendingModSetting;
        private KeyCode _pendingModKey;
        private float _listenTimeLeft;
        private readonly Dictionary<(ControllerType, int, int, int), bool> _mapStates = new Dictionary<(ControllerType, int, int, int), bool>();

        /// <summary>The open Controls screen, if any.</summary>
        public static ControlsScreen Current { get; private set; }

        /// <summary>
        /// True while the screen is open and for a couple of frames after, so the button press that closed it doesn't
        /// also close the Options menu.
        /// </summary>
        public static bool BlocksGameMenus => Current != null || Time.frameCount <= _blockUntilFrame;

        private static readonly AccessTools.FieldRef<PauseMenu, GameObject> PausePanel = AccessTools.FieldRefAccess<PauseMenu, GameObject>("_pausePanel");
        private static readonly AccessTools.FieldRef<PauseMenu, bool> PauseIsSubMenu = AccessTools.FieldRefAccess<PauseMenu, bool>("_isSubMenu");
        private static readonly Action<PauseMenu, bool> SetPaused =
            AccessTools.MethodDelegate<Action<PauseMenu, bool>>(AccessTools.Method(typeof(PauseMenu), "SetPaused"));

        /// <summary>
        /// The hotkey: opens Options (the way the game would, by pausing) and then the Controls screen on top of it.
        /// Closing Controls leaves you in Options, and closing Options carries on as normal.
        /// </summary>
        public static IEnumerator OpenFromAnywhere()
        {
            var menus = Resources.FindObjectsOfTypeAll<OptionsMenu>().Where(m => m != null && m.gameObject.scene.isLoaded).ToList();
            OptionsMenu menu = menus.FirstOrDefault(m => m.isActiveAndEnabled);
            if (menu == null)
            {
                if (menus.Count == 0)
                {
                    // During a run the pause menu has no Options screen to open Controls from.
                    Plugin.Log.LogWarning("There's no Options menu here; open Controls from the main menu's Options.");
                    yield break;
                }
                if (PauseMenu.PauseBlock)
                {
                    yield break;
                }
                var pauseMenus = Resources.FindObjectsOfTypeAll<PauseMenu>().Where(p => p != null && p.gameObject.scene.isLoaded && p.isActiveAndEnabled).ToList();
                // On the main menu the Options panel is itself a "pause" panel; opening it is how the game shows it.
                PauseMenu owner = pauseMenus.FirstOrDefault(p => menus.Any(m => PausePanel(p) == m.gameObject));
                if (owner != null)
                {
                    menu = menus.First(m => PausePanel(owner) == m.gameObject);
                    SetPaused(owner, true);
                }
                else
                {
                    // During a run: pause the game, then bring up its Options panel over the pause screen.
                    PauseMenu pause = pauseMenus.FirstOrDefault(p => !PauseIsSubMenu(p));
                    if (pause != null && !PauseMenu.Paused)
                    {
                        SetPaused(pause, true);
                    }
                    menu = menus.First();
                    menu.gameObject.SetActive(true);
                }
                // Let the Options panel finish waking up (that's when the Controls row is added).
                yield return null;
                if (!menu.isActiveAndEnabled)
                {
                    Plugin.Log.LogWarning("Couldn't open the Options menu here; open Controls from Options instead.");
                    yield break;
                }
            }
            Transform row = menu.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == OptionsMenuPatch.RowName);
            Open(menu, row != null ? row.gameObject : null);
        }

        public static void Open(OptionsMenu menu, GameObject returnTo)
        {
            Player player = BindingLoader.GamePlayer;
            if (player == null || menu == null || Current != null)
            {
                return;
            }
            ControlsScreen screen = null;
            Transform existing = menu.transform.Find(ScreenName);
            if (existing != null)
            {
                screen = existing.GetComponent<ControlsScreen>();
            }
            if (screen == null)
            {
                try
                {
                    screen = Build(menu, player);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Couldn't build the Controls screen: {e}");
                    return;
                }
            }
            if (screen != null)
            {
                screen._returnTo = returnTo;
                screen.Show();
            }
        }

        private static ControlsScreen Build(OptionsMenu menu, Player player)
        {
            // Copy the whole Options panel under an inactive parent, so none of its scripts wake up while we strip it.
            var holder = new GameObject("PeglinKeybinds_Holder");
            holder.SetActive(false);
            try
            {
                return BuildUnder(menu, player, holder.transform);
            }
            finally
            {
                // Also cleans up a half-built screen if anything above threw.
                if (holder != null)
                {
                    DestroyImmediate(holder);
                }
            }
        }

        private static ControlsScreen BuildUnder(OptionsMenu menu, Player player, Transform holder)
        {
            GameObject root = Instantiate(menu.gameObject, holder, false);
            root.name = ScreenName;
            foreach (MonoBehaviour script in root.GetComponents<MonoBehaviour>())
            {
                if (!(script is Graphic))
                {
                    DestroyImmediate(script);
                }
            }

            var keep = new HashSet<string> { "BasePanel", "HeaderDropShadow", "Scrollbar Vertical", "TitleBackground", "ModalPanel", menu.cancelButton.name, menu.applyButton.name };
            foreach (Transform child in root.transform.Cast<Transform>().ToList())
            {
                if (!keep.Contains(child.name))
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            ScrollRect scroll = root.GetComponentInChildren<ScrollRect>(true);
            Transform contents = scroll != null ? scroll.content : null;
            Transform template = contents != null ? contents.Find(menu.deleteButtonSelectable.name) : null;
            if (template == null)
            {
                Plugin.Log.LogWarning("Options menu layout not recognised; the Controls screen can't be built.");
                return null;
            }
            foreach (Transform child in contents.Cast<Transform>().ToList())
            {
                if (child != template && child.name != "TopPadding" && child.name != "BottomPadding")
                {
                    DestroyImmediate(child.gameObject);
                }
            }
            // The top padding keeps the first row clear of the title banner; the sticky strip does that here, and the
            // padding would just be an empty row under it.
            Transform topPadding = contents.Find("TopPadding");
            if (topPadding != null)
            {
                topPadding.gameObject.SetActive(false);
            }
            UiCopy.RemoveScripts(root, "Localize", "OverrideButtonMapWhenSelected", "ArrowSelection", "ModalPanel");
            if (contents.TryGetComponent(out EnsureGridItemIsVisible scroller))
            {
                // It scrolls to a point proportional to the row's position, which can leave a selected row's
                // section heading out of sight; FollowSelection does this instead.
                DestroyImmediate(scroller);
            }

            var screen = root.AddComponent<ControlsScreen>();
            screen._menu = menu;
            screen._scroll = scroll;
            screen.SetUpFrame(root.transform, menu);
            screen.SetUpModal(root.transform.Find("ModalPanel"));
            screen.BuildRows(player, template.gameObject, contents);
            DestroyImmediate(template.gameObject);

            root.SetActive(false);
            root.transform.SetParent(menu.transform, false);
            root.transform.SetAsLastSibling();
            return screen;
        }

        private void SetUpFrame(Transform root, OptionsMenu menu)
        {
            Transform title = UiCopy.FindDeep(root, "TitleText");
            if (title != null && title.TryGetComponent(out TMP_Text titleText))
            {
                titleText.text = "Controls";
            }
            _backButton = SetUpBottomButton(root.Find(menu.cancelButton.name), "Back", Close, true);
            // No shortcut for Reset: it was too easy to hit by accident, and it left the selection down at the bottom.
            _resetButton = SetUpBottomButton(root.Find(menu.applyButton.name), "Reset", AskReset, false);
        }

        private Button SetUpBottomButton(Transform t, string label, Action onClick, bool shortcut)
        {
            if (t == null || !t.TryGetComponent(out Button button))
            {
                return null;
            }
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
            foreach (TMP_Text text in t.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text = label;
            }
            if (!shortcut)
            {
                // Drop the button press and the prompt icon that advertises it; it's still clickable and selectable.
                UiCopy.RemoveScripts(t.gameObject, "RewiredUIButtonTrigger");
                foreach (ButtonGraphicMapper prompt in t.GetComponentsInChildren<ButtonGraphicMapper>(true))
                {
                    prompt.gameObject.SetActive(false);
                }
            }
            // The game presses Back with UI Cancel, like on the Options screen.
            else if (t.TryGetComponent(out RewiredUIButtonTrigger trigger))
            {
                _bottomTriggers.Add(trigger);
            }
            return button;
        }

        private void SetUpModal(Transform modal)
        {
            if (modal == null)
            {
                return;
            }
            _modal = modal.gameObject;
            _modal.SetActive(false);
            Transform question = UiCopy.FindDeep(modal, "QuestionText");
            _modalText = question != null ? question.GetComponent<TMP_Text>() : null;
            Transform dismiss = UiCopy.FindDeep(modal, "DismissCheckmark");
            if (dismiss != null)
            {
                dismiss.gameObject.SetActive(false);
            }
            _yesButton = ModalButton(modal, "YesButton");
            _noButton = ModalButton(modal, "NoButton");
            _okayButton = ModalButton(modal, "OkayButton");
            // The game's No button has smaller, darker text than Yes; match them all to Yes so every option reads the same.
            TMP_Text yesLabel = _yesButton != null ? _yesButton.GetComponentInChildren<TMP_Text>(true) : null;
            if (yesLabel != null)
            {
                foreach (Button b in new[] { _noButton, _okayButton })
                {
                    TMP_Text label = b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;
                    if (label != null)
                    {
                        label.font = yesLabel.font;
                        label.enableAutoSizing = yesLabel.enableAutoSizing;
                        label.fontSizeMin = yesLabel.fontSizeMin;
                        label.fontSizeMax = yesLabel.fontSizeMax;
                        label.fontSize = yesLabel.fontSize;
                        label.color = yesLabel.color;
                        label.alignment = yesLabel.alignment;
                        label.rectTransform.anchorMin = yesLabel.rectTransform.anchorMin;
                        label.rectTransform.anchorMax = yesLabel.rectTransform.anchorMax;
                        label.rectTransform.sizeDelta = yesLabel.rectTransform.sizeDelta;
                        label.rectTransform.anchoredPosition = yesLabel.rectTransform.anchoredPosition;
                    }
                }
            }
            // The game tints its green pop-up button red for No, which comes out dark and hard to read. Give it the
            // look of the red Cancel button at the bottom of the Options screen instead.
            if (_noButton != null && _backButton != null && _noButton.TryGetComponent(out Image noImage) && _backButton.TryGetComponent(out Image backImage))
            {
                noImage.sprite = backImage.sprite;
                noImage.color = backImage.color;
                noImage.type = backImage.type;
                _noButton.transition = _backButton.transition;
                _noButton.spriteState = _backButton.spriteState;
            }
            Transform row = UiCopy.FindDeep(modal, "HorizontalContainer");
            if (row is RectTransform rowRect)
            {
                rowRect.sizeDelta = new Vector2(620f, rowRect.sizeDelta.y);
            }
            // Wider than the game's pop-up so up to five choices fit in a row.
            foreach (string part in new[] { "WindowBg", "WindowBorder" })
            {
                if (UiCopy.FindDeep(modal, part) is RectTransform partRect)
                {
                    partRect.sizeDelta = new Vector2(680f, partRect.sizeDelta.y);
                }
            }
            if (_modalText != null)
            {
                _modalText.rectTransform.sizeDelta = new Vector2(620f, _modalText.rectTransform.sizeDelta.y);
            }
        }

        private Button ModalButton(Transform modal, string name)
        {
            Transform t = UiCopy.FindDeep(modal, name);
            if (t == null || !t.TryGetComponent(out Button button))
            {
                return null;
            }
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => ModalChoose(Array.IndexOf(_modalButtons, button)));
            return button;
        }

        private void BuildRows(Player player, GameObject template, Transform contents)
        {
            // Strip the Delete Save Data row down to a label and a button: the row itself isn't selectable here,
            // its two buttons are.
            foreach (MonoBehaviour script in template.GetComponents<MonoBehaviour>())
            {
                if (script is Selectable || script.GetType().Name == "ButtonHandleHover")
                {
                    DestroyImmediate(script);
                }
            }
            foreach (ButtonGraphicMapper prompt in template.GetComponentsInChildren<ButtonGraphicMapper>(true))
            {
                DestroyImmediate(prompt.gameObject);
            }
            Button cellTemplate = template.GetComponentInChildren<Button>(true);
            UiCopy.RemoveScripts(cellTemplate.gameObject, "RewiredUIButtonTrigger");
            TMP_Text labelTemplate = template.GetComponentsInChildren<TMP_Text>(true).First(t => !t.transform.IsChildOf(cellTemplate.transform));
            int insertAt = template.transform.GetSiblingIndex();
            SetUpStickyStrip(template, labelTemplate);

            // Mouse buttons first: they are what most players look for. The rows show whatever the game has bound to
            // each button (Right Click is Discard Orb / Close Inspect by default); anything assigned here comes on
            // top of the game's own clicking.
            Mouse mouse = ReInput.controllers.Mouse;
            if (mouse != null)
            {
                var buttons = new[] { ("Left Mouse Button", "Left Click", "Mouse0"), ("Right Mouse Button", "Right Click", "Mouse1"), ("Mouse Button 3", "Middle Click", "Mouse2") };
                bool heading = false;
                foreach (var (elementName, label, keyName) in buttons)
                {
                    ControllerElementIdentifier element = mouse.ElementIdentifiers.FirstOrDefault(e => e.name == elementName);
                    if (element == null)
                    {
                        continue;
                    }
                    if (!heading)
                    {
                        Heading mouseHeading = AddHeading(template, contents, ref insertAt, "Mouse", 1f);
                        mouseHeading.FirstColumn = "Action";
                        mouseHeading.ShowController = false;
                        heading = true;
                    }
                    AddBindingRow(template, contents, ref insertAt, labelTemplate, label,
                        new RowView { MouseElement = element.id, MouseLabel = label, MouseKeyName = keyName });
                }
            }

            foreach (BindingRow row in RewiredBindings.BuildRows(player))
            {
                if (row.IsHeader)
                {
                    AddHeading(template, contents, ref insertAt, row.Label, 1f);
                    continue;
                }
                AddBindingRow(template, contents, ref insertAt, labelTemplate, row.Label, new RowView { Row = row });
                _gameCategory = row.CategoryId;
            }

            // Keys from other installed mods' configs, under a Mods heading with a smaller heading per mod.
            _modSettings = ModBindings.Discover();
            var modSections = ModKeys.BuildSections(_modSettings);
            if (modSections.Count > 0)
            {
                AddHeading(template, contents, ref insertAt, "Mods", 1f);
                foreach (var (plugin, rows) in modSections)
                {
                    AddHeading(template, contents, ref insertAt, plugin, 0.8f).StickyText = "Mods: " + plugin;
                    foreach (ModKeyRow modRow in rows)
                    {
                        AddBindingRow(template, contents, ref insertAt, labelTemplate, modRow.Label, new RowView { Mod = modRow });
                    }
                }
            }
        }

        private Heading AddHeading(GameObject template, Transform contents, ref int insertAt, string text, float scale)
        {
            GameObject header = MakeHeading(template, contents, text, scale);
            header.transform.SetSiblingIndex(insertAt++);
            var heading = new Heading { Rect = (RectTransform)header.transform, StickyText = text };
            _headings.Add(heading);
            return heading;
        }

        /// <summary>
        /// The strip above the list: a heading-style row with the column titles, outside the scrolling area. The list
        /// is shortened by its height so nothing scrolls underneath it.
        /// </summary>
        private void SetUpStickyStrip(GameObject template, TMP_Text labelTemplate)
        {
            RectTransform viewport = _scroll != null ? _scroll.viewport : null;
            if (viewport == null || viewport.parent == null)
            {
                return;
            }
            GameObject strip = MakeHeading(template, viewport.parent, "", 1f);
            strip.name = "PeglinKeybinds_StickyHeader";
            if (strip.TryGetComponent(out LayoutElement layout))
            {
                DestroyImmediate(layout);
            }
            var rect = (RectTransform)strip.transform;
            float top = viewport.offsetMax.y;
            rect.anchorMin = new Vector2(viewport.anchorMin.x, 1f);
            rect.anchorMax = new Vector2(viewport.anchorMax.x, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(viewport.offsetMin.x, top - StickyHeight);
            rect.offsetMax = new Vector2(viewport.offsetMax.x, top);
            viewport.offsetMax = new Vector2(viewport.offsetMax.x, top - StickyHeight);
            // Drawn over the scrollbar's top end rather than under it.
            strip.transform.SetAsLastSibling();

            _stickyLabel = strip.GetComponentInChildren<TMP_Text>(true);
            _stickyFirstColumn = HeaderText(labelTemplate, strip.transform, "Keyboard", KeyboardColumn);
            _controllerHeader = HeaderText(labelTemplate, strip.transform, "Controller", ControllerColumn);
        }

        private static GameObject MakeHeading(GameObject template, Transform parent, string text, float scale)
        {
            GameObject header = Instantiate(template, parent, false);
            header.name = "Section " + text;
            DestroyImmediate(header.GetComponentInChildren<Button>(true).gameObject);
            TMP_Text headerLabel = header.GetComponentInChildren<TMP_Text>(true);
            headerLabel.text = text;
            headerLabel.color = SectionColour;
            headerLabel.fontSize *= scale;
            headerLabel.rectTransform.sizeDelta = new Vector2(LabelWidth * 2f, headerLabel.rectTransform.sizeDelta.y);
            Transform headerArrow = header.transform.Find("ArrowSelection");
            if (headerArrow != null)
            {
                headerArrow.gameObject.SetActive(false);
            }
            return header;
        }

        private void AddBindingRow(GameObject template, Transform contents, ref int insertAt, TMP_Text labelTemplate, string text, RowView view)
        {
            GameObject go = Instantiate(template, contents, false);
            go.name = "Binding " + text;
            go.transform.SetSiblingIndex(insertAt++);
            TMP_Text label = go.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == labelTemplate.name && t.GetComponentInParent<Button>() == null);
            label.text = text;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18f;
            label.fontSizeMax = label.fontSize;
            label.rectTransform.sizeDelta = new Vector2(LabelWidth, label.rectTransform.sizeDelta.y);
            Transform arrow = go.transform.Find("ArrowSelection");
            if (arrow != null && arrow.TryGetComponent(out Image arrowImage))
            {
                view.Arrow = arrowImage;
                arrowImage.enabled = false;
            }

            Button first = go.GetComponentInChildren<Button>(true);
            Button second = Instantiate(first.gameObject, go.transform, false).GetComponent<Button>();
            SetUpCell(view, KeyboardColumn, first);
            SetUpCell(view, ControllerColumn, second);
            _rows.Add(view);
        }

        private static TMP_Text HeaderText(TMP_Text template, Transform parent, string text, int column)
        {
            TMP_Text heading = Instantiate(template.gameObject, parent, false).GetComponent<TMP_Text>();
            heading.text = text;
            heading.alignment = TextAlignmentOptions.Center;
            RectTransform rect = heading.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(CellWidth, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(ColumnX[column], 0f);
            return heading;
        }

        private void SetUpCell(RowView view, int column, Button cell)
        {
            cell.name = column == KeyboardColumn ? "Keyboard" : "Controller";
            var rect = (RectTransform)cell.transform;
            rect.sizeDelta = new Vector2(CellWidth, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(ColumnX[column], rect.anchoredPosition.y);
            cell.onClick = new Button.ButtonClickedEvent();
            int rowIndex = _rows.Count;
            cell.onClick.AddListener(() => OnCellClicked(rowIndex, column));

            TMP_Text text = cell.GetComponentInChildren<TMP_Text>(true);
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = text.fontSize;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(44f, 8f);
            textRect.offsetMax = new Vector2(-10f, -4f);

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(cell.transform, false);
            var iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(8f, 2f);
            iconRect.sizeDelta = new Vector2(32f, 22f);
            Image image = icon.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;

            view.Cells[column] = cell;
            view.Texts[column] = text;
            view.Icons[column] = image;
        }

        private void Show()
        {
            Current = this;
            gameObject.SetActive(true);
            _scrollGoal = null;
            _scrolledFor = null;
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1f;
            }
            // Turn off the Options screen's own Accept / Apply / Cancel shortcuts while we're on top of it, and hide
            // those buttons: ours sit in the same places, but there's no Accept to cover theirs.
            SetOptionsButtonsActive(_menu, false);
            SetOptionsBottomButtonsVisible(false);
            RowAcceptSwap swap = _returnTo != null ? _returnTo.GetComponent<RowAcceptSwap>() : null;
            if (swap != null)
            {
                swap.HandOver();
            }
            SetBottomTriggers(false);
            StartCoroutine(EnableBottomTriggersSoon());

            Refresh();
            ReInput.ControllerConnectedEvent += OnControllersChanged;
            ReInput.ControllerDisconnectedEvent += OnControllersChanged;
            Button firstCell = _rows.SelectMany(r => r.Cells).FirstOrDefault(c => c != null && c.interactable);
            Select(firstCell != null ? firstCell.gameObject : _backButton?.gameObject);
        }

        private void Close()
        {
            if (Current != this)
            {
                return;
            }
            CleanUp();
            gameObject.SetActive(false);
            if (_returnTo != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_returnTo);
            }
            // Give the Options screen its shortcuts back once the button that closed us has been let go of.
            if (BindingLoader.Instance != null)
            {
                BindingLoader.Instance.StartCoroutine(RestoreOptions(_menu));
            }
        }

        private void OnDisable()
        {
            // The Options menu closed under us (or the scene changed): put everything back.
            if (Current == this)
            {
                CleanUp();
                if (_menu != null)
                {
                    SetOptionsButtonsActive(_menu, true);
                }
            }
        }

        private void SetOptionsBottomButtonsVisible(bool visible)
        {
            if (_menu == null)
            {
                return;
            }
            foreach (Component button in new Component[] { _menu.acceptButton, _menu.applyButton, _menu.cancelButton })
            {
                if (button != null)
                {
                    button.gameObject.SetActive(visible);
                }
            }
        }

        private void CleanUp()
        {
            SetOptionsBottomButtonsVisible(true);
            if (_listening || _restoreMapsOnRelease || _mapStates.Count > 0)
            {
                RestoreMaps();
            }
            if (_pendingModSetting != null)
            {
                ModBindings.Set(_pendingModSetting, _pendingModKey);
                _pendingModSetting = null;
            }
            _listening = false;
            _restoreMapsOnRelease = false;
            _waitForRelease = false;
            if (_modal != null)
            {
                _modal.SetActive(false);
            }
            _modalCallback = null;
            ReInput.ControllerConnectedEvent -= OnControllersChanged;
            ReInput.ControllerDisconnectedEvent -= OnControllersChanged;
            Current = null;
            _blockUntilFrame = Time.frameCount + 2;
        }

        private static IEnumerator RestoreOptions(OptionsMenu menu)
        {
            yield return null;
            yield return null;
            if (menu != null && menu.isActiveAndEnabled)
            {
                SetOptionsButtonsActive(menu, true);
            }
        }

        private IEnumerator EnableBottomTriggersSoon()
        {
            yield return null;
            yield return null;
            if (_modal == null || !_modal.activeSelf)
            {
                SetBottomTriggers(true);
            }
        }

        private void SetBottomTriggers(bool on)
        {
            foreach (Behaviour trigger in _bottomTriggers)
            {
                if (trigger != null)
                {
                    trigger.enabled = on;
                }
            }
        }

        private void OnControllersChanged(ControllerStatusChangedEventArgs args)
        {
            if (!_listening)
            {
                Refresh();
                return;
            }
            // A pad plugged in mid-listen comes with its maps switched on; keep them off until listening ends, and
            // remember them as on so they come back then.
            Player player = BindingLoader.GamePlayer;
            if (player == null)
            {
                return;
            }
            foreach (ControllerMap map in AllMaps(player))
            {
                var key = (map.controllerType, map.controllerId, map.categoryId, map.layoutId);
                if (!_mapStates.ContainsKey(key))
                {
                    _mapStates[key] = map.enabled;
                    map.enabled = false;
                }
            }
        }

        private Controller ControllerFor(Player player, int column)
        {
            return column == KeyboardColumn ? (Controller)ReInput.controllers.Keyboard : player.controllers.Joysticks.FirstOrDefault();
        }

        /// <summary>Shows each row's current bindings and works out which buttons can be reached.</summary>
        private void Refresh()
        {
            Player player = BindingLoader.GamePlayer;
            if (player == null)
            {
                return;
            }
            Controller joystick = ControllerFor(player, ControllerColumn);
            if (_controllerHeader != null)
            {
                _controllerHeader.text = joystick != null ? "Controller"
                    : RewiredBindings.DefaultControllerMap(_gameCategory).map != null ? "Controller (defaults)" : "No controller";
            }
            ClashIndex clashes = BuildClashIndex(player);
            foreach (RowView view in _rows)
            {
                for (int column = 0; column < 2; column++)
                {
                    if (view.Mod != null)
                    {
                        RefreshModCell(view, column, clashes);
                    }
                    else if (view.IsMouse)
                    {
                        RefreshMouseCell(player, view, column, clashes);
                    }
                    else
                    {
                        RefreshGameCell(player, view, column, clashes);
                    }
                }
            }
            WireNavigation();
        }

        private ControllerMap MouseMap(Player player)
        {
            return player.controllers.maps.GetFirstMapInCategory(ControllerType.Mouse, 0, _gameCategory);
        }

        /// <summary>
        /// The actions on a mouse button that this screen lists. The game also puts an internal "MouseClick" action
        /// on the left button for its own clicking; that one isn't shown and is never removed, so clicking keeps working.
        /// </summary>
        private List<ActionElementMap> MouseBindings(ControllerMap map, RowView view)
        {
            return map == null ? new List<ActionElementMap>()
                : map.AllMaps.Where(a => a.elementIdentifierId == view.MouseElement && _rows.Any(r => r.Row != null && r.Row.ActionId == a.actionId)).ToList();
        }

        /// <summary>The name a game action goes by on this screen, for one direction of it.</summary>
        private string ActionLabel(int actionId, bool positive)
        {
            RowView row = _rows.FirstOrDefault(r => r.Row != null && r.Row.ActionId == actionId && (!r.Row.IsAxis || r.Row.Positive == positive));
            if (row != null)
            {
                return row.Label;
            }
            InputAction action = ReInput.mapping.GetAction(actionId);
            return action == null ? "?" : BindingRows.Label(RewiredBindings.Info(action), positive);
        }

        /// <summary>A mouse row has one button, in the first column: the action(s) that mouse button triggers.</summary>
        private void RefreshMouseCell(Player player, RowView view, int column, ClashIndex clashes)
        {
            ControllerMap map = column == KeyboardColumn ? MouseMap(player) : null;
            List<ActionElementMap> bindings = MouseBindings(map, view);
            // Clashes as seen by each action on this button, leaving out the button's own actions.
            var own = bindings.Select(b => ActionLabel(b.actionId, b.axisContribution == Pole.Positive)).ToList();
            string mouseId = ClashIndex.KeyboardId(view.MouseKeyName);
            view.Clashes[column] = map == null ? new List<string>()
                : (own.Count == 0 ? clashes.ClashesFor(mouseId, view.MouseLabel, false) : own.SelectMany(o => clashes.ClashesFor(mouseId, o, false)))
                    .Where(c => !own.Contains(c)).Distinct().ToList();
            string text = map == null ? null : string.Join(" / ", own.Distinct().ToArray());
            ShowCell(view, column, map != null, text, map == null ? null : PromptIcons.LabelIcon(MouseIconLabel(view)));
        }

        private static string MouseIconLabel(RowView view) => view.MouseKeyName == "Mouse0" ? "LMB" : view.MouseKeyName == "Mouse1" ? "RMB" : "MMB";

        private void RefreshGameCell(Player player, RowView view, int column, ClashIndex clashes)
        {
            Controller controller = ControllerFor(player, column);
            if (controller == null && column == ControllerColumn)
            {
                // No controller plugged in: show the game's defaults, greyed out, until one is.
                var (type, defaults) = RewiredBindings.DefaultControllerMap(view.Row.CategoryId);
                List<ActionElementMap> preview = RewiredBindings.BindingsFor(defaults, view.Row);
                view.Clashes[column] = new List<string>();
                ShowCell(view, column, false, defaults == null ? null : string.Join(" / ", preview.Select(RewiredBindings.Describe).Distinct().ToArray()),
                    preview.Count > 0 ? PromptIcons.IconFor(player, preview[0], view.Row.Positive, type) : null, preview: defaults != null);
                return;
            }
            ControllerMap map = RewiredBindings.MapFor(player, controller, view.Row);
            List<ActionElementMap> bindings = RewiredBindings.BindingsFor(map, view.Row);
            view.Clashes[column] = bindings.SelectMany(b => clashes.ClashesFor(GameClashId(b), view.Row.Label, false)).Distinct().ToList();
            Sprite icon = bindings.Count > 0 ? PromptIcons.IconFor(player, bindings[0], view.Row.Positive) : null;
            string text = map == null ? null : string.Join(" / ", bindings.Select(RewiredBindings.Describe).Distinct().ToArray());
            ShowCell(view, column, map != null, text, icon);
        }

        private void RefreshModCell(RowView view, int column, ClashIndex clashes)
        {
            ModKeySetting setting = view.ModSetting(column);
            KeyCode key = ModBindings.Current(setting);
            view.Clashes[column] = setting == null ? new List<string>() : clashes.ClashesFor(ModBindings.ClashId(setting), ModBindings.Owner(view.Mod), true);
            Sprite icon = key == KeyCode.None ? null
                : ModKeys.IsJoystickKey(key.ToString()) ? PromptIcons.IconForPadLabel(BindingLoader.GamePlayer, ModKeys.JoystickButtonLabel(key.ToString()))
                : PromptIcons.IconForKey(key);
            // With no pad plugged in, another mod's controller key is shown but can't be changed, like the game's own
            // controller column. Otherwise it looks editable and then does nothing.
            Player player = BindingLoader.GamePlayer;
            bool noPad = column == ControllerColumn && (player == null || player.controllers.joystickCount == 0);
            ShowCell(view, column, setting != null && !noPad, setting == null ? null : ModBindings.Describe(setting) ?? "", icon,
                preview: noPad && setting != null);
        }

        /// <summary>Draws one button: "-" when there's nothing to bind, "Not bound", or the keys, amber if something else uses them.</summary>
        private static void ShowCell(RowView view, int column, bool available, string text, Sprite icon, bool preview = false)
        {
            Button cell = view.Cells[column];
            var image = cell.GetComponent<Image>();
            cell.interactable = available;
            view.Icons[column].sprite = icon;
            view.Icons[column].enabled = icon != null;
            if (preview)
            {
                // Read-only defaults: the game's own look for a button that can't be pressed yet.
                view.Texts[column].text = string.IsNullOrEmpty(text) ? "Not bound" : text;
                image.color = new Color(UiCopy.Unbound.r, UiCopy.Unbound.g, UiCopy.Unbound.b, 0.75f);
            }
            else if (!available)
            {
                view.Texts[column].text = "-";
                image.color = new Color(UiCopy.Unbound.r, UiCopy.Unbound.g, UiCopy.Unbound.b, 0.5f);
            }
            else if (string.IsNullOrEmpty(text))
            {
                view.Texts[column].text = "Not bound";
                image.color = UiCopy.Unbound;
            }
            else
            {
                view.Texts[column].text = text;
                image.color = view.Clashes[column].Count > 0 ? ClashColour : UiCopy.Green;
            }
        }

        /// <summary>Who uses which key: every game binding shown here and every mod key, dev keys included.</summary>
        private ClashIndex BuildClashIndex(Player player)
        {
            var index = new ClashIndex();
            AddDefaultUsers(player, index);
            foreach (RowView view in _rows.Where(v => v.Row != null))
            {
                for (int column = 0; column < 2; column++)
                {
                    ControllerMap map = RewiredBindings.MapFor(player, ControllerFor(player, column), view.Row);
                    foreach (ActionElementMap aem in RewiredBindings.BindingsFor(map, view.Row))
                    {
                        index.Add(GameClashId(aem), view.Row.Label, false);
                    }
                }
            }
            foreach (RowView view in _rows.Where(v => v.IsMouse))
            {
                foreach (ActionElementMap aem in MouseBindings(MouseMap(player), view))
                {
                    index.Add(ClashIndex.KeyboardId(view.MouseKeyName), ActionLabel(aem.actionId, aem.axisContribution == Pole.Positive), false);
                }
            }
            foreach (ModKeySetting setting in _modSettings)
            {
                index.Add(ModBindings.ClashId(setting), BindingRows.Humanize(setting.PluginName) + ": " + ModKeys.Label(setting.Key), true);
            }
            return index;
        }

        /// <summary>
        /// Which game actions share which keys in the game's own defaults, so those intended overlaps aren't flagged.
        /// </summary>
        private void AddDefaultUsers(Player player, ClashIndex index)
        {
            Joystick joystick = player.controllers.Joysticks.FirstOrDefault();
            foreach (RowView view in _rows.Where(v => v.Row != null))
            {
                var defaults = new List<ControllerMap>();
                foreach (InputLayout layout in ReInput.mapping.MapLayouts(ControllerType.Keyboard))
                {
                    defaults.Add(ReInput.mapping.GetKeyboardMapInstance(view.Row.CategoryId, layout.id));
                }
                defaults.Add(joystick != null
                    ? ReInput.mapping.GetJoystickMapInstance(joystick, view.Row.CategoryId, RewiredBindings.ControllerLayout(player, joystick, view.Row.CategoryId))
                    : RewiredBindings.DefaultControllerMap(view.Row.CategoryId).map);
                foreach (ControllerMap map in defaults.Where(m => m != null))
                {
                    foreach (ActionElementMap aem in RewiredBindings.BindingsFor(map, view.Row))
                    {
                        index.AddDefault(GameClashId(aem), view.Row.Label);
                    }
                }
            }
            foreach (RowView view in _rows.Where(v => v.IsMouse))
            {
                foreach (InputLayout layout in ReInput.mapping.MapLayouts(ControllerType.Mouse))
                {
                    ControllerMap map = ReInput.mapping.GetMouseMapInstance(_gameCategory, layout.id);
                    foreach (ActionElementMap aem in MouseBindings(map, view))
                    {
                        index.AddDefault(ClashIndex.KeyboardId(view.MouseKeyName), ActionLabel(aem.actionId, aem.axisContribution == Pole.Positive));
                    }
                }
            }
        }

        private static string GameClashId(ActionElementMap aem)
        {
            ControllerMap map = aem.controllerMap;
            if (map != null && map.controllerType == ControllerType.Keyboard)
            {
                return ClashIndex.KeyboardId(aem.keyCode.ToString(), aem.modifierKeyFlags.ToString().Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries));
            }
            // Mods only see controller buttons, so only buttons can clash with them.
            return aem.elementType == ControllerElementType.Button ? ClashIndex.PadId(PromptNames.ElementLabel(aem.elementIdentifierName)) : null;
        }

        /// <summary>Up/down moves within a column, left/right between the two, and the bottom row leads to Back and Reset.</summary>
        private void WireNavigation()
        {
            Selectable Cell(int row, int column)
            {
                if (row < 0 || row >= _rows.Count)
                {
                    return null;
                }
                Button preferred = _rows[row].Cells[column];
                Button other = _rows[row].Cells[1 - column];
                return preferred.interactable ? preferred : other.interactable ? other : null;
            }

            Selectable Step(int row, int column, int direction)
            {
                for (int r = row + direction; r >= 0 && r < _rows.Count; r += direction)
                {
                    Selectable s = Cell(r, column);
                    if (s != null)
                    {
                        return s;
                    }
                }
                return null;
            }

            for (int row = 0; row < _rows.Count; row++)
            {
                for (int column = 0; column < 2; column++)
                {
                    Button cell = _rows[row].Cells[column];
                    Button other = _rows[row].Cells[1 - column];
                    cell.navigation = new Navigation
                    {
                        mode = Navigation.Mode.Explicit,
                        selectOnUp = Step(row, column, -1),
                        selectOnDown = Step(row, column, 1) ?? (Selectable)_backButton,
                        selectOnLeft = column == ControllerColumn && other.interactable ? other : null,
                        selectOnRight = column == KeyboardColumn && other.interactable ? other : null,
                    };
                }
            }
            Selectable last = Step(_rows.Count, KeyboardColumn, -1);
            if (_backButton != null)
            {
                _backButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = last, selectOnRight = _resetButton };
            }
            if (_resetButton != null)
            {
                _resetButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = last, selectOnLeft = _backButton };
            }
        }

        private void Select(GameObject target)
        {
            if (target != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(target);
                _lastSelected = target;
            }
        }

        private void Update()
        {
            Player player = BindingLoader.GamePlayer;
            if (player == null)
            {
                return;
            }
            if (_listening)
            {
                UpdateListening(player);
                return;
            }
            if (_restoreMapsOnRelease)
            {
                // Give up waiting after a few seconds: a key stuck down or a drifting stick must not leave the game
                // with every input map switched off.
                bool held = AnythingHeld(player) || UnityJoystickButtonHeld();
                if (held && Time.unscaledTime < _restoreMapsAt)
                {
                    return;
                }
                if (held)
                {
                    Plugin.Log.LogWarning("A key or stick still reads as held; restoring the game's input anyway.");
                }
                RestoreMaps();
                _restoreMapsOnRelease = false;
                if (_pendingModSetting != null)
                {
                    ModBindings.Set(_pendingModSetting, _pendingModKey);
                    _pendingModSetting = null;
                    Refresh();
                }
            }

            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            foreach (RowView view in _rows)
            {
                if (view.Arrow != null)
                {
                    view.Arrow.enabled = selected != null && (selected == view.Cells[0].gameObject || selected == view.Cells[1].gameObject);
                }
            }

            if (_modal != null && _modal.activeSelf)
            {
                if (player.GetButtonDown("UICancel") || player.GetButtonDown("Pause"))
                {
                    ModalChoose(_modalCancelIndex);
                }
                return;
            }
            if (player.GetButtonDown("Pause"))
            {
                Close();
                return;
            }

            if (selected != null && selected.transform.IsChildOf(transform))
            {
                _lastSelected = selected;
            }
            else if (selected == null && _lastSelected != null && (player.GetAnyButtonDown()
                || Mathf.Abs(player.GetAxis("UIVertical")) > 0.5f || Mathf.Abs(player.GetAxis("UIHorizontal")) > 0.5f))
            {
                // Clicking empty space with the mouse drops the selection; pick it back up when keys or a pad are used.
                Select(_lastSelected);
            }
        }

        private void LateUpdate()
        {
            if (_scroll == null || _scroll.content == null || _scroll.viewport == null)
            {
                return;
            }
            FollowSelection();
            UpdateStickyStrip();
        }

        /// <summary>
        /// When the selection moves with keys or a controller, scroll just enough to show its row, along with the
        /// section heading above it when it's the first row of a section (and the very top for the first row).
        /// </summary>
        private void FollowSelection()
        {
            RectTransform content = _scroll.content;
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (PeglinUI.ControllerSupport.ControllerInputManager.IsPointerBased)
            {
                // The mouse wheel and scrollbar are in charge; don't pull the list back.
                _scrollGoal = null;
                _scrolledFor = selected;
                return;
            }
            if (selected != _scrolledFor)
            {
                _scrolledFor = selected;
                if (selected != null && selected.transform.parent != null && selected.transform.parent.parent == content)
                {
                    Transform row = selected.transform.parent;
                    int first = row.GetSiblingIndex();
                    while (first > 0 && IsHeadingOrPadding(content.GetChild(first - 1)))
                    {
                        first--;
                    }
                    float rowTop = TopIn(content, content.GetChild(first));
                    float rowBottom = BottomIn(content, row);
                    float viewHeight = _scroll.viewport.rect.height;
                    float y = content.anchoredPosition.y;
                    // Content is pinned at its top, so scrolling down raises its y; a child at local height h is
                    // on screen while -h lies between y and y + viewHeight.
                    float goal = Mathf.Max(y, -rowBottom - viewHeight);
                    goal = Mathf.Min(goal, -rowTop);
                    _scrollGoal = Mathf.Clamp(goal, 0f, Mathf.Max(0f, content.rect.height - viewHeight));
                }
            }
            if (_scrollGoal.HasValue)
            {
                _scroll.StopMovement();
                Vector2 position = content.anchoredPosition;
                position.y = Mathf.Lerp(position.y, _scrollGoal.Value, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                if (Mathf.Abs(position.y - _scrollGoal.Value) < 0.5f)
                {
                    position.y = _scrollGoal.Value;
                    _scrollGoal = null;
                }
                content.anchoredPosition = position;
            }
        }

        private bool IsHeadingOrPadding(Transform t) => t.gameObject.activeSelf && (t.name == "TopPadding" || _headings.Any(h => h.Rect == t));

        private static float TopIn(RectTransform space, Transform child) => Corner(space, child, 1).y;

        private static float BottomIn(RectTransform space, Transform child) => Corner(space, child, 0).y;

        private static readonly Vector3[] Corners = new Vector3[4];

        // GetWorldCorners order: bottom-left, top-left, top-right, bottom-right.
        private static Vector3 Corner(RectTransform space, Transform child, int index)
        {
            ((RectTransform)child).GetWorldCorners(Corners);
            return space.InverseTransformPoint(Corners[index]);
        }

        /// <summary>
        /// Names the section the top of the list is in, once that section's heading has (mostly) scrolled out of
        /// view, and shows that section's column titles.
        /// </summary>
        private void UpdateStickyStrip()
        {
            if (_stickyLabel == null || _headings.Count == 0)
            {
                return;
            }
            RectTransform viewport = _scroll.viewport;
            float viewTop = viewport.rect.yMax;
            Heading current = null;
            foreach (Heading heading in _headings)
            {
                // Headings are in list order; the last one whose middle is above the top edge is the one we're in.
                float middle = (TopIn(viewport, heading.Rect) + BottomIn(viewport, heading.Rect)) / 2f;
                if (middle > viewTop)
                {
                    current = heading;
                }
            }
            string label = current != null ? current.StickyText : "";
            if (_stickyLabel.text != label)
            {
                _stickyLabel.text = label;
            }
            Heading columns = current ?? _headings[0];
            if (_stickyFirstColumn != null && _stickyFirstColumn.text != columns.FirstColumn)
            {
                _stickyFirstColumn.text = columns.FirstColumn;
            }
            if (_controllerHeader != null && _controllerHeader.gameObject.activeSelf != columns.ShowController)
            {
                _controllerHeader.gameObject.SetActive(columns.ShowController);
            }
        }

        private void OnCellClicked(int rowIndex, int column)
        {
            Player player = BindingLoader.GamePlayer;
            if (player == null || _listening || rowIndex < 0 || rowIndex >= _rows.Count)
            {
                return;
            }
            RowView view = _rows[rowIndex];
            if (view.Mod != null)
            {
                OnModCellClicked(view, column);
                return;
            }
            if (view.IsMouse)
            {
                OnMouseCellClicked(player, view);
                return;
            }
            Controller controller = ControllerFor(player, column);
            ControllerMap map = RewiredBindings.MapFor(player, controller, view.Row);
            if (map == null)
            {
                return;
            }
            bool bound = RewiredBindings.BindingsFor(map, view.Row).Count > 0;
            // Replace swaps all of this row's keys for one; Add keeps them and adds another (the game gives Fire three).
            string[] options = bound ? new[] { "Replace", "Add", "Default", "Unbind", "Cancel" } : new[] { "Set", "Default", "Cancel" };
            ShowModal(ChoiceText(view, column), options, options.Length - 1, choice =>
            {
                string picked = choice >= 0 && choice < options.Length ? options[choice] : "Cancel";
                switch (picked)
                {
                    case "Replace":
                    case "Set":
                        StartListening(controller, view, column);
                        return;
                    case "Add":
                        StartListening(controller, view, column, add: true);
                        return;
                    case "Default":
                        RewiredBindings.RestoreDefault(player, controller, map, view.Row);
                        Changed(player, controller);
                        break;
                    case "Unbind":
                        RewiredBindings.Clear(map, view.Row);
                        Changed(player, controller);
                        break;
                }
                Select(view.Cells[column].gameObject);
            });
        }

        /// <summary>
        /// Mouse buttons have no key to press, so instead you browse the game's actions (Previous / Next) and assign
        /// one to the button. The pop-up stays open while browsing.
        /// </summary>
        private void OnMouseCellClicked(Player player, RowView view)
        {
            ControllerMap map = MouseMap(player);
            var candidates = _rows.Where(r => r.Row != null).ToList();
            if (map == null || candidates.Count == 0)
            {
                return;
            }
            ActionElementMap current = MouseBindings(map, view).FirstOrDefault();
            int start = current == null ? 0 : Math.Max(0, candidates.FindIndex(r => r.Row.ActionId == current.actionId
                && (!r.Row.IsAxis || r.Row.Positive == (current.axisContribution == Pole.Positive))));
            ShowMousePicker(player, view, map, candidates, start, 0);
        }

        private void ShowMousePicker(Player player, RowView view, ControllerMap map, List<RowView> candidates, int index, int focus)
        {
            BindingRow choice = candidates[index].Row;
            var text = new System.Text.StringBuilder(view.Label);
            text.Append("\n<size=70%>Now: ").Append(view.Texts[KeyboardColumn].text).Append("</size>");
            text.Append("\n<size=90%>").Append(index + 1).Append("/").Append(candidates.Count).Append("  ").Append(choice.Label).Append("</size>");
            if (view.MouseKeyName == "Mouse0")
            {
                text.Append("\n<size=60%>Left click still clicks and aims as usual.</size>");
            }
            string[] options = { "Previous", "Next", "Assign", "Unbind", "Cancel" };
            ShowModal(text.ToString(), options, options.Length - 1, picked =>
            {
                switch (picked)
                {
                    case 0:
                        ShowMousePicker(player, view, map, candidates, (index - 1 + candidates.Count) % candidates.Count, 0);
                        return;
                    case 1:
                        ShowMousePicker(player, view, map, candidates, (index + 1) % candidates.Count, 1);
                        return;
                    case 2:
                        ClearMouseButton(map, view);
                        map.CreateElementMap(ElementAssignment.CompleteAssignment(ControllerType.Mouse, ControllerElementType.Button, view.MouseElement,
                            AxisRange.Positive, KeyCode.None, ModifierKeyFlags.None, choice.ActionId, choice.Positive ? Pole.Positive : Pole.Negative, false));
                        Plugin.Log.LogInfo($"{view.Label} now does {choice.Label}.");
                        Changed(player, ReInput.controllers.Mouse);
                        break;
                    case 3:
                        ClearMouseButton(map, view);
                        Changed(player, ReInput.controllers.Mouse);
                        break;
                }
                Select(view.Cells[KeyboardColumn].gameObject);
            }, focus);
        }

        private void ClearMouseButton(ControllerMap map, RowView view)
        {
            foreach (ActionElementMap aem in MouseBindings(map, view))
            {
                map.DeleteElementMap(aem.id);
            }
        }

        private void OnModCellClicked(RowView view, int column)
        {
            ModKeySetting setting = view.ModSetting(column);
            if (setting == null)
            {
                return;
            }
            // A mod setting holds one key, so there's nothing to add to.
            bool bound = ModBindings.Current(setting) != KeyCode.None;
            string[] options = bound ? new[] { "Change", "Default", "Unbind", "Cancel" } : new[] { "Set", "Default", "Cancel" };
            ShowModal(ChoiceText(view, column), options, options.Length - 1, choice =>
            {
                string picked = choice >= 0 && choice < options.Length ? options[choice] : "Cancel";
                switch (picked)
                {
                    case "Change":
                    case "Set":
                        StartListening(column == KeyboardColumn ? ReInput.controllers.Keyboard : null, view, column);
                        return;
                    case "Default":
                        ModBindings.ResetToDefault(setting);
                        Refresh();
                        break;
                    case "Unbind":
                        ModBindings.Set(setting, KeyCode.None);
                        Refresh();
                        break;
                }
                Select(view.Cells[column].gameObject);
            });
        }

        /// <summary>The pop-up text for a binding: its name, what a mod key is for, the current keys, and any clash.</summary>
        private static string ChoiceText(RowView view, int column)
        {
            var text = new System.Text.StringBuilder(view.Label);
            if (view.Mod != null && view.Mod.Summary.Length > 0)
            {
                text.Append("\n<size=60%>").Append(view.Mod.Summary).Append("</size>");
            }
            text.Append("\n<size=80%>").Append(view.Texts[column].text).Append("</size>");
            if (view.Clashes[column].Count > 0)
            {
                text.Append("\n<size=65%><color=#F2B04A>Also used by ").Append(string.Join(", ", view.Clashes[column])).Append("</color></size>");
            }
            return text.ToString();
        }

        private void AskReset()
        {
            Player player = BindingLoader.GamePlayer;
            if (player == null)
            {
                return;
            }
            bool hasController = ControllerFor(player, ControllerColumn) != null || _rows.Any(r => r.Mod?.Controller != null);
            var options = new List<string> { "Keyboard" };
            if (hasController)
            {
                options.Add("Controller");
            }
            if (_rows.Any(r => r.IsMouse))
            {
                options.Add("Mouse");
            }
            options.Add("Cancel");
            ShowModal("Reset bindings to their defaults?\n<size=70%>Includes the mod keys in that column.</size>", options.ToArray(), options.Count - 1, picked =>
            {
                string choice = picked >= 0 && picked < options.Count ? options[picked] : "Cancel";
                if (choice == "Keyboard")
                {
                    player.controllers.maps.LoadDefaultMaps(ControllerType.Keyboard);
                    Plugin.Store.ForgetAll(ControllerType.Keyboard);
                    ResetModKeys(KeyboardColumn);
                    Changed(player, null);
                }
                else if (choice == "Mouse")
                {
                    player.controllers.maps.LoadDefaultMaps(ControllerType.Mouse);
                    Plugin.Store.ForgetAll(ControllerType.Mouse);
                    Changed(player, null);
                }
                else if (choice == "Controller")
                {
                    player.controllers.maps.LoadDefaultMaps(ControllerType.Joystick);
                    // Every saved model, not just the pads plugged in now, or old bindings come back on reconnect.
                    Plugin.Store.ForgetAll(ControllerType.Joystick);
                    ResetModKeys(ControllerColumn);
                    Changed(player, null);
                }
                Select(_resetButton != null ? _resetButton.gameObject : null);
            });
        }

        private void ResetModKeys(int column)
        {
            foreach (RowView view in _rows)
            {
                ModKeySetting setting = view.ModSetting(column);
                if (setting != null)
                {
                    ModBindings.ResetToDefault(setting);
                }
            }
        }

        /// <summary>After a change: save it, and update this screen and every button prompt in the game.</summary>
        private void Changed(Player player, Controller controller)
        {
            if (controller != null)
            {
                Plugin.Store.Save(player, controller);
                RewiredBindings.ForgetDefaultControllerMaps();
            }
            Refresh();
            PromptIcons.RefreshAll();
        }

        private void ShowModal(string text, string[] options, int cancelIndex, Action<int> callback, int selected = 0)
        {
            if (_modal == null)
            {
                callback(cancelIndex);
                return;
            }
            // Confirm-style options go on green buttons, the way out on the red one. A lone option (the listening
            // pop-up's Cancel) stays green, as the game's own single-button pop-ups are.
            var greens = new List<Button> { _yesButton, _okayButton };
            greens.AddRange(_extraModalButtons);
            int greenCount = options.Length == 1 ? 1 : options.Length - 1;
            while (greens.Count < greenCount && _yesButton != null)
            {
                // The game's pop-up has two green buttons; make more from Yes when a choice needs them.
                Button extra = Instantiate(_yesButton.gameObject, _yesButton.transform.parent, false).GetComponent<Button>();
                extra.onClick = new Button.ButtonClickedEvent();
                extra.onClick.AddListener(() => ModalChoose(Array.IndexOf(_modalButtons, extra)));
                _extraModalButtons.Add(extra);
                greens.Add(extra);
            }
            var order = new Button[options.Length];
            int nextGreen = 0;
            for (int i = 0; i < options.Length; i++)
            {
                order[i] = options.Length > 1 && i == cancelIndex ? _noButton : greens[nextGreen++];
            }
            _modalButtons = order;
            _modalCancelIndex = cancelIndex;
            _modalCallback = callback;
            if (_modalText != null)
            {
                _modalText.text = text;
            }
            foreach (Button b in greens.Concat(new[] { _noButton }))
            {
                if (b != null)
                {
                    b.gameObject.SetActive(order.Contains(b));
                }
            }
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == null)
                {
                    continue;
                }
                order[i].transform.SetSiblingIndex(i);
                foreach (TMP_Text label in order[i].GetComponentsInChildren<TMP_Text>(true))
                {
                    label.text = options[i];
                }
                order[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? order[i - 1] : null,
                    selectOnRight = i < order.Length - 1 ? order[i + 1] : null,
                };
            }
            SetBottomTriggers(false);
            _modal.SetActive(true);
            Button focus = order[Mathf.Clamp(selected, 0, order.Length - 1)];
            Select(focus != null ? focus.gameObject : null);
        }

        private void ModalChoose(int index)
        {
            if (_modal == null || !_modal.activeSelf)
            {
                return;
            }
            Action<int> callback = _modalCallback;
            _modalCallback = null;
            _modal.SetActive(false);
            StartCoroutine(EnableBottomTriggersSoon());
            callback?.Invoke(index < 0 ? _modalCancelIndex : index);
        }

        private void StartListening(Controller controller, RowView view, int column, bool add = false)
        {
            Player player = BindingLoader.GamePlayer;
            // Mod controller keys are listened for through Unity's own joystick buttons, so they need no Rewired controller.
            if (player == null || (controller == null && view.ModSetting(column) == null))
            {
                return;
            }
            _listenController = controller;
            _listenRow = view;
            _listenColumn = column;
            _listenAdd = add;
            _listenTimeLeft = Mathf.Max(1f, Plugin.ListenSeconds.Value);
            ShowModal(ListenText(), new[] { "Cancel" }, 0, _ => FinishListening());
            // A restore still waiting from the last listen must happen first, or this snapshot would record every
            // map as off and the game's input would never come back.
            if (_restoreMapsOnRelease)
            {
                RestoreMaps();
                _restoreMapsOnRelease = false;
            }
            // Switch the game's input off while listening, so the key pressed only sets the binding.
            SnapshotAndDisableMaps(player);
            _listening = true;
            _waitForRelease = true;
        }

        private string ListenText()
        {
            string what = _listenColumn == KeyboardColumn ? "a key" : "a button";
            return $"Press {what} {(_listenAdd ? "to add " : "")}for\n{_listenRow?.Label}\n<size=70%>Cancels in {Mathf.CeilToInt(Mathf.Max(0f, _listenTimeLeft))}s</size>";
        }

        /// <summary>The first of Unity's joystick buttons pressed this frame, as mods see them (JoystickButton0-19).</summary>
        private static KeyCode UnityJoystickButtonDown()
        {
            for (int i = 0; i < 20; i++)
            {
                KeyCode key = KeyCode.JoystickButton0 + i;
                if (Input.GetKeyDown(key))
                {
                    return key;
                }
            }
            return KeyCode.None;
        }

        private static bool UnityJoystickButtonHeld()
        {
            for (int i = 0; i < 20; i++)
            {
                if (Input.GetKey(KeyCode.JoystickButton0 + i))
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateListening(Player player)
        {
            _listenTimeLeft -= Time.unscaledDeltaTime;
            if (_modalText != null)
            {
                _modalText.text = ListenText();
            }
            ModKeySetting modSetting = _listenRow?.ModSetting(_listenColumn);
            if (_listenTimeLeft <= 0f || (modSetting == null && (_listenController == null || !_listenController.isConnected)))
            {
                FinishListening();
                return;
            }
            // Ignore the press that picked the binding: wait for everything to be let go first.
            if (_waitForRelease)
            {
                _waitForRelease = AnythingHeld(player) || UnityJoystickButtonHeld();
                return;
            }
            // Rewired reads the keyboard even when the game isn't the focused window, so typing in another program
            // would otherwise become a binding.
            if (!Application.isFocused)
            {
                return;
            }
            if (modSetting != null)
            {
                KeyCode key = KeyCode.None;
                if (_listenColumn == ControllerColumn)
                {
                    key = UnityJoystickButtonDown();
                }
                else
                {
                    ControllerPollingInfo keyPoll = ReInput.controllers.polling.PollControllerForFirstElementDown(ControllerType.Keyboard, 0);
                    key = keyPoll.success ? keyPoll.keyboardKey : KeyCode.None;
                }
                if (key == KeyCode.None)
                {
                    return;
                }
                // Other mods read keys straight from Unity, so setting it now would also trigger the mod this frame
                // (a restart key restarting the run). Apply it once the key is let go.
                _pendingModSetting = modSetting;
                _pendingModKey = key;
                FinishListening();
                return;
            }
            ControllerPollingInfo polled = ReInput.controllers.polling.PollControllerForFirstElementDown(_listenController.type, _listenController.id);
            if (!polled.success)
            {
                return;
            }
            ControllerMap map = RewiredBindings.MapFor(player, _listenController, _listenRow.Row);
            if (map != null)
            {
                RewiredBindings.Assign(map, _listenRow.Row, polled, _listenAdd);
                Changed(player, _listenController);
            }
            FinishListening();
        }

        private void FinishListening()
        {
            if (!_listening)
            {
                return;
            }
            _listening = false;
            // Keep the game's input off until the new key is let go, or it would also do whatever it's now bound to.
            // Set before Refresh(): if that throws, CleanUp must still put the maps back.
            _restoreMapsOnRelease = true;
            _restoreMapsAt = Time.unscaledTime + 3f;
            // A controller may have come or gone while listening; catch the screen up.
            Refresh();
            if (_modal != null && _modal.activeSelf)
            {
                _modalCallback = null;
                _modal.SetActive(false);
                StartCoroutine(EnableBottomTriggersSoon());
            }
            if (_listenRow != null)
            {
                Select(_listenRow.Cells[_listenColumn].gameObject);
            }
        }

        private static bool AnythingHeld(Player player)
        {
            var polling = ReInput.controllers.polling;
            if (polling.PollControllerForFirstElement(ControllerType.Keyboard, 0).success
                || polling.PollControllerForFirstElement(ControllerType.Mouse, 0).success)
            {
                return true;
            }
            foreach (Joystick joystick in player.controllers.Joysticks)
            {
                if (polling.PollControllerForFirstElement(ControllerType.Joystick, joystick.id).success)
                {
                    return true;
                }
            }
            return false;
        }

        private void SnapshotAndDisableMaps(Player player)
        {
            _mapStates.Clear();
            foreach (ControllerMap map in AllMaps(player))
            {
                _mapStates[(map.controllerType, map.controllerId, map.categoryId, map.layoutId)] = map.enabled;
                map.enabled = false;
            }
        }

        private void RestoreMaps()
        {
            Player player = BindingLoader.GamePlayer;
            if (player == null)
            {
                return;
            }
            foreach (ControllerMap map in AllMaps(player))
            {
                // A map that appeared meanwhile (a controller plugged in) starts enabled, as Rewired would have it.
                map.enabled = !_mapStates.TryGetValue((map.controllerType, map.controllerId, map.categoryId, map.layoutId), out bool enabled) || enabled;
            }
            _mapStates.Clear();
        }

        private static IEnumerable<ControllerMap> AllMaps(Player player)
        {
            foreach (ControllerType type in new[] { ControllerType.Keyboard, ControllerType.Mouse, ControllerType.Joystick, ControllerType.Custom })
            {
                foreach (ControllerMap map in player.controllers.maps.GetAllMaps(type))
                {
                    yield return map;
                }
            }
        }
    }
}
