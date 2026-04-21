using UnityEngine;

/// <summary>
/// Controls a door using two dedicated Animator clips: one to open, one to close.
/// The door is toggled when the player looks at the handle collider and presses E
/// (handled by PlayerInteraction.cs via the IInteractable interface).
/// </summary>
public class DoorController : MonoBehaviour, IInteractable, IInspectable
{
    [Header("Animator")]
    [Tooltip("Animator component that contains the open and close animations.")]
    public Animator doorAnimator;

    [Header("Animation Trigger Names")]
    [Tooltip("Trigger name defined in the Animator for the Open animation.")]
    public string openTrigger = "Door_Open";
    [Tooltip("Trigger name defined in the Animator for the Close animation.")]
    public string closeTrigger = "Door_Close";

    [Header("Temperature")]
    [Tooltip("Define se a porta está quente ao toque.")]
    public bool isHot = false;

    private bool isOpen = false;
    private bool hasBeenInspected = false;
    public bool IsOpen => isOpen;
    public bool HasBeenInspected => hasBeenInspected;

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

        // Se a porta estiver quente e o jogador a abrir SEM inspecionar primeiro
        if (isHot && !isOpen && !hasBeenInspected)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                SmokeHealthReceiver health = playerObj.GetComponent<SmokeHealthReceiver>();
                if (health != null)
                {
                    Debug.Log($"[DoorController] Player opened hot door without checking! Applying damage.");
                    health.TakeFlameDamage(5f);
                }
            }
        }

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

        GameplaySessionStats.Instance?.RegisterDoorStateChanged(this, isOpen, transform.position);
    }

    /// <summary>
    /// Returns the interaction hint shown in the UI.
    /// </summary>
    public string GetInteractText()
    {
        return isOpen ? "Close Door [E]" : "Open Door [E]";
    }

    /// <summary>
    /// Chamado por PlayerInteraction quando o jogador prime R enquanto olha para a porta.
    /// </summary>
    public InspectResult Inspect()
    {
        hasBeenInspected = true;
        GameplaySessionStats.Instance?.RegisterDoorHeatChecked(this, transform.position);

        if (isHot)
            return new InspectResult { message = "Too Hot", isSafe = false };
        else
            return new InspectResult { message = "Safe", isSafe = true };
    }
}
