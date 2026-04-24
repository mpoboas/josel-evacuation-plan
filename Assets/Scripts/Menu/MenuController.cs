using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class MenuController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────
    //  UI References – assign in the Inspector
    // ─────────────────────────────────────────────────────────────────────

    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject levelMenuPanel;
    public GameObject settingsMenuPanel;
    public LoadingPanelController loadingController;

    [Header("Level Menu")]
    [Tooltip("The Play button inside the Level Menu. Starts disabled until a level is selected.")]
    public Button playLevelButton;

    [Header("Level Selection UI")]
    public TMP_Text levelTitleText; // Displays "Level X - Goals"
    public TMP_Text timeGoalText;
    public TMP_Text smokeGoalText;
    public TMP_Text fireGoalText;
    public TMP_Text doorsClosedGoalText;
    public TMP_Text doorsCheckedGoalText;

    [Header("Scene")]
    [Tooltip("Exact name of the Game Scene as registered in Build Settings.")]
    public string gameSceneName = "B";

    // ─────────────────────────────────────────────────────────────────────
    //  Initialisation
    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        EnsureAudioSettingsController();

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
        if (loadingController != null)
        {
            loadingController.Show();
        }
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
        AudioSettingsMenu audioSettings = settingsMenuPanel != null ? settingsMenuPanel.GetComponent<AudioSettingsMenu>() : null;
        audioSettings?.SyncFromAudioManager();
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
        // Persist the selection
        PlayerPrefs.SetInt("SelectedLevel", levelIndex);
        PlayerPrefs.Save();

        Debug.Log($"[MenuController] Level {levelIndex} selected.");

        // 1. Update Title (Level Index + 1 for user friendly display)
        if (levelTitleText != null)
            levelTitleText.text = $"Level {levelIndex + 1} - Goals";

        // 2. Fetch Goals from GameManager
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null && gm.levels != null && levelIndex >= 0 && levelIndex < gm.levels.Length)
        {
            var level = gm.levels[levelIndex];
            
            if (timeGoalText != null) 
                timeGoalText.text = $"Time Target: {FormatTime(level.targetTimeSeconds)}";
            
            if (smokeGoalText != null) 
                smokeGoalText.text = $"Max Smoke: {Mathf.RoundToInt(level.maxSmokeDamageAllowed)}";
            
            if (fireGoalText != null) 
                fireGoalText.text = $"Max Fire: {Mathf.RoundToInt(level.maxFireDamageAllowed)}";
            
            if (doorsClosedGoalText != null) 
                doorsClosedGoalText.text = $"Min Doors Closed: {level.minDoorsClosedRequired}";
            
            if (doorsCheckedGoalText != null) 
                doorsCheckedGoalText.text = $"Min Doors Checked: {level.minDoorsCheckedRequired}";
        }

        // Unlock the Play button now that a level has been chosen
        if (playLevelButton != null)
            playLevelButton.interactable = true;
    }

    private string FormatTime(float totalSeconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(totalSeconds));
        int mins = total / 60;
        int secs = total % 60;
        return string.Format("{0:00}:{1:00}", mins, secs);
    }

    /// <summary>
    /// Loads the Game Scene with the previously selected level.
    /// Link this to the playLevelButton's OnClick.
    /// </summary>
    public void PlaySelectedLevel()
    {
        int level = PlayerPrefs.GetInt("SelectedLevel", 0);
        Debug.Log($"[MenuController] Playing level {level} – loading scene: {gameSceneName}");
        
        if (loadingController != null)
        {
            loadingController.Show();
        }

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

    private void EnsureAudioSettingsController()
    {
        if (settingsMenuPanel == null)
        {
            return;
        }

        if (settingsMenuPanel.GetComponent<AudioSettingsMenu>() == null)
        {
            settingsMenuPanel.AddComponent<AudioSettingsMenu>();
        }
    }
}
