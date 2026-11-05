using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PeglinKeybinds.Core;
using Rewired;

namespace PeglinKeybinds
{
    /// <summary>
    /// Saves the player's changed controller maps to a file and puts them back on the player's controllers. Only maps
    /// in user-assignable categories are saved; everything else stays at the game's defaults.
    /// </summary>
    internal sealed class BindingStore
    {
        private readonly string _path;
        private Dictionary<string, string> _maps;
        private bool _readFailed;
        private int _droppedLines;

        public BindingStore(string path)
        {
            _path = path;
        }

        private Dictionary<string, string> Maps
        {
            get
            {
                if (_maps == null)
                {
                    try
                    {
                        _maps = File.Exists(_path) ? BindingFile.Read(File.ReadAllText(_path), out _droppedLines) : new Dictionary<string, string>();
                        if (_droppedLines > 0)
                        {
                            Plugin.Log.LogWarning($"{_path} had {_droppedLines} line(s) that couldn't be read; they'll be left out the next time bindings are saved.");
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"Couldn't read saved bindings from {_path}; using the game's defaults. {e.Message}");
                        _maps = new Dictionary<string, string>();
                        // Don't overwrite a file we couldn't read: it may hold bindings for other controllers.
                        _readFailed = true;
                    }
                }
                return _maps;
            }
        }

        /// <summary>The controller model a saved map belongs to. Keyboards are all alike; controllers go by Rewired's hardware type.</summary>
        public static string HardwareId(Controller controller)
        {
            if (controller is Joystick joystick && joystick.hardwareTypeGuid != Guid.Empty)
            {
                return joystick.hardwareTypeGuid.ToString();
            }
            return controller.type == ControllerType.Keyboard ? "" : controller.hardwareName;
        }

        /// <summary>Controller types (Rewired hardware GUIDs) that have saved bindings, most likely the player's own pads.</summary>
        public IEnumerable<Guid> SavedControllerTypes()
        {
            foreach (string key in Maps.Keys)
            {
                if (MapKey.TryParse(key, out MapKey parsed) && parsed.ControllerType == ControllerType.Joystick.ToString()
                    && Guid.TryParse(parsed.HardwareId, out Guid guid) && guid != Guid.Empty)
                {
                    yield return guid;
                }
            }
        }

        /// <summary>Replaces the player's maps for this controller with any saved ones. Returns how many were applied.</summary>
        public int Apply(Player player, Controller controller)
        {
            string prefix = MapKey.ControllerPrefix(controller.type.ToString(), HardwareId(controller));
            int applied = 0;
            foreach (var entry in Maps.Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                if (!MapKey.TryParse(entry.Key, out MapKey key) || !IsUserAssignable(key.CategoryId))
                {
                    continue;
                }
                try
                {
                    ControllerMap map = ControllerMap.CreateFromXml(controller.type, entry.Value);
                    if (map == null)
                    {
                        continue;
                    }
                    // AddMap replaces the map with the same category and layout. Keep that map's on/off state, since
                    // the game switches some categories on and off depending on the screen.
                    ControllerMap existing = player.controllers.maps.GetMap(controller.type, controller.id, key.CategoryId, key.LayoutId);
                    // Saved maps can carry enabled=false (they may be saved while the controls screen has input off),
                    // so with nothing to copy, switch it on as Rewired would a new map.
                    bool enabled = existing?.enabled ?? true;
                    player.controllers.maps.AddMap(controller, map);
                    ControllerMap added = player.controllers.maps.GetMap(controller.type, controller.id, key.CategoryId, key.LayoutId);
                    if (added != null)
                    {
                        added.enabled = enabled;
                    }
                    applied++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Skipped a saved {controller.type} map ({entry.Key}): {e.Message}");
                }
            }
            return applied;
        }

        /// <summary>Saves the player's current user-assignable maps for this controller, replacing what was saved for it.</summary>
        public void Save(Player player, Controller controller)
        {
            // Collect first: if the controller has no user-assignable maps yet (Rewired hasn't assigned it), keep
            // what was already saved for this model instead of wiping it.
            string type = controller.type.ToString();
            string hardware = HardwareId(controller);
            var saved = new Dictionary<string, string>();
            foreach (ControllerMap map in player.controllers.maps.GetMaps(controller.type, controller.id))
            {
                if (IsUserAssignable(map.categoryId))
                {
                    saved[new MapKey(type, hardware, map.categoryId, map.layoutId).ToString()] = map.ToXmlString();
                }
            }
            if (saved.Count == 0)
            {
                Plugin.Log.LogWarning($"Not saving {controller.name}: it has no bindings in a player-editable category yet.");
                return;
            }
            Forget(controller, write: false);
            foreach (var entry in saved)
            {
                Maps[entry.Key] = entry.Value;
            }
            Write();
        }

        /// <summary>Drops this controller's saved maps, e.g. after resetting it to defaults.</summary>
        public void Forget(Controller controller, bool write = true)
        {
            string prefix = MapKey.ControllerPrefix(controller.type.ToString(), HardwareId(controller));
            foreach (string key in Maps.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                Maps.Remove(key);
            }
            if (write)
            {
                Write();
            }
        }

        /// <summary>
        /// Drops every saved map for a kind of controller, including models that aren't plugged in right now, so
        /// "Reset > Controller" really resets the column instead of leaving old bindings to come back later.
        /// </summary>
        public void ForgetAll(ControllerType type)
        {
            string prefix = type + "|";
            foreach (string key in Maps.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                Maps.Remove(key);
            }
            Write();
        }

        private void Write()
        {
            if (_readFailed)
            {
                Plugin.Log.LogWarning($"Not saving bindings: {_path} couldn't be read at startup and would be overwritten.");
                return;
            }
            try
            {
                if (Maps.Count == 0)
                {
                    // Nothing left to save. Never delete a file that had lines we couldn't read, in case they can
                    // still be recovered by hand.
                    if (File.Exists(_path) && _droppedLines == 0)
                    {
                        File.Delete(_path);
                    }
                    return;
                }
                // Write then swap in one step, so a crash mid-write can't leave a half-written or missing file.
                string temp = _path + ".tmp";
                File.WriteAllText(temp, BindingFile.Write(Maps));
                if (File.Exists(_path))
                {
                    File.Replace(temp, _path, null);
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't save bindings to {_path}: {e.Message}");
            }
        }

        private static bool IsUserAssignable(int categoryId)
        {
            InputMapCategory category = ReInput.mapping.GetMapCategory(categoryId);
            return category != null && category.userAssignable;
        }
    }
}
