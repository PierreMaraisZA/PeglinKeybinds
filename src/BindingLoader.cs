using BepInEx.Configuration;
using Rewired;
using UnityEngine;

namespace PeglinKeybinds
{
    /// <summary>
    /// Puts saved bindings onto the player's keyboard and controllers once Rewired has started, and onto each
    /// controller as it's plugged in (Rewired gives a newly connected controller the default maps). Also hosts
    /// coroutines that must outlive the controls screen.
    /// </summary>
    internal class BindingLoader : MonoBehaviour
    {
        private BindingStore _store;
        private bool _wasReady;

        public static BindingLoader Instance { get; private set; }

        /// <summary>The game only has one player; null until Rewired has started.</summary>
        public static Player GamePlayer => ReInput.isReady ? ReInput.players.GetPlayer(0) : null;

        public void Init(BindingStore store)
        {
            _store = store;
            Instance = this;
        }

        private void Update()
        {
            // Rewired starts after plugins load, and could in principle restart; reapply whenever it (re)starts.
            bool ready = ReInput.isReady;
            if (ready && !_wasReady)
            {
                ReInput.ControllerConnectedEvent -= OnControllerConnected;
                ReInput.ControllerConnectedEvent += OnControllerConnected;
                Player player = GamePlayer;
                if (player != null)
                {
                    ApplyAll(player);
                }
            }
            _wasReady = ready;

            KeyboardShortcut open = Plugin.OpenControlsKey.Value;
            if (ready && open.MainKey != KeyCode.None && open.IsDown() && ControlsScreen.Current == null)
            {
                StartCoroutine(ControlsScreen.OpenFromAnywhere());
            }
        }

        private void ApplyAll(Player player)
        {
            int applied = 0;
            if (ReInput.controllers.Keyboard != null)
            {
                applied += _store.Apply(player, ReInput.controllers.Keyboard);
            }
            if (ReInput.controllers.Mouse != null)
            {
                applied += _store.Apply(player, ReInput.controllers.Mouse);
            }
            foreach (Joystick joystick in player.controllers.Joysticks)
            {
                applied += _store.Apply(player, joystick);
            }
            if (applied > 0)
            {
                Plugin.Log.LogInfo($"Applied {applied} saved controller map(s).");
                PromptIcons.RefreshAll();
            }
        }

        private void OnControllerConnected(ControllerStatusChangedEventArgs args)
        {
            Player player = GamePlayer;
            Controller controller = ReInput.controllers.GetController(args.controllerType, args.controllerId);
            // Rewired assigns the new controller to a player in the same event; only act once it's ours.
            if (player == null || controller == null || !player.controllers.ContainsController(controller))
            {
                return;
            }
            if (_store.Apply(player, controller) > 0)
            {
                Plugin.Log.LogInfo($"Applied saved bindings to {controller.name}.");
            }
            PromptIcons.RefreshAll();
        }

        private void OnDestroy()
        {
            ReInput.ControllerConnectedEvent -= OnControllerConnected;
        }
    }
}
