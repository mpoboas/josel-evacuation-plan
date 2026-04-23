using UnityEngine;
using UnityEngine.SceneManagement;

public class SuccessPanel : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Arraste o GameObject que contém o painel visual de sucesso.")]
    [SerializeField] private GameObject successPanelObject;
    
    [Header("Definições")]
    [Tooltip("Nome da cena do Menu Principal")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private bool hasWon = false;

    private void Start()
    {
        // Garante que o painel começa desativado
        if (successPanelObject != null)
        {
            successPanelObject.SetActive(false);
        }
    }

    private void Update()
    {
        // Se já ganhou, aguarda pelo Enter para carregar o EndPanel
        if (hasWon)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                TransitionToEndPanel();
            }
        }
    }

    /// <summary>
    /// Ativa o painel de sucesso visual.
    /// </summary>
    public void ShowSuccess()
    {
        hasWon = true;

        if (successPanelObject != null)
        {
            successPanelObject.SetActive(true);
        }

        // Pára o tempo no jogo
        Time.timeScale = 0f;

        // Desbloquear e mostrar o cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("[SuccessPanel] Success message displayed.");
    }

    private void TransitionToEndPanel()
    {
        // Desativa este painel visual
        if (successPanelObject != null)
            successPanelObject.SetActive(false);

        // Procura e ativa o EndPanel (que já deve ter sido preparado pelo EvacuationZone)
        var endPanel = Object.FindAnyObjectByType<EndPanelController>(FindObjectsInactive.Include);
        if (endPanel != null)
        {
            endPanel.gameObject.SetActive(true);
            Debug.Log("[SuccessPanel] Transitioned to EndPanel.");
        }
        else
        {
            Debug.LogError("[SuccessPanel] EndPanelController not found. Loading menu as fallback.");
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}
