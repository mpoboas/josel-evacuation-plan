using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Controls the final statistics panel, allowing the player to navigate to the next level,
/// restart the current one, or return to the main menu.
/// </summary>
public class EndPanelController : MonoBehaviour
{
    [Header("Scene Configuration")]
    [Tooltip("Name of the main menu scene.")]
    public string menuSceneName = "MainMenu";
    
    [Tooltip("Name of the game scene (where GameManager logic resides).")]
    public string gameSceneName = "B";

    [Header("Stats Text (auto-resolved by default)")]
    public TMP_Text timeText;
    public TMP_Text smokeDamageText;
    public TMP_Text fireDamageText;
    public TMP_Text doorsClosedText;
    public TMP_Text doorsCheckedText;

    /// <summary>
    /// Call this method to display the end panel, pause the game, and unlock the cursor.
    /// </summary>
    public void Show()
    {
        // Make sure the panel (and any ancestor) is active before starting coroutines.
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        Time.timeScale = 0f;

        // Unlock and show cursor for button interaction
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        RefreshStatsUI();
        EnsureMovementReplay();

        Debug.Log("[EndPanel] Panel displayed.");
    }

    /// <summary>
    /// Ensures the post-game movement replay widget is attached and restarted whenever the panel is shown.
    /// The widget expects scene-bound Timeline references (wired in EndPanel hierarchy).
    /// </summary>
    private void EnsureMovementReplay()
    {
        var replay = GetComponent<EndPanelMovementReplay>();
        if (replay == null)
            replay = gameObject.AddComponent<EndPanelMovementReplay>();

        // If the replay was already enabled before (e.g. panel re-shown), force a fresh run.
        if (replay.enabled)
            replay.StartReplay();
    }

    private void RefreshStatsUI()
    {
        ResolveStatsReferences();
        var stats = GameplaySessionStats.Instance;
        if (stats == null)
            return;

        SetTmpText(timeText, FormatPlaytime(stats.ElapsedSeconds));
        SetTmpText(smokeDamageText, Mathf.RoundToInt(stats.SmokeDamageTaken).ToString());
        SetTmpText(fireDamageText, Mathf.RoundToInt(stats.FireDamageTaken).ToString());

        var doors = FindObjectsByType<DoorController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int totalDoors = 0;
        int currentlyOpen = 0;
        int hotDoors = 0;
        int hotChecked = 0;
        for (int i = 0; i < doors.Length; i++)
        {
            var d = doors[i];
            if (d == null)
                continue;
            totalDoors++;
            if (d.IsOpen)
                currentlyOpen++;

            if (d.isHot)
            {
                hotDoors++;
                if (d.HasBeenInspected)
                    hotChecked++;
            }
        }

        int openedActions = stats.DoorOpenActions;
        int currentlyNotClosed = currentlyOpen;
        int hotNotChecked = Mathf.Max(0, hotDoors - hotChecked);
        SetTmpText(doorsClosedText, $"{openedActions} / {currentlyNotClosed}");
        SetTmpText(doorsCheckedText, $"{hotChecked} / {hotNotChecked}");
    }

    private void ResolveStatsReferences()
    {
        if (timeText != null && smokeDamageText != null && fireDamageText != null &&
            doorsClosedText != null && doorsCheckedText != null)
            return;

        timeText = timeText != null ? timeText : FindTmpTextByPath("Stats/VariableText/TimeText");
        smokeDamageText = smokeDamageText != null ? smokeDamageText : FindTmpTextByPath("Stats/VariableText/SmokeDamageText");
        fireDamageText = fireDamageText != null ? fireDamageText : FindTmpTextByPath("Stats/VariableText/FireDamageText");
        doorsClosedText = doorsClosedText != null ? doorsClosedText : FindTmpTextByPath("Stats/VariableText/DoorsClosedText");
        doorsCheckedText = doorsCheckedText != null ? doorsCheckedText : FindTmpTextByPath("Stats/VariableText/DoorsCheckedText");
    }

    private TMP_Text FindTmpTextByPath(string path)
    {
        var tr = transform.Find(path);
        return tr != null ? tr.GetComponent<TMP_Text>() : null;
    }

    private static void SetTmpText(TMP_Text label, string value)
    {
        if (label != null)
            label.text = value;
    }

    private static string FormatPlaytime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int mins = total / 60;
        int secs = total % 60;
        return $"{mins} mins, {secs} secs";
    }

    /// <summary>
    /// Increments the SelectedLevel in PlayerPrefs and reloads the game scene.
    /// The GameManager will automatically load the new building configuration.
    /// </summary>
    public void NextLevel()
    {
        int currentLevel = PlayerPrefs.GetInt("SelectedLevel", 0);
        PlayerPrefs.SetInt("SelectedLevel", currentLevel + 1);
        PlayerPrefs.Save();

        Debug.Log($"[EndPanel] Advancing to level index: {currentLevel + 1}");
        ReloadGameScene();
    }

    /// <summary>
    /// Reloads the current scene to restart the level from the beginning.
    /// </summary>
    public void RestartLevel()
    {
        Debug.Log("[EndPanel] Restarting current level.");
        ReloadGameScene();
    }

    /// <summary>
    /// Returns the player to the main menu scene.
    /// </summary>
    public void BackToMenu()
    {
        // Ensure time is running before changing scenes
        Time.timeScale = 1f;
        SceneManager.LoadScene(menuSceneName);
    }

    /// <summary>
    /// Utility to ensure time scale is reset and current scene is reloaded.
    /// </summary>
    private void ReloadGameScene()
    {
        Time.timeScale = 1f;
        // Loads the active scene name to ensure we stay in the gameplay loop
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
