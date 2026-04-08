using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MenuController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────
    //  UI References – assign in the Inspector
    // ─────────────────────────────────────────────────────────────────────

    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject levelMenuPanel;
    public GameObject settingsMenuPanel;

    [Header("Level Menu")]
    [Tooltip("The Play button inside the Level Menu. Starts disabled until a level is selected.")]
    public Button playLevelButton;

    [Header("Scene")]
    [Tooltip("Exact name of the Game Scene as registered in Build Settings.")]
    public string gameSceneName = "B";

    // ─────────────────────────────────────────────────────────────────────
    //  Initialisation
    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Make sure we start on the Main Menu
        ShowPanel(mainMenuPanel);

        // The Play button in the Level Menu is locked until a level is selected
        if (playLevelButton != null)
            playLevelButton.interactable = false;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Main Menu
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the Game Scene directly, skipping level selection.
    /// Link this to the main "Play" button if you want a quick-start.
    /// </summary>
    public void PlayGame()
    {
        Debug.Log($"[MenuController] Loading scene: {gameSceneName}");
        SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>
    /// Shows the Level Menu so the player can pick a level.
    /// </summary>
    public void OpenLevelMenu()
    {
        ShowPanel(levelMenuPanel);
    }

    /// <summary>
    /// Shows the Settings Menu.
    /// </summary>
    public void OpenSettingsMenu()
    {
        ShowPanel(settingsMenuPanel);
    }

    /// <summary>
    /// Quits the application (or stops Play Mode in the Editor).
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[MenuController] Quitting game.");
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Level Menu
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by each Level button (pass the level index as Argument in the Inspector).
    /// Saves the selection and unlocks the Play button.
    /// </summary>
    public void SelectLevel(int levelIndex)
    {
        // Persist the selection so the Game Scene can read it with
        // PlayerPrefs.GetInt("SelectedLevel", 0)
        PlayerPrefs.SetInt("SelectedLevel", levelIndex);
        PlayerPrefs.Save();

        Debug.Log($"[MenuController] Level {levelIndex} selected.");

        // Unlock the Play button now that a level has been chosen
        if (playLevelButton != null)
            playLevelButton.interactable = true;
    }

    /// <summary>
    /// Loads the Game Scene with the previously selected level.
    /// Link this to the playLevelButton's OnClick.
    /// </summary>
    public void PlaySelectedLevel()
    {
        int level = PlayerPrefs.GetInt("SelectedLevel", 0);
        Debug.Log($"[MenuController] Playing level {level} – loading scene: {gameSceneName}");
        SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>
    /// Returns to the Main Menu from any sub-menu (Level or Settings).
    /// </summary>
    public void BackToMainMenu()
    {
        ShowPanel(mainMenuPanel);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helper
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hides all panels and then shows only the requested one.
    /// </summary>
    private void ShowPanel(GameObject panelToShow)
    {
        // Hide every panel first
        if (mainMenuPanel   != null) mainMenuPanel.SetActive(false);
        if (levelMenuPanel  != null) levelMenuPanel.SetActive(false);
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(false);

        // Show only the target panel
        if (panelToShow != null)
            panelToShow.SetActive(true);
    }
}
