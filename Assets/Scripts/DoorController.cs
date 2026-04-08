using UnityEngine;

/// <summary>
/// Controls a door using two dedicated Animator clips: one to open, one to close.
/// The door is toggled when the player looks at the handle collider and presses E
/// (handled by PlayerInteraction.cs via the IInteractable interface).
/// </summary>
public class DoorController : MonoBehaviour, IInteractable
{
    [Header("Animator")]
    [Tooltip("Animator component that contains the open and close animations.")]
    public Animator doorAnimator;

    [Header("Animation Trigger Names")]
    [Tooltip("Trigger name defined in the Animator for the Open animation.")]
    public string openTrigger = "Door_Open";
    [Tooltip("Trigger name defined in the Animator for the Close animation.")]
    public string closeTrigger = "Door_Close";

    private bool isOpen = false;

    private void Awake()
    {
        // Fallback: try to find the Animator on this GameObject if not assigned
        if (doorAnimator == null)
            doorAnimator = GetComponent<Animator>();

        if (doorAnimator == null)
            Debug.LogWarning($"[DoorController] No Animator found on '{gameObject.name}'. Assign it in the Inspector.");
    }

    private void Start()
    {
        // Garante que nenhum trigger fica activo no arranque (evita animações auto-play)
        if (doorAnimator != null)
        {
            doorAnimator.ResetTrigger(openTrigger);
            doorAnimator.ResetTrigger(closeTrigger);
        }
    }

    /// <summary>
    /// Called by PlayerInteraction when the player presses E while looking at the handle.
    /// </summary>
    public void Interact()
    {
        if (doorAnimator == null) return;

        isOpen = !isOpen;

        if (isOpen)
        {
            doorAnimator.ResetTrigger(closeTrigger);
            doorAnimator.SetTrigger(openTrigger);
        }
        else
        {
            doorAnimator.ResetTrigger(openTrigger);
            doorAnimator.SetTrigger(closeTrigger);
        }
    }

    /// <summary>
    /// Returns the interaction hint shown in the UI.
    /// </summary>
    public string GetInteractText()
    {
        return isOpen ? "Close Door [E]" : "Open Door [E]";
    }
}
