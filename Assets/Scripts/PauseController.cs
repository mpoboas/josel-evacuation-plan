using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseController : MonoBehaviour
{
    [Header("Referências")]
    public GameObject pauseMenuPanel;
    public GameObject settingsMenuPanel; // Referência para o painel de definições
    public string nomeCenaMenuPrincipal = "MainMenu";

    [Header("Gameplay HUD (hidden while paused)")]
    [Tooltip("Optional extra roots to hide with the pause menu (e.g. a custom HUD canvas). If empty, HealthPanel and CrosshairAndStamina are found by name.")]
    public GameObject[] additionalHudRootsToHide;

    private bool jogoPausado = false;
    private readonly List<GameObject> _resolvedGameplayHudRoots = new List<GameObject>();

    void Start()
    {
        // Garante que ambos os menus começam desligados quando o nível começa
        pauseMenuPanel.SetActive(false);
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(false);

        Time.timeScale = 1f;
        ResolveGameplayHudRoots();
    }

    void Update()
    {
        // Verifica se o jogador carregou na tecla ESC
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // Se o menu de definições estiver aberto, o ESC apenas volta ao menu de pausa
            if (settingsMenuPanel != null && settingsMenuPanel.activeSelf)
            {
                CloseSettings();
            }
            // Se já estiver pausado (no menu principal de pausa), tira o pause
            else if (jogoPausado)
            {
                ResumeGame(); 
            }
            // Se estiver a jogar, mete pause
            else
            {
                PauseGame(); 
            }
        }
    }

    public void ResumeGame()
    {
        pauseMenuPanel.SetActive(false);
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(false); // Garante que fecha tudo

        Time.timeScale = 1f;
        jogoPausado = false;

        SetGameplayHudVisible(true);

        // Bloqueia o cursor e esconde-o ao voltar ao jogo
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void PauseGame()
    {
        ResolveGameplayHudRoots();
        SetGameplayHudVisible(false);

        pauseMenuPanel.SetActive(true);
        Time.timeScale = 0f;
        jogoPausado = true;

        // Liberta o cursor para o jogador poder clicar no menu
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // --- NOVAS FUNÇÕES PARA AS DEFINIÇÕES ---

    public void OpenSettings()
    {
        pauseMenuPanel.SetActive(false); // Esconde os botões de pausa
        settingsMenuPanel.SetActive(true); // Mostra os sliders
    }

    public void CloseSettings()
    {
        settingsMenuPanel.SetActive(false); // Esconde os sliders
        pauseMenuPanel.SetActive(true); // Mostra os botões de pausa novamente
    }

    // ----------------------------------------

    public void BackToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(nomeCenaMenuPrincipal);
    }

    private void ResolveGameplayHudRoots()
    {
        _resolvedGameplayHudRoots.Clear();

        if (additionalHudRootsToHide != null)
        {
            foreach (GameObject go in additionalHudRootsToHide)
            {
                if (go != null)
                    _resolvedGameplayHudRoots.Add(go);
            }
        }

        if (_resolvedGameplayHudRoots.Count > 0)
            return;

        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded)
                continue;

            string n = t.gameObject.name;
            if (n == "HealthPanel" || n == "CrosshairAndStamina")
                _resolvedGameplayHudRoots.Add(t.gameObject);
        }
    }

    private void SetGameplayHudVisible(bool visible)
    {
        foreach (GameObject go in _resolvedGameplayHudRoots)
        {
            if (go != null)
                go.SetActive(visible);
        }
    }
}