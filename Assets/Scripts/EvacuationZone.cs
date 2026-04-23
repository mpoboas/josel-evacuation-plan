using UnityEngine;

/// <summary>
/// Script to be attached to the escape zones (triggers).
/// When the player enters the zone, it triggers the end panel.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EvacuationZone : MonoBehaviour
{
    [Header("End Panel Reference")]
    [Tooltip("Assign the EndPanel object that has the EndPanelController script.")]
    public EndPanelController endPanelController;

    private void OnTriggerEnter(Collider other)
    {
        // Verify if the collider belongs to the Player
        if (other.CompareTag("Player"))
        {
            Debug.Log($"[Evacuation] Player entered {gameObject.name}. Triggering success sequence.");

            // 1. Prepare the EndPanel (calculate stats) but keep it HIDDEN for now
            if (endPanelController != null)
            {
                // true = reachedGoal, false = activateGameObject
                endPanelController.Show(true, false);
            }

            // 2. Trigger the SuccessPanel visual
            var successPanel = Object.FindAnyObjectByType<SuccessPanel>(FindObjectsInactive.Include);
            if (successPanel != null)
            {
                successPanel.ShowSuccess();
            }
            else
            {
                Debug.LogWarning("[Evacuation] SuccessPanel not found in scene. Showing EndPanel directly as fallback.");
                if (endPanelController != null)
                    endPanelController.gameObject.SetActive(true);
            }
        }
    }

    private void Awake()
    {
        // Ensure the collider is set to trigger
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[Evacuation] Collider on {gameObject.name} was not a trigger. Fixing automatically.");
            col.isTrigger = true;
        }

        if (endPanelController == null)
        {
            endPanelController = Object.FindAnyObjectByType<EndPanelController>(FindObjectsInactive.Include);
            if (endPanelController != null)
                Debug.Log($"[Evacuation] Resolved EndPanelController to '{endPanelController.gameObject.name}'");
        }
    }
}
