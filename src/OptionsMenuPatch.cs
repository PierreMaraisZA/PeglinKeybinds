using System;
using HarmonyLib;
using PeglinUI.SettingsMenu;
using PeglinUI.SettingsMenu.Options;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeglinKeybinds
{
    /// <summary>
    /// Adds a "Controls" row at the top of the game's Options list. It's a copy of the game's own Delete Save Data
    /// row (a label with a button), so it looks and behaves like the rows around it: selected as a row, highlighted
    /// with the game's own arrow, and opened with the Accept key the same way the Delete row handles that key.
    /// </summary>
    [HarmonyPatch(typeof(OptionsMenu), "OnEnable")]
    internal static class OptionsMenuPatch
    {
        public const string RowName = "PeglinKeybinds_ControlsRow";

        // Runs before OnEnable so the game's own "select the first option" picks our row.
        private static void Prefix(OptionsMenu __instance)
        {
            try
            {
                GameObject row = FindOrAddRow(__instance);
                if (row != null)
                {
                    // The row itself, not the button inside it: that's what the game selects, highlights and
                    // navigates between, and what the Accept-key swap below is tied to.
                    Selectable selectable = row.GetComponent<Selectable>() ?? row.GetComponentInChildren<Selectable>(true);
                    __instance.firstSelectable = selectable != null ? selectable.gameObject : row;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't add the Controls row to the Options menu: {e}");
            }
        }

        private static GameObject FindOrAddRow(OptionsMenu menu)
        {
            Selectable template = menu.deleteButtonSelectable;
            if (template == null || template.transform.parent == null)
            {
                Plugin.Log.LogWarning("Options menu layout not recognised (no Delete Save Data row); the Controls row wasn't added.");
                return null;
            }
            Transform contents = template.transform.parent;
            Transform existing = contents.Find(RowName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            // Copy under an inactive parent so none of the copy's scripts wake up before it's been adjusted.
            var holder = new GameObject("PeglinKeybinds_Holder");
            holder.SetActive(false);
            GameObject row = UnityEngine.Object.Instantiate(template.gameObject, holder.transform, false);
            row.name = RowName;
            UiCopy.RemoveLocalisation(row);

            Selectable rowSelectable = row.GetComponent<Selectable>();
            Button button = row.GetComponentInChildren<Button>(true);
            if (rowSelectable == null || button == null)
            {
                UnityEngine.Object.Destroy(holder);
                Plugin.Log.LogWarning("Options menu's Delete Save Data row isn't laid out as expected; the Controls row wasn't added.");
                return null;
            }

            // The game's Delete row opens with Accept by swapping its own key trigger in for the Accept button
            // while the row is selected. Do the same for ours (RowAcceptSwap), but as our own component: the game's
            // OverrideButtonMapWhenSelected would fight PeglinFonts' row, which swaps the same Accept button, and
            // whichever updated last would win. The trigger stays off until the row is selected.
            RewiredUIButtonTrigger keyTrigger = button.GetComponent<RewiredUIButtonTrigger>();
            OverrideButtonMapWhenSelected copiedSwap = row.GetComponent<OverrideButtonMapWhenSelected>();
            bool enterOpens = keyTrigger != null && menu.acceptButton != null;
            if (enterOpens)
            {
                UiCopy.RemoveScripts(row, "OverrideButtonMapWhenSelected");
                keyTrigger.enabled = false;
                keyTrigger.buttonName = "UISubmit";
                keyTrigger.simulateButtonClick = true;
                var swap = row.AddComponent<RowAcceptSwap>();
                swap.Menu = menu;
                swap.KeyTrigger = keyTrigger;
                swap.Accept = menu.acceptButton;
                // The badge swapped in with the key: the ENTER prompt next to Configure replaces the bottom Accept
                // button's while this row is selected, exactly as the Delete Save Data row does. Its icon follows
                // the Accept binding through the key trigger above.
                swap.Badge = copiedSwap != null ? copiedSwap.buttonGraphicMapperToEnable : row.GetComponentInChildren<ButtonGraphicMapper>(true);
                swap.AcceptBadge = copiedSwap != null ? copiedSwap.buttonGraphicMapperToDisable : null;
                if (swap.Badge == null)
                {
                    Plugin.Log.LogWarning("Options menu's Delete Save Data row has no key prompt to copy; the Controls row will have no Accept badge.");
                }
            }
            else
            {
                if (keyTrigger != null)
                {
                    keyTrigger.enabled = false;
                }
                Plugin.Log.LogWarning("Options menu's Accept button couldn't be swapped for the Controls row; Enter won't open it.");
            }

            // Other mods add rows to this list too (PeglinFonts has two), so the order, the up/down links and the
            // wrap-around are finished once the list has settled.
            row.AddComponent<OptionsRowFixup>();
            // A new event drops the delete button's own listeners, including ones set up in the editor.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => ControlsScreen.Open(menu, row));
            if (button.TryGetComponent(out Image buttonImage))
            {
                buttonImage.color = UiCopy.Green;
            }
            foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text = "Configure";
            }
            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!text.transform.IsChildOf(button.transform))
                {
                    text.text = "Controls";
                }
            }

            row.transform.SetParent(contents, false);
            int index = contents.childCount > 0 && contents.GetChild(0).GetComponent<Selectable>() == null ? 1 : 0;
            row.transform.SetSiblingIndex(index);
            UnityEngine.Object.Destroy(holder);

            Plugin.Log.LogInfo($"Controls row ready: index={index}, enter={(enterOpens ? "opens the screen" : "doesn't open the screen")}");
            return row;
        }

        internal static Selectable FirstBelow(Transform contents, int index)
        {
            for (int i = index + 1; i < contents.childCount; i++)
            {
                Transform child = contents.GetChild(i);
                Selectable candidate = child.GetComponent<Selectable>() ?? child.GetComponentInChildren<Selectable>(true);
                if (candidate != null && candidate.gameObject.activeInHierarchy)
                {
                    return candidate;
                }
            }
            return null;
        }

        internal static Selectable LastSelectable(Transform contents)
        {
            for (int i = contents.childCount - 1; i >= 0; i--)
            {
                Transform child = contents.GetChild(i);
                Selectable candidate = child.GetComponent<Selectable>() ?? child.GetComponentInChildren<Selectable>(true);
                if (candidate != null && candidate.gameObject.activeInHierarchy)
                {
                    return candidate;
                }
            }
            return null;
        }
    }

    /// <summary>While the controls screen is open, stop the Options and pause menus reacting to Esc (it closes our screen instead).</summary>
    [HarmonyPatch(typeof(OptionsMenu), "Update")]
    internal static class OptionsMenuUpdatePatch
    {
        private static bool Prefix() => !ControlsScreen.BlocksGameMenus;

        /// <summary>
        /// The game closes Options with the Pause key only where Options isn't itself the pause panel; on the main
        /// menu only Back (UI Cancel) closes it. Make the Pause key (Esc by default, or whatever it's bound to) close
        /// it there too, the way Back does.
        /// </summary>
        private static void Postfix(OptionsMenu __instance)
        {
            Rewired.Player player = BindingLoader.GamePlayer;
            // Not while Controls is open or just closed, nor while a pop-up has switched Options' buttons off.
            if (player == null || ControlsScreen.BlocksGameMenus || !__instance.unpauseOnExit
                || __instance.cancelButton == null || !__instance.cancelButton.enabled || !__instance.cancelButton.isActiveAndEnabled)
            {
                return;
            }
            // When Pause and Back share a key, the game's Back button already handles the press.
            if (player.GetButtonDown("Pause") && !player.GetButtonDown("UICancel"))
            {
                __instance.CancelOptions();
            }
        }
    }

    [HarmonyPatch(typeof(PauseMenu), "Update")]
    internal static class PauseMenuUpdatePatch
    {
        private static bool Prefix() => !ControlsScreen.BlocksGameMenus;
    }

    /// <summary>
    /// Keeps our row first in the Options list, keeps the selection on it when the menu opens, and finishes the
    /// list's navigation: Up from our row wraps to the last row, Down from the last row wraps back to ours, and the
    /// scene's old wrap-around links (which point at Language, the row that used to be first) are repointed at ours.
    /// </summary>
    internal sealed class OptionsRowFixup : MonoBehaviour
    {
        private void OnEnable()
        {
            StartCoroutine(Fix());
        }

        private void OnDisable()
        {
            // The menu closed mid-fix: stop the coroutine and let the next open start a fresh one.
            StopAllCoroutines();
        }

        private System.Collections.IEnumerator Fix()
        {
            // Two frames: long enough for every other Options patch to have added its rows for this open.
            yield return null;
            yield return null;

            Transform contents = transform.parent;
            if (contents == null)
            {
                yield break;
            }
            int first = 0;
            while (first < contents.childCount && contents.GetChild(first) != transform
                && Selectable(contents.GetChild(first)) == null)
            {
                first++;
            }
            if (first < contents.childCount && transform.GetSiblingIndex() != first)
            {
                transform.SetSiblingIndex(first);
            }

            Selectable ours = GetComponent<Selectable>();
            if (ours == null)
            {
                yield break;
            }
            Selectable below = OptionsMenuPatch.FirstBelow(contents, transform.GetSiblingIndex());
            Selectable bottom = OptionsMenuPatch.LastSelectable(contents);
            int repaired = 0;

            if (below != null)
            {
                Navigation nav = ours.navigation;
                nav.mode = Navigation.Mode.Explicit;
                // Top of the list: Up wraps to the last row, as it does in the game's other lists.
                nav.selectOnUp = bottom != null && bottom != ours && bottom != below ? bottom : null;
                nav.selectOnDown = below;
                ours.navigation = nav;
                if (below.navigation.mode == Navigation.Mode.Explicit)
                {
                    Navigation belowNav = below.navigation;
                    belowNav.selectOnUp = ours;
                    below.navigation = belowNav;
                }

                // The scene's wrap links still lead back to the old first row (Language instead of Controls). Any
                // link that jumps over the end of the list now goes to our row, at whichever end it belongs.
                for (int i = 0; i < contents.childCount; i++)
                {
                    Transform child = contents.GetChild(i);
                    Selectable row = Selectable(child);
                    if (row == null || row == ours || row.navigation.mode != Navigation.Mode.Explicit)
                    {
                        continue;
                    }
                    Navigation rowNav = row.navigation;
                    bool changed = false;
                    if (Wraps(child, rowNav.selectOnDown, down: true))
                    {
                        rowNav.selectOnDown = ours;
                        changed = true;
                    }
                    if (Wraps(child, rowNav.selectOnUp, down: false))
                    {
                        rowNav.selectOnUp = ours;
                        changed = true;
                    }
                    if (changed)
                    {
                        row.navigation = rowNav;
                        repaired++;
                    }
                }
            }

            // The game selects this row itself when the menu opens; only step in if nothing in the list has the
            // selection, and never while the Controls screen (which is not part of this list) is open.
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (isActiveAndEnabled && ControlsScreen.Current == null
                && (selected == null || !selected.transform.IsChildOf(contents)))
            {
                ours.Select();
            }
            Plugin.Log.LogInfo($"Options row: first={transform.GetSiblingIndex()}, " +
                $"up={Name(ours.navigation.selectOnUp)}, down={Name(ours.navigation.selectOnDown)}, wrap links fixed={repaired}");
        }

        /// <summary>True when a link jumps over the end of the list: Down pointing above the row, or Up pointing below it.</summary>
        private static bool Wraps(Transform row, Selectable target, bool down)
        {
            if (target == null || target.transform.parent != row.parent)
            {
                return false;
            }
            int targetIndex = target.transform.GetSiblingIndex();
            int rowIndex = row.GetSiblingIndex();
            return down ? targetIndex < rowIndex : targetIndex > rowIndex;
        }

        private static Selectable Selectable(Transform t) => t.GetComponent<Selectable>() ?? t.GetComponentInChildren<Selectable>(true);

        private static string Name(Selectable s) => s == null ? "-" : s.name;
    }

    /// <summary>
    /// Puts the Controls row's own key trigger and badge in place of the Options menu's Accept button while the row
    /// is selected, the way the game does for its Delete Save Data row. It re-applies that state every frame, so
    /// another row's swap (the Delete row, or PeglinFonts' Font row) can't leave the Accept button half-swapped,
    /// and it leaves the Accept button to those rows when one of them takes over.
    /// </summary>
    internal sealed class RowAcceptSwap : MonoBehaviour
    {
        public OptionsMenu Menu;
        public RewiredUIButtonTrigger KeyTrigger;
        public RewiredUIButtonTrigger Accept;
        public ButtonGraphicMapper Badge;
        public ButtonGraphicMapper AcceptBadge;

        private bool _applied;

        /// <summary>Hands the Accept button and this row's key trigger to the Controls screen while it is open.</summary>
        public void HandOver()
        {
            Enable(KeyTrigger, false);
        }

        private void Update()
        {
            // The Controls screen owns the Accept button and the selection while it is open, and for the couple of
            // frames its closing press takes to settle.
            if (ControlsScreen.BlocksGameMenus)
            {
                Enable(KeyTrigger, false);
                return;
            }
            // A pop-up (the delete confirmation) has switched Options' buttons off: it owns the Accept button until
            // the menu switches them back on. Match the game's own swap, which is disabled for the same reason.
            if (Menu != null && Menu.cancelButton != null && !Menu.cancelButton.enabled)
            {
                Enable(KeyTrigger, false);
                Show(Badge, false);
                _applied = false;
                return;
            }
            if (IsSelected())
            {
                Apply();
                _applied = true;
            }
            else if (_applied)
            {
                _applied = false;
                // Our own trigger and badge are always ours to put back; the Accept button is shared with other
                // rows, so only take it back when none of them is taking over.
                Enable(KeyTrigger, false);
                Show(Badge, false);
                if (!AnotherSwapOwns(Selection()))
                {
                    Enable(Accept, true);
                    Show(AcceptBadge, true);
                }
            }
        }

        private void Apply()
        {
            Enable(KeyTrigger, true);
            Enable(Accept, false);
            Show(Badge, true);
            Show(AcceptBadge, false);
        }

        private bool IsSelected()
        {
            GameObject selected = Selection();
            return selected != null && (selected == gameObject || selected.transform.IsChildOf(transform));
        }

        private static void Enable(Behaviour behaviour, bool on)
        {
            if (behaviour != null && behaviour.enabled != on)
            {
                behaviour.enabled = on;
            }
        }

        private static void Show(ButtonGraphicMapper mapper, bool on)
        {
            if (mapper != null && mapper.gameObject.activeSelf != on)
            {
                mapper.gameObject.SetActive(on);
            }
        }

        private static GameObject Selection() => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

        /// <summary>Whether the newly selected object is a row that runs the same swap itself.</summary>
        private static bool AnotherSwapOwns(GameObject selected)
        {
            if (selected == null)
            {
                return false;
            }
            OverrideButtonMapWhenSelected other = selected.GetComponent<OverrideButtonMapWhenSelected>();
            return other != null && other.enabled && other.mapperToEnable != null;
        }
    }
}
