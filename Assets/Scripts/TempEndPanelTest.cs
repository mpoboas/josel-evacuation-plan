using UnityEngine;

/// <summary>
/// Temporary script to test the EndPanel appearance after a delay.
/// </summary>
public class TempEndPanelTest : MonoBehaviour
{
    [Header("Testing Configuration")]
    [Tooltip("The root GameObject of your End Panel UI.")]
    public GameObject endPanelRoot;

    [Tooltip("How many seconds to wait before showing the panel.")]
    public float delaySeconds = 10f;

    void Start()
    {
        if (endPanelRoot == null)
        {
            Debug.LogError("[EndPanelTest] Please assign the End Panel Root in the Inspector!");
            return;
        }

        // Ensure it starts hidden
        endPanelRoot.SetActive(false);

        // Schedule the panel display
        Invoke("ShowPanel", delaySeconds);
        Debug.Log($"[EndPanelTest] Waiting {delaySeconds} seconds to show panel...");
    }

    void ShowPanel()
    {
        if (endPanelRoot != null)
        {
            // Ensure the official controller is present so the replay widget auto-wires itself.
            EndPanelController controller = endPanelRoot.GetComponent<EndPanelController>();
            if (controller == null)
                controller = endPanelRoot.AddComponent<EndPanelController>();

            controller.Show();

            Debug.Log("[EndPanelTest] Test call complete.");
        }
    }
}
