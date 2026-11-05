using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using PeglinUI.SettingsMenu;
using PeglinUI.SettingsMenu.Options;
using Rewired;
using TMPro;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeglinKeybinds
{
    // TEMPORARY development aid: dumps the Options menu hierarchy and prompt icon assets, then quits. Only runs when
    // BepInEx/config/PeglinKeybinds.dump exists.
    internal class DevDump : MonoBehaviour
    {
        private bool _done;

        private void Awake()
        {
            SceneManager.sceneLoaded += (scene, _) =>
            {
                if (!_done && scene.name == "MainMenu")
                {
                    _done = true;
                    StartCoroutine(Run());
                }
            };
        }

        private static string Out(string name) => System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "PeglinKeybinds_test", name);

        /// <summary>Loads each other scene additively to find prompt icons and anything showing a key sprite.</summary>
        private IEnumerator SceneSurvey()
        {
            Directory.CreateDirectory(Out(""));
            var log = new StringBuilder();
            yield return new WaitForSecondsRealtime(5f);
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
                log.AppendLine($"=== scene {i} {path}");
                if (path.Contains("MainMenu"))
                {
                    continue;
                }
                AsyncOperation op = null;
                try { op = SceneManager.LoadSceneAsync(i, LoadSceneMode.Additive); } catch (System.Exception e) { log.AppendLine("load failed " + e.Message); }
                if (op == null) continue;
                float t = 0f;
                while (!op.isDone && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
                yield return new WaitForSecondsRealtime(1f);
                Scene scene = SceneManager.GetSceneByBuildIndex(i);
                try
                {
                    foreach (var g in Resources.FindObjectsOfTypeAll<UI.ButtonGraphicMapper>().Where(g => g.gameObject.scene == scene))
                        log.AppendLine($"mapper {Path(g.transform)} -> {N(g._controllerIconMapping)}");
                    foreach (var img in Resources.FindObjectsOfTypeAll<Image>().Where(m => m.gameObject.scene == scene && m.sprite != null
                        && (m.sprite.name.StartsWith("keyboard_") || m.sprite.name.StartsWith("xbox") || m.sprite.name.StartsWith("ps4_") || m.sprite.name.StartsWith("switch_"))))
                        log.AppendLine($"image {Path(img.transform)} sprite={img.sprite.name} comps={string.Join(",", img.GetComponents<Component>().Select(c => c.GetType().Name))}");
                    foreach (var s in Resources.FindObjectsOfTypeAll<PeglinUI.SpeedupIndicator>().Where(m => m.gameObject.scene == scene))
                    {
                        log.AppendLine($"speedup {Path(s.transform)} notPressed={N(s.SpeedupNotPressed)} pressed={N(s.SpeedupPressed)}");
                        Dump(s.transform.parent != null ? s.transform.parent : s.transform, 0, log);
                    }
                }
                catch (System.Exception e) { log.AppendLine("survey failed " + e.Message); }
                var unload = SceneManager.UnloadSceneAsync(scene);
                t = 0f;
                while (unload != null && !unload.isDone && t < 10f) { t += Time.unscaledDeltaTime; yield return null; }
            }
            log.AppendLine("=== icon sets now loaded");
            foreach (var m in Resources.FindObjectsOfTypeAll<ControllerIconMapping>().OrderBy(m => m.name))
                log.AppendLine($"{m.name}: kb={N(m.keyboardIcon)} xbox={N(m.xboxIcon)}");
            File.WriteAllText(Out("log.txt"), log.ToString());
            Application.Quit();
        }

        private IEnumerator UiTest()
        {
            // The test opens real pop-ups; keep the player's saved bindings safe whatever it presses.
            string saved = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PeglinKeybinds.bindings");
            string backup = saved + ".testbackup";
            if (File.Exists(saved)) File.Copy(saved, backup, true);
            Directory.CreateDirectory(Out(""));
            var log = new StringBuilder();
            yield return new WaitForSecondsRealtime(6f);
            OptionsMenu menu = Resources.FindObjectsOfTypeAll<OptionsMenu>().FirstOrDefault(o => o.gameObject.scene.IsValid());
            // Hotkey path first: opens Options and Controls from the plain main menu.
            yield return ControlsScreen.OpenFromAnywhere();
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(Out("0_hotkey.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            log.AppendLine("hotkey opened screen: " + (ControlsScreen.Current != null) + " options active: " + menu.isActiveAndEnabled);
            if (ControlsScreen.Current != null) Traverse.Create(ControlsScreen.Current).Method("Close").GetValue();
            yield return new WaitForSecondsRealtime(0.5f);
            menu.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(Out("1_options.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            GameObject row = menu.transform.GetComponentsInChildren<Transform>(true).First(t => t.name == OptionsMenuPatch.RowName).gameObject;
            log.AppendLine("selected after open: " + UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject?.name);
            ControlsScreen.Open(menu, row);
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(Out("2_controls.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            log.AppendLine("selected in screen: " + UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject?.name);
            // Sticky strip and follow-the-selection scrolling, as a controller would move it: a row deep in the
            // list, then the first row of a later section, then back to the very first row.
            var stickyRows = Traverse.Create(ControlsScreen.Current).Field("_rows").GetValue<System.Collections.IList>();
            foreach (int pick in new[] { stickyRows.Count / 3, stickyRows.Count - 1, 0 })
            {
                var cell = Traverse.Create(stickyRows[pick]).Field("Cells").GetValue<Button[]>()[0];
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(cell.gameObject);
                yield return new WaitForSecondsRealtime(1f);
                string sticky = Traverse.Create(ControlsScreen.Current).Field("_stickyLabel").GetValue<TMP_Text>()?.text;
                log.AppendLine($"sticky after selecting row {pick} ({Traverse.Create(stickyRows[pick]).Property("Label").GetValue<string>()}): '{sticky}'");
                ScreenCapture.CaptureScreenshot(Out($"2s_sticky_row{pick}.png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
            var screenT = ControlsScreen.Current.transform;
            Dump(screenT, 0, log);

            // Rebind UI Cancel to K (in memory only) and check the Back prompt.
            var player = ReInput.players.GetPlayer(0);
            var kb = player.controllers.maps.GetFirstMapInCategory(ControllerType.Keyboard, 0, 0);
            int cancel = ReInput.mapping.GetAction("UICancel").id;
            foreach (var aem in kb.ElementMapsWithAction(cancel).ToList()) kb.DeleteElementMap(aem.id);
            kb.CreateElementMap(ElementAssignment.KeyboardKeyAssignment(KeyCode.K, ModifierKeyFlags.None, cancel, Pole.Positive));
            int submit = ReInput.mapping.GetAction("UIApply").id;
            foreach (var aem in kb.ElementMapsWithAction(submit).ToList()) kb.DeleteElementMap(aem.id);
            kb.CreateElementMap(ElementAssignment.KeyboardKeyAssignment(KeyCode.Q, ModifierKeyFlags.None, submit, Pole.Positive));
            // The user's case: the game's "Back" (Discard Orb) moved to Esc, which Pause already uses.
            int discard = ReInput.mapping.GetAction("Back").id;
            foreach (var aem in kb.ElementMapsWithAction(discard).ToList()) kb.DeleteElementMap(aem.id);
            kb.CreateElementMap(ElementAssignment.KeyboardKeyAssignment(KeyCode.Escape, ModifierKeyFlags.None, discard, Pole.Positive));
            PromptIcons.RefreshAll();
            Traverse.Create(ControlsScreen.Current).Method("Refresh").GetValue();
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(Out("3_rebound.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            var clashRows = Traverse.Create(ControlsScreen.Current).Field("_rows").GetValue<System.Collections.IList>();
            for (int i = 0; i < clashRows.Count; i++)
            {
                if (Traverse.Create(clashRows[i]).Property("Label").GetValue<string>() == "Discard Orb / Close Inspect")
                {
                    var cell = Traverse.Create(clashRows[i]).Field("Cells").GetValue<Button[]>()[0];
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(cell.gameObject);
                    ControlsScreen.Current.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 0.78f;
                    yield return new WaitForSecondsRealtime(0.8f);
                    ScreenCapture.CaptureScreenshot(Out("3a_clash_row.png"));
                    yield return new WaitForSecondsRealtime(0.4f);
                    Traverse.Create(ControlsScreen.Current).Method("OnCellClicked", new[] { typeof(int), typeof(int) }).GetValue(i, 0);
                    yield return new WaitForSecondsRealtime(0.8f);
                    ScreenCapture.CaptureScreenshot(Out("3b_clash_popup.png"));
                    yield return new WaitForSecondsRealtime(0.4f);
                    Traverse.Create(ControlsScreen.Current).Method("ModalChoose", new[] { typeof(int) }).GetValue(-1);
                    ControlsScreen.Current.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 1f;
                    yield return new WaitForSecondsRealtime(0.4f);
                    break;
                }
            }
            foreach (var g in Resources.FindObjectsOfTypeAll<UI.ButtonGraphicMapper>().Where(g => g.isActiveAndEnabled))
            {
                log.AppendLine($"prompt {Path(g.transform)} -> {g.GetComponent<Image>()?.sprite?.name}");
            }
            Traverse.Create(ControlsScreen.Current).Method("OnCellClicked", new[] { typeof(int), typeof(int) }).GetValue(0, 0);
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(Out("4_modal.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            Traverse.Create(ControlsScreen.Current).Method("ModalChoose", new[] { typeof(int) }).GetValue(-1); // cancel
            foreach (var a in ReInput.mapping.Actions)
            {
                log.AppendLine($"action {a.id} {a.name} desc='{a.descriptiveName}' type={a.type} pos='{a.positiveDescriptiveName}' neg='{a.negativeDescriptiveName}' assignable={a.userAssignable}");
            }
            // Listening pop-up for an unbound-able cell: start listening on row 0 keyboard.
            var rows = Traverse.Create(ControlsScreen.Current).Field("_rows").GetValue<System.Collections.IList>();
            foreach (var map in player.controllers.maps.GetAllMaps(ControllerType.Mouse))
            {
                log.AppendLine($"mouse map cat={map.categoryId} enabled={map.enabled}");
                foreach (var aem in map.AllMaps)
                    log.AppendLine($"   {ReInput.mapping.GetAction(aem.actionId)?.name} {aem.axisContribution} <- {aem.elementIdentifierName} (id {aem.elementIdentifierId}, {aem.elementType}, {aem.axisRange})");
            }
            var (padType, padMap) = RewiredBindings.DefaultControllerMap(0);
            log.AppendLine($"preview pad type {padType}");
            if (padMap != null)
            {
                foreach (var aem in padMap.AllMaps)
                    log.AppendLine($"   pad default {ReInput.mapping.GetAction(aem.actionId)?.name} {aem.axisContribution} <- {aem.elementIdentifierName} (id {aem.elementIdentifierId}, {aem.elementType})");
                foreach (var cat in ReInput.mapping.MapCategories) { var m = ReInput.mapping.GetJoystickMapInstance(padType, cat.id, 0); log.AppendLine($"   category {cat.id} {cat.name} assignable={cat.userAssignable} cancelOn=" + (m == null ? "-" : string.Join(",", m.ElementMapsWithAction(ReInput.mapping.GetAction("UISubmit").id).Select(a => a.elementIdentifierName)))); }
                var sets = Traverse.Create(typeof(PromptIcons)).Method("ElementSets", new[] { typeof(System.Guid), typeof(Joystick) }).GetValue<System.Collections.Generic.Dictionary<int, ControllerIconMapping>>(padType, null);
                foreach (var kv in sets)
                    log.AppendLine($"   element {kv.Key} -> {kv.Value.name} (xbox {kv.Value.xboxIcon?.name})");
                foreach (var set in Resources.FindObjectsOfTypeAll<ControllerIconMapping>())
                {
                    PeglinKeybinds.Core.PromptNames.TryGetIconSetAction(set.name, out string an, out bool pos);
                    var act = an == null ? null : ReInput.mapping.GetAction(an);
                    string els = act == null ? "-" : string.Join(",", RewiredBindings.BindingsFor(padMap, act.id, act.type == InputActionType.Axis, pos).Select(a => a.elementIdentifierId + ":" + a.elementType));
                    log.AppendLine($"   set {set.name} #{set.GetInstanceID()} action={an} xbox={set.xboxIcon?.name} defaults={els}");
                }
            }
            foreach (var e in ReInput.controllers.Mouse.ElementIdentifiers)
                log.AppendLine($"mouse element {e.id} {e.name} {e.elementType}");
            Traverse.Create(ControlsScreen.Current).Method("StartListening", new[] { typeof(Controller), rows[0].GetType(), typeof(int), typeof(bool) })
                .GetValue(ReInput.controllers.Keyboard, rows[0], 0, true);
            yield return new WaitForSecondsRealtime(1.2f);
            ScreenCapture.CaptureScreenshot(Out("4b_listening.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            Traverse.Create(ControlsScreen.Current).Method("FinishListening").GetValue();
            yield return new WaitForSecondsRealtime(0.3f);
            Traverse.Create(ControlsScreen.Current).Method("OnCellClicked", new[] { typeof(int), typeof(int) }).GetValue(0, 0);
            yield return new WaitForSecondsRealtime(0.8f);
            ScreenCapture.CaptureScreenshot(Out("4c_modal.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            Traverse.Create(ControlsScreen.Current).Method("ModalChoose", new[] { typeof(int) }).GetValue(-1); // cancel
            ControlsScreen.Current.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 0.45f;
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(Out("5_middle.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            ControlsScreen.Current.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 0f;
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(Out("6_bottom.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            // Mouse section and its action picker (cancelled, nothing saved).
            var allRows = Traverse.Create(ControlsScreen.Current).Field("_rows").GetValue<System.Collections.IList>();
            int mouseIndex = -1;
            for (int i = 0; i < allRows.Count; i++)
            {
                if (Traverse.Create(allRows[i]).Field("MouseElement").GetValue<int>() >= 0) { mouseIndex = i; break; }
            }
            log.AppendLine("mouse row index: " + mouseIndex + " of " + allRows.Count);
            if (mouseIndex >= 0)
            {
                var cell = Traverse.Create(allRows[mouseIndex]).Field("Cells").GetValue<Button[]>()[0];
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(cell.gameObject);
                ControlsScreen.Current.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 0.2f;
                yield return new WaitForSecondsRealtime(1f);
                ScreenCapture.CaptureScreenshot(Out("6b_mouse.png"));
                yield return new WaitForSecondsRealtime(0.5f);
                Traverse.Create(ControlsScreen.Current).Method("OnCellClicked", new[] { typeof(int), typeof(int) }).GetValue(mouseIndex + 1, 0);
                yield return new WaitForSecondsRealtime(0.5f);
                Traverse.Create(ControlsScreen.Current).Method("ModalChoose", new[] { typeof(int) }).GetValue(1);
                yield return new WaitForSecondsRealtime(0.8f);
                ScreenCapture.CaptureScreenshot(Out("6c_picker.png"));
                yield return new WaitForSecondsRealtime(0.5f);
                Traverse.Create(ControlsScreen.Current).Method("ModalChoose", new[] { typeof(int) }).GetValue(4);
                yield return new WaitForSecondsRealtime(0.3f);
            }
            Traverse.Create(ControlsScreen.Current).Method("AskReset").GetValue();
            yield return new WaitForSecondsRealtime(0.8f);
            ScreenCapture.CaptureScreenshot(Out("6d_reset.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            Traverse.Create(ControlsScreen.Current).Method("ModalChoose", new[] { typeof(int) }).GetValue(-1);
            yield return new WaitForSecondsRealtime(0.3f);
            Traverse.Create(ControlsScreen.Current).Method("Close").GetValue();
            yield return new WaitForSecondsRealtime(1f);
            log.AppendLine("after close selected: " + UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject?.name
                + " accept active: " + menu.acceptButton.gameObject.activeSelf + " accept trigger: " + menu.acceptButton.enabled
                + " screen open: " + (ControlsScreen.Current != null));
            ScreenCapture.CaptureScreenshot(Out("7_closed.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            player.controllers.maps.LoadDefaultMaps(ControllerType.Keyboard);
            if (File.Exists(backup)) { File.Copy(backup, saved, true); File.Delete(backup); }
            File.WriteAllText(Out("log.txt"), log.ToString());
            Application.Quit();
        }

        private IEnumerator Run()
        {
            string mode = File.ReadAllText(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PeglinKeybinds.dump"));
            // TEMPORARY test-run addition: section A (Options row prompt) checks.
            if (mode.Contains("rowprompt"))
            {
                yield return RowPromptTest();
                yield break;
            }
            if (mode.Contains("full"))
            {
                yield return FullTest();
                yield break;
            }
            if (mode.Contains("esc"))
            {
                yield return EscTest();
                yield break;
            }
            if (mode.Contains("battle"))
            {
                yield return BattleTest();
                yield break;
            }
            if (mode.Contains("persist1"))
            {
                yield return PersistTest(1);
                yield break;
            }
            if (mode.Contains("persist2"))
            {
                yield return PersistTest(2);
                yield break;
            }
            if (File.ReadAllText(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PeglinKeybinds.dump")).Contains("scenes"))
            {
                yield return SceneSurvey();
                yield break;
            }
            if (File.ReadAllText(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PeglinKeybinds.dump")).Contains("ui"))
            {
                yield return UiTest();
                yield break;
            }
            yield return new WaitForSecondsRealtime(6f);
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine("=== ControllerIconMapping assets");
                foreach (var m in Resources.FindObjectsOfTypeAll<ControllerIconMapping>().OrderBy(m => m.name))
                {
                    sb.AppendLine($"{m.name}: kb={N(m.keyboardIcon)}/{N(m.keyboardIconPressed)} xbox={N(m.xboxIcon)} ps={N(m.psIcon)} sw={N(m.switchIcon)}");
                }
                sb.AppendLine("=== Sprites on icon textures");
                var textures = Resources.FindObjectsOfTypeAll<ControllerIconMapping>()
                    .SelectMany(m => new[] { m.keyboardIcon, m.xboxIcon, m.psIcon, m.switchIcon })
                    .Where(s => s != null).Select(s => s.texture).Distinct().ToList();
                foreach (var t in textures)
                {
                    sb.AppendLine($"texture {t.name} {t.width}x{t.height}: " + string.Join(", ",
                        Resources.FindObjectsOfTypeAll<Sprite>().Where(s => s.texture == t).Select(s => s.name).OrderBy(n => n)));
                }
                string pngDir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "PeglinKeybinds_sprites");
                Directory.CreateDirectory(pngDir);
                var wanted = new[] { "keyboard_", "xboxone_", "xbox_dpad", "ps4_x", "whitebutton_0", "greenbutton", "CharacterNameScroll_0", "pixelUIPack_643", "SmallBrownBox", "Arrow_Button_32x32" };
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>().Where(s => wanted.Any(w => s.name.StartsWith(w))).GroupBy(s => s.name).Select(g => g.First()))
                {
                    Rect r = s.textureRect;
                    var rt = RenderTexture.GetTemporary(s.texture.width, s.texture.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(s.texture, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.ARGB32, false);
                    tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    File.WriteAllBytes(System.IO.Path.Combine(pngDir, s.name + ".png"), ImageConversion.EncodeToPNG(tex));
                    sb.AppendLine($"sprite {s.name} rect={r} border={s.border} ppu={s.pixelsPerUnit} filter={s.texture.filterMode}");
                }
                sb.AppendLine("=== Key triggers in scene");
                foreach (var k in Resources.FindObjectsOfTypeAll<PixelCrushers.UIButtonKeyTrigger>().Where(k => k.gameObject.scene.IsValid()))
                {
                    sb.AppendLine($"{Path(k.transform)} button={k.buttonName} key={k.key} any={k.anyKeyOrButton}");
                }
                sb.AppendLine("=== ButtonGraphicMappers in scene");
                foreach (var g in Resources.FindObjectsOfTypeAll<UI.ButtonGraphicMapper>())
                {
                    if (g.gameObject.scene.IsValid())
                    {
                        sb.AppendLine($"{Path(g.transform)} -> {N(g._controllerIconMapping)}");
                    }
                }
                sb.AppendLine("=== Actions and default bindings");
                var player = ReInput.players.GetPlayer(0);
                foreach (var map in player.controllers.maps.GetAllMaps(ControllerType.Keyboard).Concat(player.controllers.maps.GetAllMaps(ControllerType.Joystick)))
                {
                    sb.AppendLine($"map {map.controllerType} cat={ReInput.mapping.GetMapCategory(map.categoryId)?.name} enabled={map.enabled}");
                    foreach (var aem in map.AllMaps)
                    {
                        sb.AppendLine($"   {ReInput.mapping.GetAction(aem.actionId)?.name} {aem.axisContribution} <- {aem.elementIdentifierName} (id {aem.elementIdentifierId}, {aem.elementType}, {aem.axisRange}, key {aem.keyCode}, mods {aem.modifierKeyFlags})");
                    }
                }
                foreach (var j in ReInput.controllers.Joysticks)
                {
                    sb.AppendLine($"joystick {j.name} guid {j.hardwareTypeGuid}");
                }
                foreach (var menu in Resources.FindObjectsOfTypeAll<OptionsMenu>().Where(o => o.gameObject.scene.IsValid()))
                {
                    sb.AppendLine("=== OptionsMenu " + Path(menu.transform));
                    Transform root = menu.transform;
                    while (root.parent != null && root.GetComponent<Canvas>() == null)
                    {
                        root = root.parent;
                    }
                    Dump(root, 0, sb);
                }
            }
            catch (System.Exception e)
            {
                sb.AppendLine("FAILED " + e);
            }
            File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "PeglinKeybinds_dump.txt"), sb.ToString());
            Application.Quit();
        }

        // ---- Scripted main-menu checks ("full" in the .dump file). Real key presses are sent through Windows, but only
        // while the game window is focused, so they can never land in another program.

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        private const byte VkEnter = 0x0D, VkEsc = 0x1B, VkUp = 0x26, VkDown = 0x28, VkF5 = 0x74, VkLCtrl = 0xA2, VkJ = 0x4A, VkN = 0x4E;

        private readonly StringBuilder _log = new StringBuilder();
        private readonly StringBuilder _errors = new StringBuilder();
        private int _pass, _fail, _skipped;

        private static WaitForSecondsRealtime Wait(float seconds) => new WaitForSecondsRealtime(seconds);

        private static void SendKey(byte vk, bool up)
        {
            bool extended = (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E;
            uint flags = (up ? 2u : 0u) | (extended ? 1u : 0u);
            keybd_event(vk, (byte)MapVirtualKey(vk, 0), flags, System.UIntPtr.Zero);
        }

        /// <summary>Presses and releases the keys (held together, in order). Skipped if the game isn't focused.</summary>
        private IEnumerator Press(string what, params byte[] keys)
        {
            if (!Application.isFocused)
            {
                _skipped++;
                _log.AppendLine("SKIP press " + what + ": game window not focused");
                yield return Wait(0.2f);
                yield break;
            }
            foreach (byte k in keys) SendKey(k, false);
            // Which game actions Rewired reports while the keys are down. (Unity's own Input doesn't see these
            // simulated presses at all, so BepInEx hotkeys like Ctrl+F5 can't be tested this way.)
            var seen = new System.Collections.Generic.HashSet<string>();
            float until = Time.realtimeSinceStartup + _holdSeconds;
            Player player = ReInput.players.GetPlayer(0);
            while (Time.realtimeSinceStartup < until)
            {
                foreach (InputAction action in ReInput.mapping.Actions)
                    if (player.GetButton(action.id) || Mathf.Abs(player.GetAxis(action.id)) > 0.1f) seen.Add(action.name);
                yield return null;
            }
            LastSeen = seen;
            _log.AppendLine($"  [{what}] Rewired actions: {string.Join(",", seen.ToArray())}");
            for (int i = keys.Length - 1; i >= 0; i--) SendKey(keys[i], true);
            yield return Wait(0.4f);
        }

        private void Check(string name, bool ok, string detail = "")
        {
            if (ok) _pass++; else _fail++;
            _log.AppendLine((ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : ": " + detail));
        }

        private IEnumerator Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(Out(name + ".png"));
            yield return Wait(0.5f);
        }

        private static ControlsScreen S => ControlsScreen.Current;
        private static System.Collections.IList Rows => Traverse.Create(S).Field("_rows").GetValue<System.Collections.IList>();
        private static string RowLabel(object row) => Traverse.Create(row).Property("Label").GetValue<string>();
        private static string CellText(object row, int column) => Traverse.Create(row).Field("Texts").GetValue<TMP_Text[]>()[column].text;
        private static Button CellButton(object row, int column) => Traverse.Create(row).Field("Cells").GetValue<Button[]>()[column];
        private static System.Collections.Generic.List<string> Clashes(object row, int column) =>
            Traverse.Create(row).Field("Clashes").GetValue<System.Collections.Generic.List<string>[]>()[column];
        private static void ClickCell(int row, int column) => Traverse.Create(S).Method("OnCellClicked", new[] { typeof(int), typeof(int) }).GetValue(row, column);
        private static void Choose(int index) => Traverse.Create(S).Method("ModalChoose", new[] { typeof(int) }).GetValue(index);
        private static bool ModalOpen => S != null && Traverse.Create(S).Field("_modal").GetValue<GameObject>()?.activeSelf == true;
        private static string ModalText => Traverse.Create(S).Field("_modalText").GetValue<TMP_Text>()?.text;
        private static bool Listening => S != null && Traverse.Create(S).Field("_listening").GetValue<bool>();
        private static GameObject Selected => UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;

        private static int RowIndex(string label)
        {
            var rows = Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (RowLabel(rows[i]) == label) return i;
            }
            return -1;
        }

        private static string ModalButtons()
        {
            var buttons = Traverse.Create(S).Field("_modalButtons").GetValue<Button[]>();
            return string.Join("|", buttons.Select(b => b == null ? "null" : b.GetComponentInChildren<TMP_Text>(true)?.text).ToArray());
        }

        private static string CfgLine(string file, string key)
        {
            string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, file);
            return File.ReadAllLines(path).FirstOrDefault(l => l.StartsWith(key + " ="));
        }

        private static string MapStates(Player player) => string.Join(", ", new[] { ControllerType.Keyboard, ControllerType.Mouse, ControllerType.Joystick }
            .SelectMany(t => player.controllers.maps.GetAllMaps(t)).Select(m => $"{m.controllerType}/{m.categoryId}/{m.layoutId}={m.enabled}").ToArray());

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                _current?._errors.AppendLine($"{type}: {message}\n{stack}");
            }
        }

        private static DevDump _current;

        /// <summary>Picks a choice in the open pop-up by its label, so the test doesn't depend on button order.</summary>
        private bool ChooseLabel(string label)
        {
            var buttons = Traverse.Create(S).Field("_modalButtons").GetValue<Button[]>();
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].GetComponentInChildren<TMP_Text>(true)?.text == label)
                {
                    Choose(i);
                    return true;
                }
            }
            _log.AppendLine($"  (no '{label}' in pop-up: {ModalButtons()})");
            Choose(-1);
            return false;
        }

        // ---- TEMPORARY test-run addition: section A of the game-test brief (Options row, bottom prompt).
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, System.UIntPtr extra);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ClientToScreen(System.IntPtr hWnd, ref PointI p);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetClientRect(System.IntPtr hWnd, out RectI rect);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(System.IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(System.IntPtr hWnd, int cmd);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct PointI { public int x, y; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct RectI { public int left, top, right, bottom; }

        private static void FocusGame()
        {
            System.IntPtr h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            ShowWindow(h, 9);
            SetForegroundWindow(h);
        }

        private void ClickUnityPoint(Vector2 screen)
        {
            System.IntPtr h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            var origin = new PointI();
            ClientToScreen(h, ref origin);
            GetClientRect(h, out RectI client);
            int x = origin.x + Mathf.RoundToInt(screen.x);
            int y = origin.y + (client.bottom - client.top) - Mathf.RoundToInt(screen.y);
            _log.AppendLine($"  click: unity=({screen.x:0},{screen.y:0}) desktop=({x},{y}) client={client.right - client.left}x{client.bottom - client.top} origin=({origin.x},{origin.y})");
            SetCursorPos(x, y);
            mouse_event(0x0002u, 0, 0, 0, System.UIntPtr.Zero); // left down
            mouse_event(0x0004u, 0, 0, 0, System.UIntPtr.Zero); // left up
        }

        private void LogPrompt(string where, OptionsMenu menu)
        {
            OverrideButtonMapWhenSelected o = menu.mappedButtonOverride;
            UI.ButtonGraphicMapper acceptGraphic = o != null ? o.buttonGraphicMapperToDisable : null;
            UI.ButtonGraphicMapper deleteGraphic = o != null ? o.buttonGraphicMapperToEnable : null;
            _log.AppendLine($"  [{where}] selection='{Selected?.name}' acceptTrigger={menu.acceptButton.enabled} deleteTrigger={menu.deleteButton.enabled}");
            _log.AppendLine($"  [{where}] accept prompt '{Path(acceptGraphic != null ? acceptGraphic.transform : null)}' active={(acceptGraphic != null && acceptGraphic.gameObject.activeInHierarchy)} sprite={N(acceptGraphic != null ? acceptGraphic.GetComponent<Image>()?.sprite : null)} icon={N(acceptGraphic != null ? acceptGraphic._controllerIconMapping : null)}");
            _log.AppendLine($"  [{where}] delete prompt '{Path(deleteGraphic != null ? deleteGraphic.transform : null)}' active={(deleteGraphic != null && deleteGraphic.gameObject.activeInHierarchy)} sprite={N(deleteGraphic != null ? deleteGraphic.GetComponent<Image>()?.sprite : null)} icon={N(deleteGraphic != null ? deleteGraphic._controllerIconMapping : null)}");
            foreach (TMP_Text t in menu.GetComponentsInChildren<TMP_Text>(false))
            {
                if (t.text != null && (t.text.IndexOf("Accept", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || t.text.IndexOf("Delete", System.StringComparison.OrdinalIgnoreCase) >= 0) && t.text.Length < 40)
                {
                    _log.AppendLine($"  [{where}] prompt text '{t.text}' active={t.gameObject.activeInHierarchy} parent={Path(t.transform.parent)}");
                }
            }
        }

        private IEnumerator RowPromptTest()
        {
            _current = this;
            Application.logMessageReceived += OnLog;
            string saved = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PeglinKeybinds.bindings");
            string backup = saved + ".testbackup";
            if (File.Exists(saved)) File.Copy(saved, backup, true);
            Directory.CreateDirectory(Out(""));
            StartCoroutine(Watchdog(180f, "rowprompt.txt", backup, saved));
            yield return Wait(6f);
            FocusGame();
            yield return Wait(1f);
            _log.AppendLine("focused at start: " + Application.isFocused);
            Player player = ReInput.players.GetPlayer(0);
            OptionsMenu menu = Resources.FindObjectsOfTypeAll<OptionsMenu>().FirstOrDefault(o => o.gameObject.scene.IsValid());
            var owner = Resources.FindObjectsOfTypeAll<PauseMenu>().FirstOrDefault(p => p.gameObject.scene.isLoaded
                && Traverse.Create(p).Field("_pausePanel").GetValue<GameObject>() == menu.gameObject);
            if (menu == null || owner == null) { Check("main menu Options found", false, "menu=" + (menu != null) + " owner=" + (owner != null)); yield return FinishBattle(player, saved, backup, "rowprompt.txt"); yield break; }
            yield return OpenOptions(owner);
            yield return Wait(1f);

            Transform rowT = menu.GetComponentsInChildren<Transform>(true).First(t => t.name == OptionsMenuPatch.RowName);
            Check("Options open on the main menu", menu.isActiveAndEnabled);
            Check("Controls row is the Options menu's first selectable", menu.firstSelectable == rowT.gameObject, menu.firstSelectable?.name);
            Check("Controls row selected on open", Selected == rowT.gameObject, Selected?.name);
            for (int i = 0; i < rowT.parent.childCount; i++)
            {
                Transform child = rowT.parent.GetChild(i);
                Image bg = child.GetComponent<Image>();
                _log.AppendLine($"  options row {i} {child.name} active={child.gameObject.activeSelf} bg={(bg == null ? "-" : N(bg.sprite) + " " + bg.color + " enabled=" + bg.enabled)}");
            }
            LogPrompt("controls row selected", menu);
            Check("Controls row selected: Accept prompt is shown", menu.acceptButton.enabled && menu.mappedButtonOverride.buttonGraphicMapperToDisable.gameObject.activeInHierarchy && !menu.mappedButtonOverride.buttonGraphicMapperToEnable.gameObject.activeInHierarchy,
                $"accept={menu.acceptButton.enabled} acceptGraphic={menu.mappedButtonOverride.buttonGraphicMapperToDisable.gameObject.activeInHierarchy} deleteGraphic={menu.mappedButtonOverride.buttonGraphicMapperToEnable.gameObject.activeInHierarchy}");
            Check("Controls row selected: Delete prompt is not shown", !menu.mappedButtonOverride.buttonGraphicMapperToEnable.gameObject.activeInHierarchy);
            Check("Controls row selected: Controls screen not open", S == null);
            yield return Shot("a_row_prompt_before_controls");

            // The real Delete Save Data row: its prompt and trigger swap.
            Selectable del = menu.deleteButtonSelectable;
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(del.gameObject);
            yield return Wait(0.6f);
            _log.AppendLine("selected after picking Delete row: " + Selected?.name);
            LogPrompt("delete row selected", menu);
            Check("Delete row selected: prompt reads Delete (delete graphic on, accept graphic off)",
                menu.mappedButtonOverride.buttonGraphicMapperToEnable.gameObject.activeInHierarchy && !menu.mappedButtonOverride.buttonGraphicMapperToDisable.gameObject.activeInHierarchy,
                $"deleteGraphic={menu.mappedButtonOverride.buttonGraphicMapperToEnable.gameObject.activeInHierarchy} acceptGraphic={menu.mappedButtonOverride.buttonGraphicMapperToDisable.gameObject.activeInHierarchy}");
            Check("Delete row selected: delete trigger on, accept trigger off", menu.deleteButton.enabled && !menu.acceptButton.enabled,
                $"delete={menu.deleteButton.enabled} accept={menu.acceptButton.enabled}");
            yield return Shot("a_row_prompt_delete");

            // Accept on a different row: must not open Controls; the delete row's own confirmation must.
            yield return Press("Enter (Accept) on Delete row", VkEnter);
            yield return Wait(0.8f);
            Check("Accept on the Delete row does not open Controls", S == null, "screen=" + (S != null));
            Check("Accept on the Delete row opens the game's delete confirmation", menu.modalPanel != null && menu.modalPanel.gameObject.activeInHierarchy,
                "modal=" + (menu.modalPanel != null && menu.modalPanel.gameObject.activeInHierarchy));
            yield return Shot("a_row_prompt_delete_confirm");
            Button noButton = Traverse.Create(menu.modalPanel).Field("_noButton").GetValue<Button>();
            if (noButton != null) noButton.onClick.Invoke();
            yield return Wait(0.6f);
            Check("delete confirmation cancelled, no save touched", menu.modalPanel == null || !menu.modalPanel.gameObject.activeInHierarchy);
            _log.AppendLine("  save files still present: " + string.Join(",", new[] { "Save_0.data", "Save_0r.data" }
                .Select(f => f + "=" + File.Exists(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData) + "Low", "Red Nexus Games Inc", "Peglin", f))).ToArray()));

            // A plain row: Accept applies Options as usual.
            Transform neighbour = rowT.parent.GetChild(rowT.GetSiblingIndex() + 1);
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(neighbour.gameObject);
            yield return Wait(0.5f);
            _log.AppendLine("selected plain row: " + Selected?.name);
            LogPrompt("plain row selected", menu);
            Check("plain row selected: Accept trigger and prompt are back on", menu.acceptButton.enabled);
            yield return Press("Enter (Accept) on a plain row", VkEnter);
            yield return Wait(0.8f);
            _log.AppendLine("  after Accept on a plain row: options=" + menu.isActiveAndEnabled + " screen=" + (S != null));
            Check("Accept on a plain row does not open Controls", S == null);
            Check("Accept on a plain row applies/closes Options as usual", !menu.isActiveAndEnabled, "options=" + menu.isActiveAndEnabled);
            yield return Shot("a_row_prompt_after_accept");

            // Reopen and open the Controls screen: mouse click, Enter, controller Submit.
            if (!menu.isActiveAndEnabled) { yield return OpenOptions(owner); yield return Wait(0.8f); }
            _log.AppendLine("reopened, selected: " + Selected?.name);
            FocusGame();
            yield return Wait(0.5f);
            var rowRect = (RectTransform)rowT;
            Canvas canvas = rowT.GetComponentInParent<Canvas>();
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            ClickUnityPoint(UnityEngine.RectTransformUtility.WorldToScreenPoint(cam, rowRect.position));
            yield return Wait(0.8f);
            Check("Controls opens by mouse click", S != null, "selected=" + Selected?.name);
            yield return Shot("a_row_open_mouse");
            if (S != null) Traverse.Create(S).Method("Close").GetValue();
            yield return Wait(0.6f);
            Check("after closing Controls, Options is still open", menu.isActiveAndEnabled && S == null);
            Check("after closing Controls, selection is back on the Controls row", Selected == rowT.gameObject, Selected?.name);
            LogPrompt("after back", menu);
            Check("after closing Controls, prompt reads Accept again", menu.acceptButton.enabled && menu.mappedButtonOverride.buttonGraphicMapperToDisable.gameObject.activeInHierarchy);
            yield return Shot("a_row_prompt_after_back");

            // Keyboard navigation and Enter.
            yield return Press("Down", VkDown);
            Check("Down leaves the Controls row", Selected != null && Selected != rowT.gameObject, Selected?.name);
            yield return Press("Up", VkUp);
            Check("Up returns to the Controls row", Selected == rowT.gameObject, Selected?.name);
            yield return Press("Enter", VkEnter);
            yield return Wait(0.8f);
            Check("Controls opens by Enter", S != null);
            if (S != null) Traverse.Create(S).Method("Close").GetValue();
            yield return Wait(0.6f);

            // Controller Submit: the EventSystem submit event, as a pad's Submit would deliver it (no pad connected).
            var ped = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            UnityEngine.EventSystems.ExecuteEvents.Execute(rowT.gameObject, ped, UnityEngine.EventSystems.ExecuteEvents.submitHandler);
            yield return Wait(0.8f);
            Check("Controls opens by controller Submit (simulated submit event)", S != null, "selected=" + Selected?.name);
            yield return Shot("a_row_open_submit");
            if (S != null) Traverse.Create(S).Method("Close").GetValue();
            yield return Wait(0.6f);
            Check("screen closed at the end", S == null && menu.isActiveAndEnabled);

            Application.logMessageReceived -= OnLog;
            player.controllers.maps.LoadDefaultMaps(ControllerType.Keyboard);
            if (File.Exists(backup)) { File.Copy(backup, saved, true); File.Delete(backup); }
            _log.Insert(0, $"RESULT pass={_pass} fail={_fail} skippedKeys={_skipped}\n");
            _log.AppendLine("=== errors/exceptions during test:\n" + (_errors.Length == 0 ? "none" : _errors.ToString()));
            File.WriteAllText(Out("rowprompt.txt"), _log.ToString());
            Application.Quit();
        }

        /// <summary>Opens a game row's pop-up, picks Replace/Add/Set and presses a key for it.</summary>
        private IEnumerator Rebind(int row, string option, string keyName, byte vk)
        {
            ClickCell(row, 0);
            yield return Wait(0.3f);
            if (!ChooseLabel(option)) yield break;
            yield return Wait(0.3f);
            if (!Listening) { _log.AppendLine("  (not listening after " + option + ")"); yield break; }
            yield return Press(keyName, vk);
            yield return Wait(0.4f);
        }

        private IEnumerator FullTest()
        {
            _current = this;
            Application.logMessageReceived += OnLog;
            string saved = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PeglinKeybinds.bindings");
            string backup = saved + ".testbackup";
            if (File.Exists(saved)) File.Copy(saved, backup, true);
            Directory.CreateDirectory(Out(""));
            StartCoroutine(Watchdog(300f, "full.txt", backup, saved));
            yield return Wait(6f);
            Player player = ReInput.players.GetPlayer(0);
            OptionsMenu menu = Resources.FindObjectsOfTypeAll<OptionsMenu>().FirstOrDefault(o => o.gameObject.scene.IsValid());
            _log.AppendLine("focused at start: " + Application.isFocused + "; maps: " + MapStates(player));
            string mapsAtStart = MapStates(player);

            // --- Ctrl+F5 from the plain main menu.
            Check("main menu has no Options open", menu != null && !menu.isActiveAndEnabled);
            yield return Press("Ctrl+F5", VkLCtrl, VkF5);
            yield return Wait(1.2f);
            Check("Ctrl+F5 opens Options > Controls", S != null && menu.isActiveAndEnabled, $"screen={(S != null)} options={menu.isActiveAndEnabled}");
            yield return Shot("a01_hotkey");
            if (S == null)
            {
                yield return ControlsScreen.OpenFromAnywhere();
                yield return Wait(1f);
            }

            // --- Esc closes Controls but not Options.
            yield return Press("Esc", VkEsc);
            yield return Wait(0.6f);
            Check("Esc closes Controls only", S == null && menu.isActiveAndEnabled, $"screen={(S != null)} options={menu.isActiveAndEnabled} selected={Selected?.name}");
            Check("focus returns to the Controls row", Selected != null && Selected.name == OptionsMenuPatch.RowName, Selected?.name);
            yield return Shot("a02_after_esc");
            yield return Press("Esc (closes Options)", VkEsc);
            yield return Wait(0.8f);
            Check("second Esc closes Options", !menu.isActiveAndEnabled, $"cancelTrigger={menu.cancelButton.enabled} unpauseOnExit={menu.unpauseOnExit} blocks={ControlsScreen.BlocksGameMenus}");
            if (menu.isActiveAndEnabled) { menu.cancelButton.GetComponent<Button>()?.onClick.Invoke(); yield return Wait(0.8f); _log.AppendLine("  closed with its Cancel button instead: " + !menu.isActiveAndEnabled); }

            // --- Options opened the way the game does: Controls row first and selected.
            var owner = Resources.FindObjectsOfTypeAll<PauseMenu>().FirstOrDefault(p => p.gameObject.scene.isLoaded
                && Traverse.Create(p).Field("_pausePanel").GetValue<GameObject>() == menu.gameObject);
            if (owner != null) Traverse.Create(owner).Method("SetPaused", new[] { typeof(bool) }).GetValue(true);
            else menu.gameObject.SetActive(true);
            yield return Wait(1.2f);
            Transform rowT = menu.GetComponentsInChildren<Transform>(true).First(t => t.name == OptionsMenuPatch.RowName);
            Transform contents = rowT.parent;
            Selectable firstSelectable = contents.Cast<Transform>().Select(t => t.GetComponent<Selectable>()).FirstOrDefault(s => s != null && s.gameObject.activeSelf);
            Check("Controls row is the first option", firstSelectable != null && firstSelectable.transform == rowT, firstSelectable?.name);
            Check("Controls row selected on open", Selected == rowT.gameObject, Selected?.name);
            foreach (Transform child in contents)
            {
                Image bg = child.GetComponent<Image>();
                _log.AppendLine($"  options row {child.GetSiblingIndex()} {child.name} active={child.gameObject.activeSelf} bg={(bg == null ? "-" : bg.sprite?.name + " " + bg.color + " enabled=" + bg.enabled)}");
            }
            yield return Shot("a03_options");

            // --- Keyboard navigation in Options.
            yield return Press("Down", VkDown);
            GameObject below = Selected;
            Check("Down from Controls row moves to the next option", below != null && below != rowT.gameObject, below?.name);
            yield return Press("Up", VkUp);
            Check("Up returns to the Controls row", Selected == rowT.gameObject, Selected?.name);
            Selectable rowSel = rowT.GetComponent<Selectable>();
            _log.AppendLine($"  row nav mode={rowSel.navigation.mode} up={rowSel.FindSelectableOnUp()?.name} down={rowSel.FindSelectableOnDown()?.name}");
            Selectable belowSel = below != null ? below.GetComponent<Selectable>() : null;
            if (belowSel != null) _log.AppendLine($"  next row up -> {belowSel.FindSelectableOnUp()?.name}");

            // --- Enter on the row opens the screen.
            yield return Press("Enter", VkEnter);
            yield return Wait(1f);
            Check("Enter on Controls row opens the screen", S != null, "selected=" + Selected?.name);
            if (S == null)
            {
                ControlsScreen.Open(menu, rowT.gameObject);
                yield return Wait(1f);
            }
            yield return Shot("a04_controls_top");
            Check("first cell selected on open", Selected != null && Selected.transform.IsChildOf(S.transform) && Selected.GetComponent<Button>() != null, Selected?.name);
            Check("Options' own Accept/Apply/Cancel hidden", !menu.acceptButton.gameObject.activeSelf && !menu.cancelButton.gameObject.activeSelf);

            // --- Sections, headers, labels.
            var headings = S.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Section ")).Select(t => t.name.Substring(8)).ToList();
            _log.AppendLine("  sections: " + string.Join(", ", headings.ToArray()));
            foreach (string want in new[] { "Gameplay", "Menus", "Loadout & Encirclepedia", "Mouse", "Mods" })
                Check("section " + want + " present", headings.Contains(want));
            string controllerHeader = Traverse.Create(S).Field("_controllerHeader").GetValue<TMP_Text>()?.text;
            Check("controller header shows defaults with no pad", player.controllers.joystickCount > 0 || controllerHeader == "Controller (defaults)", controllerHeader);
            var overflow = S.GetComponentsInChildren<TMP_Text>(false).Where(t => { t.ForceMeshUpdate(); return t.isTextOverflowing; }).Select(t => Path(t.transform) + " '" + t.text + "'").ToList();
            Check("no overflowing text", overflow.Count == 0, string.Join("; ", overflow.ToArray()));
            var rows = Rows;
            _log.AppendLine("  rows (" + rows.Count + "):");
            for (int i = 0; i < rows.Count; i++)
            {
                Button c0 = CellButton(rows[i], 0), c1 = CellButton(rows[i], 1);
                _log.AppendLine($"   [{i}] {RowLabel(rows[i])} | kb='{CellText(rows[i], 0)}' {(c0.interactable ? "" : "(off)")} clash={string.Join("+", Clashes(rows[i], 0).ToArray())} | pad='{CellText(rows[i], 1)}' {(c1.interactable ? "" : "(off)")} colour={c1.GetComponent<Image>().color} clash={string.Join("+", Clashes(rows[i], 1).ToArray())}");
            }
            if (player.controllers.joystickCount == 0)
            {
                bool allGrey = Enumerable.Range(0, rows.Count).Where(i => Traverse.Create(rows[i]).Field("Row").GetValue() != null)
                    .All(i => !CellButton(rows[i], 1).interactable);
                Check("no pad: controller cells are read-only previews", allGrey);
                int fire = RowIndex("Fire / End Shot");
                Check("no pad: Fire shows a default pad button", fire >= 0 && CellText(rows[fire], 1) != "-" && CellText(rows[fire], 1) != "Not bound", fire >= 0 ? CellText(rows[fire], 1) : "no row");
            }

            // --- Intended shared keys aren't flagged.
            int select = RowIndex("Select"), fireRow = RowIndex("Fire / End Shot");
            Check("Select not flagged (shares Enter with Fire)", select >= 0 && Clashes(rows[select], 0).Count == 0, select >= 0 ? CellText(rows[select], 0) + " clash=" + string.Join("+", Clashes(rows[select], 0).ToArray()) : "no row");
            Check("Fire not flagged", fireRow >= 0 && Clashes(rows[fireRow], 0).Count == 0, fireRow >= 0 ? CellText(rows[fireRow], 0) + " clash=" + string.Join("+", Clashes(rows[fireRow], 0).ToArray()) : "no row");

            // --- Scrolling with keys: the selection stays on screen.
            ScrollRect scroll = S.GetComponentInChildren<ScrollRect>();
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            bool allVisible = true;
            for (int i = 0; i < 25; i++)
            {
                yield return Press("Down", VkDown);
                if (!Visible(Selected, viewport)) { allVisible = false; _log.AppendLine($"  selection off screen after {i + 1} downs: {Selected?.transform.parent?.name} pos={scroll.verticalNormalizedPosition:0.00}"); }
            }
            Check("Down x25 scrolls with the selection", allVisible && scroll.verticalNormalizedPosition < 0.95f, $"pos={scroll.verticalNormalizedPosition:0.00} selected={Selected?.transform.parent?.name}");
            yield return Shot("a05_keys_scrolled");
            // Off the bottom: Down from the last row reaches Back.
            Selectable lastCell = Traverse.Create(S).Field("_backButton").GetValue<Button>().navigation.selectOnUp;
            Check("Back reachable from the last row", lastCell != null && lastCell.navigation.selectOnDown == Traverse.Create(S).Field("_backButton").GetValue<Button>(), lastCell?.transform.parent?.name);

            // --- Scrolling with the mouse wheel.
            scroll.verticalNormalizedPosition = 1f;
            yield return Wait(0.3f);
            for (int i = 0; i < 5; i++)
            {
                var wheel = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { scrollDelta = new Vector2(0f, -1f) };
                UnityEngine.EventSystems.ExecuteEvents.Execute(scroll.gameObject, wheel, UnityEngine.EventSystems.ExecuteEvents.scrollHandler);
                yield return null;
            }
            yield return Wait(0.3f);
            Check("mouse wheel scrolls the list", scroll.verticalNormalizedPosition < 0.99f, $"pos={scroll.verticalNormalizedPosition:0.00}");
            yield return Shot("a06_wheel_scrolled");
            scroll.verticalNormalizedPosition = 0.5f;
            yield return Wait(0.5f);
            yield return Shot("a07_middle");
            scroll.verticalNormalizedPosition = 0f;
            yield return Wait(0.5f);
            yield return Shot("a08_bottom");
            scroll.verticalNormalizedPosition = 1f;

            // --- Pop-up choices on Speed Up (keyboard).
            int speed = RowIndex("Speed Up");
            string speedBefore = CellText(Rows[speed], 0);
            _log.AppendLine("  Speed Up before: " + speedBefore);
            ClickCell(speed, 0);
            yield return Wait(0.4f);
            _log.AppendLine("  pop-up: " + ModalButtons() + " text=" + ModalText?.Replace("\n", " / "));
            yield return Shot("a09_popup");
            Check("pop-up offers Replace/Add/Default/Unbind/Cancel", ModalButtons() == "Replace|Add|Default|Unbind|Cancel", ModalButtons());
            ChooseLabel("Cancel");
            yield return Wait(0.3f);
            Check("Cancel changes nothing", !ModalOpen && CellText(Rows[speed], 0) == speedBefore, CellText(Rows[speed], 0));

            yield return Rebind(speed, "Replace", "J", VkJ);
            Check("Replace sets J", CellText(Rows[speed], 0) == "J", CellText(Rows[speed], 0));
            Check("maps back on after Replace", MapStates(player) == mapsAtStart, MapStates(player));
            yield return Rebind(speed, "Add", "N", VkN);
            Check("Add keeps J and adds N", CellText(Rows[speed], 0) == "J / N", CellText(Rows[speed], 0));
            yield return Shot("a10_added");
            yield return Rebind(speed, "Add", "N", VkN);
            Check("Add of a key already there doesn't duplicate", CellText(Rows[speed], 0) == "J / N", CellText(Rows[speed], 0));
            // Default restores just this row: change another row first and check it survives.
            int orbBag = RowIndex("Orb Bag");
            string orbBagBefore = CellText(Rows[orbBag], 0);
            yield return Rebind(orbBag, "Replace", "J", VkJ);
            ClickCell(speed, 0);
            yield return Wait(0.3f);
            ChooseLabel("Default");
            yield return Wait(0.3f);
            var speedRow = (PeglinKeybinds.Core.BindingRow)Traverse.Create(Rows[speed]).Field("Row").GetValue();
            string speedDefault = string.Join(" / ", RewiredBindings.BindingsFor(ReInput.mapping.GetKeyboardMapInstance(speedRow.CategoryId, 0), speedRow).Select(RewiredBindings.Describe).ToArray());
            Check("Default restores the row to the game default", CellText(Rows[speed], 0) == speedDefault, $"now '{CellText(Rows[speed], 0)}' default '{speedDefault}'");
            Check("Default leaves other rows alone", CellText(Rows[orbBag], 0) == "J", CellText(Rows[orbBag], 0));
            ClickCell(speed, 0);
            yield return Wait(0.3f);
            ChooseLabel("Unbind");
            yield return Wait(0.3f);
            Check("Unbind clears the row", CellText(Rows[speed], 0) == "Not bound", CellText(Rows[speed], 0));
            ClickCell(speed, 0);
            yield return Wait(0.3f);
            Check("unbound row offers Set/Default/Cancel", ModalButtons() == "Set|Default|Cancel", ModalButtons());
            Choose(-1);
            yield return Wait(0.3f);
            // Listening times out on its own and gives input back.
            ClickCell(speed, 0);
            yield return Wait(0.3f);
            ChooseLabel("Set");
            yield return Wait(Plugin.ListenSeconds.Value + 1f);
            Check("listening times out", !Listening && !ModalOpen && CellText(Rows[speed], 0) == "Not bound", CellText(Rows[speed], 0));
            Check("maps back on after timeout", MapStates(player) == mapsAtStart, MapStates(player));

            // --- Clash: Discard Orb on Esc.
            int discard = RowIndex("Discard Orb / Close Inspect");
            yield return Rebind(discard, "Replace", "Esc", VkEsc);
            Check("Esc while listening binds, doesn't close", S != null && CellText(Rows[discard], 0) == "ESC", S == null ? "screen closed" : CellText(Rows[discard], 0));
            if (S == null) { ControlsScreen.Open(menu, rowT.gameObject); yield return Wait(1f); }
            Check("Discard Orb on Esc warns about Pause", Clashes(Rows[discard], 0).Contains("Pause"), string.Join("+", Clashes(Rows[discard], 0).ToArray()));
            int pause = RowIndex("Pause");
            Check("Pause row warns too", pause >= 0 && Clashes(Rows[pause], 0).Contains("Discard Orb / Close Inspect"), pause >= 0 ? string.Join("+", Clashes(Rows[pause], 0).ToArray()) : "no row");
            EnsureVisible(scroll, CellButton(Rows[discard], 0).transform);
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(CellButton(Rows[discard], 0).gameObject);
            yield return Wait(0.5f);
            yield return Shot("a11_clash_row");
            ClickCell(discard, 0);
            yield return Wait(0.4f);
            Check("clash pop-up says 'Also used by Pause'", ModalText != null && ModalText.Contains("Also used by") && ModalText.Contains("Pause"), ModalText?.Replace("\n", " / "));
            yield return Shot("a12_clash_popup");
            // Esc on the pop-up cancels it, not the screen.
            yield return Press("Esc", VkEsc);
            yield return Wait(0.3f);
            Check("Esc cancels a pop-up only", S != null && !ModalOpen);
            scroll.verticalNormalizedPosition = 1f;

            // --- Mods section.
            var modRows = Enumerable.Range(0, Rows.Count).Where(i => Traverse.Create(Rows[i]).Field("Mod").GetValue() != null).ToList();
            foreach (int i in modRows)
            {
                var mod = (PeglinKeybinds.Core.ModKeyRow)Traverse.Create(Rows[i]).Field("Mod").GetValue();
                _log.AppendLine($"  mod row [{i}] {(mod.Keyboard ?? mod.Controller).PluginName}: {mod.Label} kb={mod.Keyboard?.Key}='{CellText(Rows[i], 0)}' pad={mod.Controller?.Key}='{CellText(Rows[i], 1)}'");
            }
            Check("no TestingFloor keys listed", !modRows.Any(i => ((PeglinKeybinds.Core.ModKeyRow)Traverse.Create(Rows[i]).Field("Mod").GetValue()).Keyboard?.PluginGuid == "TestingFloor"));
            int restart = modRows.FirstOrDefault(i => ((PeglinKeybinds.Core.ModKeyRow)Traverse.Create(Rows[i]).Field("Mod").GetValue()).Keyboard?.Key == "RestartKey");
            int reroll = modRows.FirstOrDefault(i => ((PeglinKeybinds.Core.ModKeyRow)Traverse.Create(Rows[i]).Field("Mod").GetValue()).Keyboard?.Key == "RerollHotkey");
            Check("Quick Restart row X / L3", modRows.Contains(restart) && CellText(Rows[restart], 0) == "X" && CellText(Rows[restart], 1) == "L3", $"'{CellText(Rows[restart], 0)}' '{CellText(Rows[restart], 1)}'");
            Check("Relic Rerolls row T / R3", modRows.Contains(reroll) && CellText(Rows[reroll], 0) == "T" && CellText(Rows[reroll], 1) == "R3", $"'{CellText(Rows[reroll], 0)}' '{CellText(Rows[reroll], 1)}'");
            ClickCell(restart, 0);
            yield return Wait(0.3f);
            Check("mod pop-up offers Change/Default/Unbind/Cancel", ModalButtons() == "Change|Default|Unbind|Cancel", ModalButtons() + " text=" + ModalText?.Replace("\n", " / "));
            EnsureVisible(scroll, CellButton(Rows[restart], 0).transform);
            yield return Wait(0.3f);
            yield return Shot("a13_mod_popup");
            if (ChooseLabel("Change"))
            {
                yield return Wait(0.3f);
                yield return Press("N", VkN);
                yield return Wait(1f);
            }
            Check("mod key change shows N", CellText(Rows[restart], 0) == "N", CellText(Rows[restart], 0));
            Check("mod key change written to PeglinQuickRestart.cfg", CfgLine("PeglinQuickRestart.cfg", "RestartKey") == "RestartKey = N", CfgLine("PeglinQuickRestart.cfg", "RestartKey"));
            ClickCell(restart, 0);
            yield return Wait(0.3f);
            ChooseLabel("Default");
            yield return Wait(0.5f);
            Check("mod Default puts X back in the .cfg", CfgLine("PeglinQuickRestart.cfg", "RestartKey") == "RestartKey = X", CfgLine("PeglinQuickRestart.cfg", "RestartKey"));
            // A mod key on a game key is flagged: put Relic Rerolls on J (Orb Bag has J from earlier).
            ClickCell(reroll, 0);
            yield return Wait(0.3f);
            if (ChooseLabel("Change"))
            {
                yield return Wait(0.3f);
                yield return Press("J", VkJ);
                yield return Wait(1f);
            }
            Check("mod key on a game key is flagged on both", Clashes(Rows[reroll], 0).Count > 0 && Clashes(Rows[orbBag], 0).Count > 0,
                $"reroll={string.Join("+", Clashes(Rows[reroll], 0).ToArray())} orbBag={string.Join("+", Clashes(Rows[orbBag], 0).ToArray())}");
            ClickCell(reroll, 0);
            yield return Wait(0.3f);
            ChooseLabel("Default");
            yield return Wait(0.5f);
            Check("Relic Rerolls back to T", CfgLine("PeglinRelicRerolls.cfg", "RerollHotkey") == "RerollHotkey = T", CfgLine("PeglinRelicRerolls.cfg", "RerollHotkey"));

            // --- Mouse section.
            int left = RowIndex("Left Click"), right = RowIndex("Right Click");
            _log.AppendLine($"  mouse: left='{CellText(Rows[left], 0)}' right='{CellText(Rows[right], 0)}' middle='{CellText(Rows[RowIndex("Middle Click")], 0)}'");
            ClickCell(right, 0);
            yield return Wait(0.3f);
            string first = ModalText;
            Check("picker offers Previous/Next/Assign/Unbind/Cancel", ModalButtons() == "Previous|Next|Assign|Unbind|Cancel", ModalButtons());
            EnsureVisible(scroll, CellButton(Rows[right], 0).transform);
            yield return Shot("a14_picker");
            ChooseLabel("Next");
            yield return Wait(0.3f);
            string next = ModalText;
            ChooseLabel("Previous");
            yield return Wait(0.3f);
            Check("picker Next/Previous browse", next != first && ModalText == first, $"first='{first?.Replace("\n", " / ")}' next='{next?.Replace("\n", " / ")}'");
            ChooseLabel("Next");
            yield return Wait(0.3f);
            string picked = next.Split('\n').Last(l => l.Contains("/")).Split(new[] { "  " }, System.StringSplitOptions.None).Last().Replace("</size>", "");
            ChooseLabel("Assign");
            yield return Wait(0.3f);
            Check("picker Assign sets the action", CellText(Rows[right], 0) == picked, $"'{CellText(Rows[right], 0)}' expected '{picked}'");
            ClickCell(right, 0);
            yield return Wait(0.3f);
            ChooseLabel("Unbind");
            yield return Wait(0.3f);
            Check("picker Unbind clears", CellText(Rows[right], 0) == "Not bound" || CellText(Rows[right], 0) == "", CellText(Rows[right], 0));
            // Left click: assign something and check the game's own click action stays.
            ClickCell(left, 0);
            yield return Wait(0.3f);
            ChooseLabel("Next");
            yield return Wait(0.2f);
            ChooseLabel("Assign");
            yield return Wait(0.3f);
            ControllerMap mouseMap = player.controllers.maps.GetFirstMapInCategory(ControllerType.Mouse, 0, 0);
            var leftActions = mouseMap == null ? new string[0] : mouseMap.AllMaps.Where(a => a.elementIdentifierName == "Left Mouse Button").Select(a => ReInput.mapping.GetAction(a.actionId)?.name).ToArray();
            Check("Left Click keeps the game's MouseClick action", leftActions.Contains("MouseClick"), string.Join(",", leftActions));
            ClickCell(left, 0);
            yield return Wait(0.3f);
            ChooseLabel("Unbind");
            yield return Wait(0.3f);
            leftActions = mouseMap == null ? new string[0] : mouseMap.AllMaps.Where(a => a.elementIdentifierName == "Left Mouse Button").Select(a => ReInput.mapping.GetAction(a.actionId)?.name).ToArray();
            Check("Unbind on Left Click keeps MouseClick", leftActions.Contains("MouseClick"), string.Join(",", leftActions));

            // --- Reset: Mouse, Keyboard, Controller.
            Traverse.Create(S).Method("AskReset").GetValue();
            yield return Wait(0.4f);
            _log.AppendLine("  reset pop-up: " + ModalButtons());
            yield return Shot("a15_reset");
            ChooseLabel("Mouse");
            yield return Wait(0.4f);
            Check("Reset Mouse: Right Click back to Discard Orb / Close Inspect", CellText(Rows[right], 0) == "Discard Orb / Close Inspect", CellText(Rows[right], 0));
            Traverse.Create(S).Method("AskReset").GetValue();
            yield return Wait(0.3f);
            ChooseLabel("Keyboard");
            yield return Wait(0.4f);
            var notDefault = new System.Collections.Generic.List<string>();
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Traverse.Create(Rows[i]).Field("Row").GetValue() as PeglinKeybinds.Core.BindingRow;
                if (row == null) continue;
                string want = string.Join(" / ", RewiredBindings.BindingsFor(ReInput.mapping.GetKeyboardMapInstance(row.CategoryId, 0), row).Select(RewiredBindings.Describe).Distinct().ToArray());
                if (CellText(Rows[i], 0) != (want.Length == 0 ? "Not bound" : want)) notDefault.Add($"{row.Label}='{CellText(Rows[i], 0)}' vs '{want}'");
            }
            Check("Reset Keyboard: every keyboard row at default", notDefault.Count == 0, string.Join("; ", notDefault.ToArray()));
            Check("Reset Keyboard: no clashes left on game rows", !Enumerable.Range(0, Rows.Count).Any(i => Traverse.Create(Rows[i]).Field("Row").GetValue() != null && Clashes(Rows[i], 0).Count > 0),
                string.Join("; ", Enumerable.Range(0, Rows.Count).Where(i => Clashes(Rows[i], 0).Count > 0).Select(i => RowLabel(Rows[i]) + ":" + string.Join("+", Clashes(Rows[i], 0).ToArray())).ToArray()));
            yield return Shot("a16_after_reset");
            Traverse.Create(S).Method("AskReset").GetValue();
            yield return Wait(0.3f);
            bool hasController = ModalButtons().Contains("Controller");
            ChooseLabel(hasController ? "Controller" : "Cancel");
            yield return Wait(0.4f);
            Check("Reset Controller leaves mod pad keys at default", CfgLine("PeglinQuickRestart.cfg", "ControllerRestartButton") == "ControllerRestartButton = JoystickButton8", CfgLine("PeglinQuickRestart.cfg", "ControllerRestartButton"));

            // --- Back closes the screen, Options stays.
            Button back = Traverse.Create(S).Field("_backButton").GetValue<Button>();
            Check("bottom buttons read Back / Reset", back.GetComponentInChildren<TMP_Text>(true).text == "Back"
                && Traverse.Create(S).Field("_resetButton").GetValue<Button>().GetComponentInChildren<TMP_Text>(true).text == "Reset");
            back.onClick.Invoke();
            yield return Wait(0.6f);
            Check("Back closes Controls, Options stays", S == null && menu.isActiveAndEnabled && menu.acceptButton.gameObject.activeSelf);
            Check("maps all back on at the end", MapStates(player) == mapsAtStart, MapStates(player));
            yield return Shot("a17_closed");

            Application.logMessageReceived -= OnLog;
            player.controllers.maps.LoadDefaultMaps(ControllerType.Keyboard);
            if (File.Exists(backup)) { File.Copy(backup, saved, true); File.Delete(backup); }
            _log.Insert(0, $"RESULT pass={_pass} fail={_fail} skippedKeys={_skipped}\n");
            _log.AppendLine("=== errors/exceptions during test:\n" + (_errors.Length == 0 ? "none" : _errors.ToString()));
            File.WriteAllText(Out("full.txt"), _log.ToString());
            Application.Quit();
        }

        private float _holdSeconds = 0.15f;
        private System.Collections.Generic.HashSet<string> LastSeen = new System.Collections.Generic.HashSet<string>();
        private const byte VkInsert = 0x2D, VkD = 0x44;

        private static string BattleState() => Traverse.Create(typeof(Battle.BattleController)).Field("_battleState").GetValue()?.ToString();

        /// <summary>Every on-screen button prompt: where it is, its own icon set and the sprite it's showing.</summary>
        private string Prompts()
        {
            var sb = new StringBuilder();
            foreach (var g in Resources.FindObjectsOfTypeAll<UI.ButtonGraphicMapper>().Where(g => g.isActiveAndEnabled).OrderBy(g => Path(g.transform)))
            {
                var img = g.GetComponent<Image>();
                string path = string.Join("/", Path(g.transform).Split('/').Reverse().Take(3).Reverse().ToArray());
                sb.AppendLine($"    {path} set={N(g._controllerIconMapping)} sprite={(img == null ? "-" : N(img.sprite) + (img.enabled ? "" : " (hidden)"))}");
            }
            return sb.ToString();
        }

        private static string PromptSprite(string pathPart)
        {
            var g = Resources.FindObjectsOfTypeAll<UI.ButtonGraphicMapper>().FirstOrDefault(m => m.isActiveAndEnabled && Path(m.transform).Contains(pathPart));
            var img = g != null ? g.GetComponent<Image>() : null;
            return img == null ? null : (img.enabled && img.sprite != null ? img.sprite.name : "(none)");
        }

        /// <summary>
        /// Starts a run from the main menu (as Quick Restart does), then checks the hotkey path, button prompts and
        /// rebound keys in the first battle. Leaves the run in progress; the player's save is backed up outside.
        /// </summary>
        private IEnumerator BattleTest()
        {
            _current = this;
            Application.logMessageReceived += OnLog;
            string saved = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PeglinKeybinds.bindings");
            string backup = saved + ".testbackup";
            if (File.Exists(saved)) File.Copy(saved, backup, true);
            Directory.CreateDirectory(Out(""));
            StartCoroutine(Watchdog(300f, "battle.txt", backup, saved));
            yield return Wait(6f);
            Player player = ReInput.players.GetPlayer(0);
            OptionsMenu menu = Resources.FindObjectsOfTypeAll<OptionsMenu>().FirstOrDefault(o => o.gameObject.scene.IsValid());

            // --- Control: does Esc close the main menu's Options when Controls was never opened? (game behaviour)
            var owner = Resources.FindObjectsOfTypeAll<PauseMenu>().FirstOrDefault(p => p.gameObject.scene.isLoaded
                && Traverse.Create(p).Field("_pausePanel").GetValue<GameObject>() == menu.gameObject);
            Traverse.Create(owner).Method("SetPaused", new[] { typeof(bool) }).GetValue(true);
            yield return Wait(1f);
            yield return Press("Esc", VkEsc);
            yield return Wait(0.6f);
            _log.AppendLine($"  control: main-menu Options still open after Esc, Controls never opened: {menu.isActiveAndEnabled} (owner isSubMenu={Traverse.Create(owner).Field("_isSubMenu").GetValue()})");
            yield return Press("Backspace", 0x08);
            yield return Wait(0.6f);
            Check("Backspace (Back) closes main-menu Options", !menu.isActiveAndEnabled);

            // --- Scrolling with keys once the game is in keyboard mode (two real key presses put it there).
            yield return ControlsScreen.OpenFromAnywhere();
            yield return Wait(1f);
            PeglinUI.ControllerSupport.ControllerInputManager.primaryInputType = PeglinUI.ControllerSupport.InputType.Keyboard;
            ScrollRect scroll = S.GetComponentInChildren<ScrollRect>();
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            int offscreen = 0;
            for (int i = 0; i < 45; i++)
            {
                yield return Press("Down", VkDown);
                yield return Wait(0.25f);
                if (!Visible(Selected, viewport)) { offscreen++; _log.AppendLine($"  off screen: {Selected?.transform.parent?.name} pos={scroll.verticalNormalizedPosition:0.00}"); }
            }
            Check("keyboard mode: Down x45 keeps the selection on screen", offscreen == 0 && scroll.verticalNormalizedPosition < 0.5f, $"offscreen={offscreen} pos={scroll.verticalNormalizedPosition:0.00} at {Selected?.transform.parent?.name}");
            yield return Shot("b00_keys_scrolled");
            for (int i = 0; i < 45; i++) { yield return Press("Up", VkUp); }
            yield return Wait(0.4f);
            Check("keyboard mode: Up x45 scrolls back to the top", scroll.verticalNormalizedPosition > 0.95f && Visible(Selected, viewport), $"pos={scroll.verticalNormalizedPosition:0.00}");
            Traverse.Create(S).Method("Close").GetValue();
            yield return Wait(0.5f);
            Traverse.Create(owner).Method("SetPaused", new[] { typeof(bool) }).GetValue(false);
            yield return Wait(1f);

            // --- Start a run.
            var play = FindObjectOfType<PeglinUI.MainMenu.PlayButton>();
            play.MovetoCharacterSelect();
            PeglinUI.MainMenu.Cruciball.CruciballLevelSelector selector = null;
            for (float t = 0; t < 15f && (selector == null || !play._characterSelectGroup.gameObject.activeInHierarchy); t += 0.25f)
            {
                yield return Wait(0.25f);
                selector = FindObjectOfType<PeglinUI.MainMenu.Cruciball.CruciballLevelSelector>();
            }
            yield return Wait(1.5f);
            yield return Shot("b01_character_select");
            selector.playButton.onClick.Invoke();
            string state = null;
            for (float t = 0; t < 90f; t += 0.5f)
            {
                yield return Wait(0.5f);
                // The run may open on the "Would you like a boost?" relic offer: skip it.
                var skip = Resources.FindObjectsOfTypeAll<TMP_Text>().FirstOrDefault(x => x.isActiveAndEnabled && x.text.Contains("Skip Relic"));
                Button skipButton = skip != null ? skip.GetComponentInParent<Button>() : null;
                if (skipButton != null && skipButton.interactable) { _log.AppendLine("  skipping the starting relic offer"); skipButton.onClick.Invoke(); yield return Wait(1f); }
                state = FindObjectOfType<Battle.BattleController>() != null ? BattleState() : null;
                if (state == "AWAITING_SHOT") break;
            }
            _log.AppendLine($"  scene={SceneManager.GetActiveScene().name} battle state={state}");
            if (state != "AWAITING_SHOT")
            {
                yield return Shot("b02_no_battle");
                Check("reached a battle", false, state);
                yield return FinishBattle(player, saved, backup);
                yield break;
            }
            yield return Wait(2f);
            yield return Shot("b02_battle");
            _log.AppendLine("  prompts at battle start (input type " + PeglinUI.ControllerSupport.ControllerInputManager.primaryInputType + "):\n" + Prompts());
            PeglinUI.ControllerSupport.ControllerInputManager.primaryInputType = PeglinUI.ControllerSupport.InputType.Keyboard;
            PromptIcons.RefreshAll();
            yield return Wait(0.5f);
            _log.AppendLine("  prompts in keyboard mode:\n" + Prompts());
            yield return Shot("b03_battle_keyboard");

            // --- Hotkey path mid-run (the code Ctrl+F5 runs; the key itself can't be simulated).
            System.Func<string> pauseInfo = () => $"PauseBlock={PauseMenu.PauseBlock} Paused={PauseMenu.Paused} options=[" +
                string.Join("; ", Resources.FindObjectsOfTypeAll<OptionsMenu>().Where(m => m != null && m.gameObject.scene.isLoaded)
                    .Select(m => $"{Path(m.transform)} active={m.isActiveAndEnabled}").ToArray()) + "] pauseMenus=[" +
                string.Join("; ", Resources.FindObjectsOfTypeAll<PauseMenu>().Where(p => p != null && p.gameObject.scene.isLoaded)
                    .Select(p => $"{Path(p.transform)} active={p.isActiveAndEnabled} sub={Traverse.Create(p).Field("_isSubMenu").GetValue()} panel={N(Traverse.Create(p).Field("_pausePanel").GetValue<GameObject>())}").ToArray()) + "]";
            _log.AppendLine("  before hotkey: " + pauseInfo());
            yield return ControlsScreen.OpenFromAnywhere();
            yield return Wait(1.2f);
            if (S == null)
            {
                _log.AppendLine("  hotkey did nothing; after: " + pauseInfo());
                yield return Wait(3f);
                yield return ControlsScreen.OpenFromAnywhere();
                yield return Wait(1.2f);
                _log.AppendLine($"  retry 3s later: screen={(S != null)} " + pauseInfo());
            }
            if (S == null)
            {
                yield return Press("Esc (game pause)", VkEsc);
                yield return Wait(1f);
                _log.AppendLine("  after Esc: " + pauseInfo());
                yield return ControlsScreen.OpenFromAnywhere();
                yield return Wait(1.2f);
                _log.AppendLine($"  hotkey while paused: screen={(S != null)} " + pauseInfo());
            }
            if (S == null)
            {
                Check("mid-run: opens pause > Options > Controls", false, "no Options menu in this scene");
                // What the in-run pause menu holds instead.
                PauseMenu pause = Resources.FindObjectsOfTypeAll<PauseMenu>().First(p => p.gameObject.scene.isLoaded);
                Traverse.Create(pause).Method("SetPaused", new[] { typeof(bool) }).GetValue(true);
                yield return Wait(1f);
                yield return Shot("b04_pause_menu");
                GameObject panel = Traverse.Create(pause).Field("_pausePanel").GetValue<GameObject>();
                _log.AppendLine("  pause panel buttons/texts: " + string.Join(" | ", panel.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text.Replace("\n", " ")).Where(t => t.Length > 0).ToArray()));
                _log.AppendLine("  options menus after pausing: " + Resources.FindObjectsOfTypeAll<OptionsMenu>().Count(m => m.gameObject.scene.isLoaded));
                Traverse.Create(pause).Method("SetPaused", new[] { typeof(bool) }).GetValue(false);
                yield return Wait(1f);
            }
            else
            {
                Check("mid-run: opens pause > Options > Controls", PauseMenu.Paused && menu != null, $"paused={PauseMenu.Paused} timeScale={Time.timeScale}");
            }
            bool viaScreen = S != null;
            menu = Resources.FindObjectsOfTypeAll<OptionsMenu>().FirstOrDefault(o => o.isActiveAndEnabled);
            yield return Shot("b04_controls_midrun");

            // Rebind in battle: Fire -> J, Speed Up -> Insert (no game art), Discard Orb -> unbound.
            if (!viaScreen)
            {
                // No screen here: make the same map changes the screen makes, then refresh prompts as it does.
                ControllerMap kb = player.controllers.maps.GetFirstMapInCategory(ControllerType.Keyboard, 0, 0);
                void SetKey(string action, KeyCode key)
                {
                    int id = ReInput.mapping.GetAction(action).id;
                    foreach (var aem in kb.ElementMapsWithAction(id).ToList()) kb.DeleteElementMap(aem.id);
                    if (key != KeyCode.None) kb.CreateElementMap(ElementAssignment.KeyboardKeyAssignment(key, ModifierKeyFlags.None, id, Pole.Positive));
                }
                SetKey("Confirm", KeyCode.J);
                SetKey("Speedup", KeyCode.Insert);
                SetKey("Back", KeyCode.None);
                SetKey("TargetRight", KeyCode.N);
                PromptIcons.RefreshAll();
                yield return Wait(0.5f);
                goto AfterClose;
            }
            yield return Rebind(RowIndex("Fire / End Shot"), "Replace", "J", VkJ);
            yield return Rebind(RowIndex("Speed Up"), "Replace", "Insert", VkInsert);
            yield return Rebind(RowIndex("Next Target"), "Replace", "N", VkN);
            int discard = RowIndex("Discard Orb / Close Inspect");
            ClickCell(discard, 0);
            yield return Wait(0.3f);
            ChooseLabel("Unbind");
            yield return Wait(0.3f);
            _log.AppendLine($"  now: fire='{CellText(Rows[RowIndex("Fire / End Shot")], 0)}' speed='{CellText(Rows[RowIndex("Speed Up")], 0)}' discard='{CellText(Rows[discard], 0)}'");

            // Close Controls with Esc, then Options with Esc: back in the battle, unpaused.
            yield return Press("Esc", VkEsc);
            yield return Wait(0.6f);
            Check("mid-run: Esc closes Controls, Options stays", S == null && menu != null && menu.isActiveAndEnabled, $"screen={(S != null)} options={menu?.isActiveAndEnabled}");
            yield return Press("Esc", VkEsc);
            yield return Wait(1f);
            Check("mid-run: second Esc returns to the battle unpaused", !PauseMenu.Paused && Time.timeScale > 0f && (menu == null || !menu.isActiveAndEnabled), $"paused={PauseMenu.Paused} timeScale={Time.timeScale} options={menu?.isActiveAndEnabled}");
        AfterClose:
            yield return Wait(0.5f);
            PeglinUI.ControllerSupport.ControllerInputManager.primaryInputType = PeglinUI.ControllerSupport.InputType.Keyboard;
            PromptIcons.RefreshAll();
            yield return Wait(0.5f);
            string promptsAfter = Prompts();
            _log.AppendLine("  prompts after rebinding:\n" + promptsAfter);
            yield return Shot("b05_prompts_rebound");
            Check("a prompt shows the J key", promptsAfter.Contains("keyboard_j") || promptsAfter.Contains("PeglinKeybinds_") && promptsAfter.Contains("J"), "see prompt list");
            Check("a drawn key cap appears (Insert has no game art)", promptsAfter.Contains("PeglinKeybinds_"), "see prompt list");
            Check("Fire button prompt follows J", (PromptSprite("FireButton/Image") ?? "").ToLower().Contains("_j"), PromptSprite("FireButton/Image"));
            Check("speed-up prompt is a drawn Insert cap", (PromptSprite("SpeedupButtonContainer") ?? "").StartsWith("PeglinKeybinds_"), PromptSprite("SpeedupButtonContainer"));
            Check("discard bin prompt gone (Discard Orb unbound)", PromptSprite("Garbage") == null || PromptSprite("Garbage") == "(none)" || PromptSprite("Garbage").StartsWith("PeglinKeybinds_"), PromptSprite("Garbage"));
            Check("Next Target prompt follows N", (PromptSprite("Targeting UI/NE") ?? "").ToLower().Contains("_n"), PromptSprite("Targeting UI/NE"));

            // --- Rebound keys do what they say.
            _holdSeconds = 0.6f;
            yield return Press("Insert (Speed Up)", VkInsert);
            Check("Insert drives Speed Up", LastSeen.Contains("Speedup"), string.Join(",", LastSeen.ToArray()));
            _holdSeconds = 0.4f;
            yield return Press("D (aim right)", VkD);
            Check("D still aims right", LastSeen.Contains("Horizontal"), string.Join(",", LastSeen.ToArray()));
            _holdSeconds = 0.15f;
            yield return Press("Enter (old Fire key)", VkEnter);
            yield return Wait(0.5f);
            Check("Enter no longer fires", BattleState() == "AWAITING_SHOT" && !LastSeen.Contains("Confirm"), BattleState() + " " + string.Join(",", LastSeen.ToArray()));
            yield return Press("J (Fire)", VkJ);
            Check("J drives Fire", LastSeen.Contains("Confirm"), string.Join(",", LastSeen.ToArray()));
            yield return Wait(1f);
            _log.AppendLine("  battle state after J: " + BattleState());
            Check("J fired the orb", BattleState() != "AWAITING_SHOT", BattleState());
            yield return Shot("b06_after_fire");
            // Wait for the shot to finish, then try the default Discard keys with Discard unbound and Esc for Pause.
            for (float t = 0; t < 40f && BattleState() != "AWAITING_SHOT"; t += 0.5f) yield return Wait(0.5f);
            _log.AppendLine("  next turn: " + BattleState() + "\n  prompts:\n" + Prompts());
            yield return Shot("b07_next_turn");
            yield return Press("Esc (Pause)", VkEsc);
            yield return Wait(0.8f);
            Check("Esc pauses (Discard Orb unbound)", PauseMenu.Paused && !LastSeen.Contains("Back"), $"paused={PauseMenu.Paused} actions={string.Join(",", LastSeen.ToArray())}");
            yield return Shot("b08_esc_pause");
            yield return Press("Esc (unpause)", VkEsc);
            yield return Wait(0.8f);

            // The user's case: Discard Orb back on Esc next to Pause; what does one Esc press do?
            player.controllers.maps.GetFirstMapInCategory(ControllerType.Keyboard, 0, 0)
                .CreateElementMap(ElementAssignment.KeyboardKeyAssignment(KeyCode.Escape, ModifierKeyFlags.None, ReInput.mapping.GetAction("Back").id, Pole.Positive));
            PromptIcons.RefreshAll();
            yield return Wait(0.5f);
            yield return Press("Esc (Pause + Discard)", VkEsc);
            yield return Wait(0.8f);
            _log.AppendLine($"  Esc with Pause and Discard Orb on it: actions={string.Join(",", LastSeen.ToArray())} paused={PauseMenu.Paused} state={BattleState()}");
            yield return Shot("b09_esc_both");
            if (PauseMenu.Paused) { yield return Press("Esc (unpause)", VkEsc); yield return Wait(0.8f); }

            yield return FinishBattle(player, saved, backup);
        }

        /// <summary>The Pause key closes the main menu's Options, whatever it's bound to, without breaking Controls.</summary>
        private IEnumerator EscTest()
        {
            _current = this;
            Application.logMessageReceived += OnLog;
            string saved = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PeglinKeybinds.bindings");
            string backup = saved + ".testbackup";
            if (File.Exists(saved)) File.Copy(saved, backup, true);
            Directory.CreateDirectory(Out(""));
            StartCoroutine(Watchdog(120f, "esc.txt", backup, saved));
            yield return Wait(6f);
            Player player = ReInput.players.GetPlayer(0);
            OptionsMenu menu = Resources.FindObjectsOfTypeAll<OptionsMenu>().FirstOrDefault(o => o.gameObject.scene.IsValid());
            var owner = Resources.FindObjectsOfTypeAll<PauseMenu>().First(p => p.gameObject.scene.isLoaded
                && Traverse.Create(p).Field("_pausePanel").GetValue<GameObject>() == menu.gameObject);
            System.Func<IEnumerator> openOptions = () => OpenOptions(owner);

            yield return openOptions();
            Check("Options open", menu.isActiveAndEnabled);
            yield return Press("Esc", VkEsc);
            yield return Wait(0.6f);
            Check("Esc closes main-menu Options", !menu.isActiveAndEnabled, "actions=" + string.Join(",", LastSeen.ToArray()));
            yield return Shot("e01_after_esc");

            yield return openOptions();
            ControlsScreen.Open(menu, menu.GetComponentsInChildren<Transform>(true).First(t => t.name == OptionsMenuPatch.RowName).gameObject);
            yield return Wait(1f);
            yield return Press("Esc", VkEsc);
            yield return Wait(0.6f);
            Check("Esc in Controls closes only Controls", S == null && menu.isActiveAndEnabled);
            yield return Press("Esc", VkEsc);
            yield return Wait(0.6f);
            Check("then Esc closes Options", !menu.isActiveAndEnabled);

            // A pop-up in Options (delete save data) switches its buttons off: Esc must not close Options under it.
            yield return openOptions();
            Traverse.Create(menu).Method("SetButtonsActive", new[] { typeof(bool) }).GetValue(false);
            yield return Press("Esc", VkEsc);
            yield return Wait(0.6f);
            Check("Esc ignored while Options' buttons are off (pop-up open)", menu.isActiveAndEnabled);
            Traverse.Create(menu).Method("SetButtonsActive", new[] { typeof(bool) }).GetValue(true);
            yield return Wait(0.3f);

            // Pause moved to K: K closes Options, Esc no longer does.
            ControllerMap kb = player.controllers.maps.GetFirstMapInCategory(ControllerType.Keyboard, 0, 0);
            int pauseId = ReInput.mapping.GetAction("Pause").id;
            foreach (var aem in kb.ElementMapsWithAction(pauseId).ToList()) kb.DeleteElementMap(aem.id);
            kb.CreateElementMap(ElementAssignment.KeyboardKeyAssignment(KeyCode.K, ModifierKeyFlags.None, pauseId, Pole.Positive));
            yield return Wait(0.3f);
            yield return Press("Esc", VkEsc);
            yield return Wait(0.6f);
            Check("Pause on K: Esc leaves Options open", menu.isActiveAndEnabled, "actions=" + string.Join(",", LastSeen.ToArray()));
            yield return Press("K", 0x4B);
            yield return Wait(0.6f);
            Check("Pause on K: K closes Options", !menu.isActiveAndEnabled, "actions=" + string.Join(",", LastSeen.ToArray()));
            // Pause and Back on the same key: closes once, no error.
            yield return openOptions();
            kb.CreateElementMap(ElementAssignment.KeyboardKeyAssignment(KeyCode.K, ModifierKeyFlags.None, ReInput.mapping.GetAction("UICancel").id, Pole.Positive));
            yield return Wait(0.3f);
            yield return Press("K", 0x4B);
            yield return Wait(0.6f);
            Check("Pause and Back both on K: K closes Options", !menu.isActiveAndEnabled);

            yield return FinishBattle(player, saved, backup, "esc.txt");
        }

        private static IEnumerator OpenOptions(PauseMenu owner)
        {
            Traverse.Create(owner).Method("SetPaused", new[] { typeof(bool) }).GetValue(true);
            yield return Wait(1f);
        }

        private IEnumerator FinishBattle(Player player, string saved, string backup, string file = "battle.txt")
        {
            Application.logMessageReceived -= OnLog;
            player.controllers.maps.LoadDefaultMaps(ControllerType.Keyboard);
            if (File.Exists(backup)) { File.Copy(backup, saved, true); File.Delete(backup); }
            _log.Insert(0, $"RESULT pass={_pass} fail={_fail} skippedKeys={_skipped}\n");
            _log.AppendLine("=== errors/exceptions during test:\n" + (_errors.Length == 0 ? "none" : _errors.ToString()));
            File.WriteAllText(Out(file), _log.ToString());
            yield return Wait(0.5f);
            Application.Quit();
        }

        /// <summary>If the test stops on an exception, still write what it found, put the bindings back and quit.</summary>
        private IEnumerator Watchdog(float seconds, string file, string backup, string saved)
        {
            yield return Wait(seconds);
            _log.Insert(0, $"RESULT (WATCHDOG: test didn't finish) pass={_pass} fail={_fail} skippedKeys={_skipped}\n");
            _log.AppendLine("=== errors/exceptions during test:\n" + _errors);
            File.WriteAllText(Out(file), _log.ToString());
            if (backup != null && File.Exists(backup)) { File.Copy(backup, saved, true); File.Delete(backup); }
            Application.Quit();
        }

        private static bool Visible(GameObject selected, RectTransform viewport)
        {
            if (selected == null || !(selected.transform is RectTransform rect)) return false;
            var corners = new Vector3[4];
            var view = new Vector3[4];
            rect.GetWorldCorners(corners);
            viewport.GetWorldCorners(view);
            return corners[0].y >= view[0].y - 2f && corners[1].y <= view[1].y + 2f;
        }

        private static void EnsureVisible(ScrollRect scroll, Transform target)
        {
            RectTransform content = scroll.content;
            float total = content.rect.height - ((RectTransform)scroll.viewport ?? (RectTransform)scroll.transform).rect.height;
            if (total <= 0f) return;
            float y = -content.InverseTransformPoint(target.position).y - content.rect.height * (1f - content.pivot.y);
            scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - (y - 100f) / total);
        }

        /// <summary>Two launches: 1 rebinds Speed Up to J through the screen and quits; 2 checks it came back and restores.</summary>
        private IEnumerator PersistTest(int phase)
        {
            _current = this;
            Application.logMessageReceived += OnLog;
            string saved = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "PeglinKeybinds.bindings");
            string backup = saved + ".testbackup";
            Directory.CreateDirectory(Out(""));
            StartCoroutine(Watchdog(90f, "persist" + phase + ".txt", phase == 2 ? backup : null, saved));
            yield return Wait(6f);
            Player player = ReInput.players.GetPlayer(0);
            int speedup = ReInput.mapping.GetAction("Speedup").id;
            System.Func<string> speedKeys = () => string.Join(" / ", player.controllers.maps.GetAllMaps(ControllerType.Keyboard)
                .SelectMany(m => m.ElementMapsWithAction(speedup)).Select(a => a.keyCode.ToString()).ToArray());
            if (phase == 1)
            {
                if (File.Exists(saved)) File.Copy(saved, backup, true);
                yield return ControlsScreen.OpenFromAnywhere();
                yield return Wait(1f);
                yield return Rebind(RowIndex("Speed Up"), "Replace", "J", VkJ);
                Check("phase 1: Speed Up is J", speedKeys() == "J", speedKeys());
                Check("phase 1: saved file mentions the change", File.Exists(saved) && File.GetLastWriteTime(saved) > System.DateTime.Now.AddMinutes(-1));
            }
            else
            {
                Check("phase 2: Speed Up is still J after restart", speedKeys() == "J", speedKeys());
                string states = MapStates(player);
                Check("phase 2: keyboard and mouse maps enabled", !player.controllers.maps.GetAllMaps(ControllerType.Keyboard).Concat(player.controllers.maps.GetAllMaps(ControllerType.Mouse)).Any(m => !m.enabled), states);
                if (File.Exists(backup)) { File.Copy(backup, saved, true); File.Delete(backup); }
            }
            Application.logMessageReceived -= OnLog;
            _log.Insert(0, $"RESULT pass={_pass} fail={_fail} skippedKeys={_skipped}\n");
            _log.AppendLine("=== errors/exceptions during test:\n" + (_errors.Length == 0 ? "none" : _errors.ToString()));
            File.WriteAllText(Out("persist" + phase + ".txt"), _log.ToString());
            Application.Quit();
        }

        private static void Dump(Transform t, int depth, StringBuilder sb)
        {
            var line = new StringBuilder(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [inactive]");
            if (t is RectTransform r)
            {
                line.Append($" rect=({r.anchoredPosition.x:0},{r.anchoredPosition.y:0} {r.rect.width:0}x{r.rect.height:0} amin={r.anchorMin} amax={r.anchorMax} piv={r.pivot})");
            }
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                line.Append(" <").Append(c.GetType().Name);
                switch (c)
                {
                    case TMP_Text text: line.Append($" '{text.text?.Replace("\n", "\\n")}' font={text.font?.name} size={text.fontSize} color={text.color} align={text.alignment}"); break;
                    case Image img: line.Append($" sprite={N(img.sprite)} type={img.type} color={img.color} mat={N(img.material)}"); break;
                    case HorizontalOrVerticalLayoutGroup lg: line.Append($" spacing={lg.spacing} pad={lg.padding} align={lg.childAlignment} ctrl={lg.childControlWidth}/{lg.childControlHeight} expand={lg.childForceExpandWidth}/{lg.childForceExpandHeight}"); break;
                    case LayoutElement le: line.Append($" pref={le.preferredWidth}x{le.preferredHeight} min={le.minWidth}x{le.minHeight}"); break;
                    case Selectable s: line.Append($" nav={s.navigation.mode} up={N(s.navigation.selectOnUp)} down={N(s.navigation.selectOnDown)} trans={s.transition}"); break;
                    case ScrollRect sr: line.Append($" content={N(sr.content)} vert={sr.vertical}"); break;
                    case MonoBehaviour mb when mb.GetType().Namespace?.StartsWith("UnityEngine") != true:
                        foreach (var f in mb.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                        {
                            if (f.FieldType == typeof(string) || f.FieldType.IsPrimitive || typeof(Object).IsAssignableFrom(f.FieldType))
                            {
                                object v = f.GetValue(mb);
                                line.Append($" {f.Name}={(v is Object o ? N(o) : v)}");
                            }
                        }
                        break;
                }
                line.Append('>');
            }
            sb.AppendLine(line.ToString());
            foreach (Transform child in t)
            {
                Dump(child, depth + 1, sb);
            }
        }

        private static string N(Object o) => o == null ? "null" : o.name;

        private static string Path(Transform t) => t == null ? "null" : (t.parent == null ? t.name : Path(t.parent) + "/" + t.name);
    }
}
