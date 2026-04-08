using UnityEngine;
using UnityEngine.SceneManagement; 

public class PauseController : MonoBehaviour
{
    [Header("Referências")]
    public GameObject pauseMenuPanel;
    public GameObject settingsMenuPanel; // Referência para o painel de definições
    public string nomeCenaMenuPrincipal = "MainMenu"; 

    private bool jogoPausado = false;

    void Start()
    {
        // Garante que ambos os menus começam desligados quando o nível começa
        pauseMenuPanel.SetActive(false);
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(false); 
        
        Time.timeScale = 1f; 
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

        // Bloqueia o cursor e esconde-o ao voltar ao jogo
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void PauseGame()
    {
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
}