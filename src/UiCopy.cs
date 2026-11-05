using System.Linq;
using UnityEngine;

namespace PeglinKeybinds
{
    /// <summary>Helpers for reusing pieces of the game's own UI.</summary>
    internal static class UiCopy
    {
        /// <summary>The green of the game's Accept button.</summary>
        public static readonly Color Green = new Color(0.435f, 0.612f, 0.275f, 1f);

        /// <summary>A muted tan for buttons with nothing bound, from the Options panel's background.</summary>
        public static readonly Color Unbound = new Color(0.72f, 0.66f, 0.55f, 1f);

        /// <summary>Removes the game's translation scripts, which would otherwise put the original text back.</summary>
        public static void RemoveLocalisation(GameObject root)
        {
            RemoveScripts(root, "Localize");
        }

        /// <summary>Destroys every script on the object and its children whose type has one of these names.</summary>
        public static void RemoveScripts(GameObject root, params string[] typeNames)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && typeNames.Contains(behaviour.GetType().Name))
                {
                    Object.DestroyImmediate(behaviour);
                }
            }
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
