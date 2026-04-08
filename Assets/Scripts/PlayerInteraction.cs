using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach this to the Player (or its Camera child).
/// Every frame it sends a raycast from the camera centre;
/// if the hit object (or any of its parents) implements IInteractable,
/// an optional UI prompt is shown and pressing E calls Interact().
///
/// Setup checklist:
///   1. Attach this script to the same GameObject as FirstPersonController
///      (or drag the player camera into the 'playerCamera' field).
///   2. Make sure the door handle has a Collider (any type) and the
///      DoorController on the door root implements IInteractable.
///   3. (Optional) Create a UI Text/TMP element for the interaction hint
///      and drag it into the 'interactPrompt' field.
/// </summary>
public class PlayerInteraction : MonoBehaviour
{
    [Header("Ray Settings")]
    [Tooltip("The camera used to cast the interaction ray. " +
             "Leave empty to use Camera.main.")]
    public Camera playerCamera;

    [Tooltip("Maximum distance (metres) within which the player can interact.")]
    public float interactRange = 3f;

    [Tooltip("Layer mask – set to the layer(s) your door handles are on. " +
             "Leave as 'Everything' to hit any collider.")]
    public LayerMask interactMask = ~0;

    [Header("Input")]
    [Tooltip("Key the player presses to interact.")]
    public KeyCode interactKey = KeyCode.E;

    [Header("UI Prompt (optional)")]
    [Tooltip("A UI Text or TMP_Text component that shows the interaction hint. " +
             "Assign in the Inspector; leave empty to skip.")]
    public Text interactPrompt;   // swap for TMP_Text if you use TextMeshPro

    // ── runtime state ──────────────────────────────────────────────────
    private IInteractable _currentTarget;

    // ───────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null)
            Debug.LogError("[PlayerInteraction] No camera found! " +
                           "Assign 'playerCamera' in the Inspector.");

        // Hide prompt at startup
        SetPromptVisible(false);
    }

    private void Update()
    {
        ScanForInteractable();

        if (_currentTarget != null && Input.GetKeyDown(interactKey))
            _currentTarget.Interact();
    }

    // ── core raycast ────────────────────────────────────────────────────

    private void ScanForInteractable()
    {
        if (playerCamera == null) return;

        Ray ray = new Ray(playerCamera.transform.position,
                          playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactMask))
        {
            // Walk up the hierarchy so the collider can be on a child object
            // (e.g. the handle) while IInteractable lives on the door root.
            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();

            if (interactable != null)
            {
                SetTarget(interactable);
                return;
            }
        }

        // Nothing found – clear target
        SetTarget(null);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private void SetTarget(IInteractable target)
    {
        if (target == _currentTarget) return;

        _currentTarget = target;

        if (_currentTarget != null)
        {
            SetPromptText(_currentTarget.GetInteractText());
            SetPromptVisible(true);
        }
        else
        {
            SetPromptVisible(false);
        }
    }

    private void SetPromptText(string text)
    {
        if (interactPrompt != null)
            interactPrompt.text = text;
    }

    private void SetPromptVisible(bool visible)
    {
        if (interactPrompt != null)
            interactPrompt.gameObject.SetActive(visible);
    }

    // ── editor visualisation ────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (playerCamera == null) return;

        Gizmos.color = _currentTarget != null ? Color.green : Color.yellow;
        Gizmos.DrawRay(playerCamera.transform.position,
                       playerCamera.transform.forward * interactRange);
    }
#endif
}
