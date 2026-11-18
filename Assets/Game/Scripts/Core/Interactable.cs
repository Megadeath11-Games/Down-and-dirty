using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>Anything the player can look at and press E on.</summary>
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string Prompt { get; }
        public abstract void Interact(PlayerInteractor player);
    }
}
