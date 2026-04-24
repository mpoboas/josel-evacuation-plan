using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class LoadingPanelController : MonoBehaviour
{
    [Header("UI Reference")]
    [Tooltip("The panel GameObject to show/hide.")]
    [SerializeField] private GameObject loadingPanel;

    [Header("Settings")]
    [Tooltip("How long to show the loading screen in the game scene.")]
    [SerializeField] private float displayDuration = 2.0f;

    private void Awake()
    {
        // If we are in the game scene, we want to show it immediately.
        // Otherwise, ensure it's hidden.
        if (SceneManager.GetActiveScene().name == "B")
        {
            Show();
        }
        else
        {
            if (loadingPanel != null)
                loadingPanel.SetActive(false);
        }
    }

    private void Start()
    {
        // If we are in the game scene ("B"), start the hide timer
        if (SceneManager.GetActiveScene().name == "B")
        {
            StartCoroutine(ShowLoadingRoutine());
        }
    }

    public void Show()
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
        }
    }

    public void Hide()
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(false);
        }

        // When the panel is hidden, start the gameplay timer
        if (GameplaySessionStats.Instance != null)
        {
            GameplaySessionStats.Instance.StartTimer();
        }
    }

    private IEnumerator ShowLoadingRoutine()
    {
        // The panel is already shown in Awake for scene B
        yield return new WaitForSeconds(displayDuration);
        Hide();
    }
}
