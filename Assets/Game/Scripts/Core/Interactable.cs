using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>One choice in a look-at menu (scroll to pick, E to do it).</summary>
    public class InteractOption
    {
        public string label;
        public bool enabled = true;
        /// <summary>Why it can't be done right now (shown greyed out).</summary>
        public string note;
        public System.Action<PlayerInteractor> run;
    }

    /// <summary>Anything the player can look at and press E on.</summary>
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string Prompt { get; }
        public abstract void Interact(PlayerInteractor player);

        /// <summary>
        /// More than one thing to do here (a machine: get in, refuel, tow…): fill `list` and the player scrolls
        /// through them. Leave it empty for a plain "press E" prompt.
        /// </summary>
        public virtual void GetOptions(List<InteractOption> list) { }
    }
}
