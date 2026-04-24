using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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

    [Header("Panel Configuration")]
    public TMP_Text gameText;
    public Button nextButton;
    public GameObject hudCanvas;

    [Header("Stats Text (auto-resolved by default)")]
    public TMP_Text timeText;
    public TMP_Text smokeDamageText;
    public TMP_Text fireDamageText;
    public TMP_Text doorsClosedText;
    public TMP_Text doorsCheckedText;
    public Slider scoreSlider;
    public TMP_Text scorePercentageText;

    [Header("Internal State (for debugging)")]
    private bool _shown = false;

    private void Awake()
    {
        Debug.Log($"[EndPanel] Awake() called on '{gameObject.name}', _shown={_shown}");
        // Hide at scene start, but not when Show() triggers activation
        if (!_shown){
            gameObject.SetActive(false);
            Debug.Log($"[EndPanel] '{gameObject.name}' activeSelf={gameObject.activeSelf}, activeInHierarchy={gameObject.activeInHierarchy}");
        }
    }

    private void OnEnable()
    {
        // Whenever this panel is enabled, ensure the HUD is hidden
        if (_shown) 
        {
            Debug.Log("[EndPanel] Panel Enabled. Hiding HUD...");
            HideHUD();
        }
    }

    /// <summary>
    /// Call this method to display the end panel, pause the game, and unlock the cursor.
    /// </summary>
    public void Show(bool reachedGoal, bool activateGameObject = true)
    {
        Debug.Log($"[EndPanel] Show() called. reachedGoal={reachedGoal}, activate={activateGameObject}");

        var stats = GameplaySessionStats.Instance;
        if (stats == null) return;

        _shown = true;
        ResolveUIReferences();

        if (activateGameObject)
        {
            gameObject.SetActive(true);
        }

        Time.timeScale = 0f;

        // Check for safety and time goals
        var gm = FindAnyObjectByType<GameManager>();
        float targetTime = 9999f;
        float maxSmoke = 9999f;
        float maxFire = 9999f;
        int reqClosed = 0;
        int reqChecked = 0;

        if (gm != null && gm.levels != null)
        {
            int currentLevelIndex = PlayerPrefs.GetInt("SelectedLevel", 0);
            if (currentLevelIndex >= 0 && currentLevelIndex < gm.levels.Length)
            {
                var level = gm.levels[currentLevelIndex];
                targetTime = level.targetTimeSeconds;
                maxSmoke = level.maxSmokeDamageAllowed;
                maxFire = level.maxFireDamageAllowed;
                reqClosed = level.minDoorsClosedRequired;
                reqChecked = level.minDoorsCheckedRequired;
            }
        }

        // Calculate actual door stats to verify goals (consistent with RefreshStatsUI)
        int currentlyClosedThatWereOpened = 0;
        var allDoors = FindObjectsByType<DoorController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var door in allDoors)
        {
            if (door != null && stats.OpenedDoorIdsSet.Contains(door.GetInstanceID()) && !door.IsOpen)
                currentlyClosedThatWereOpened++;
        }

        bool timingOk = stats.ElapsedSeconds <= targetTime;
        bool smokeOk = stats.SmokeDamageTaken <= maxSmoke;
        bool fireOk = stats.FireDamageTaken <= maxFire;
        bool closedOk = currentlyClosedThatWereOpened >= reqClosed;
        bool checkedOk = stats.HeatCheckedDoorCount >= reqChecked;

        bool goalsMet = timingOk && smokeOk && fireOk && closedOk && checkedOk;

        // Unlock and show cursor for button interaction
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Show/hide next level button based on outcome and level availability
        if (nextButton != null)
        {
            bool hasNextLevel = true;
            if (gm != null && gm.levels != null)
            {
                int currentLevelIndex = PlayerPrefs.GetInt("SelectedLevel", 0);
                hasNextLevel = (currentLevelIndex + 1) < gm.levels.Length;
            }

            // Only interactable if player reached the goal AND ALL safety goals were met AND there is a next level
            nextButton.interactable = reachedGoal && goalsMet && hasNextLevel;
        }

        // Update status text
        if (gameText != null)
        {
            if (!reachedGoal)
            {
                gameText.text = "You Died";
            }
            else
            {
                gameText.text = goalsMet ? "Successful Evacuation" : "Ineffective Evacuation";
            }
        }

        // --- New: Score Percentage Calculation (20% per category) ---
        float score = 0f;

        // 1. Timing Score (20%): Full 20 if ok, drops linearly if slower (penalty of 1% per 10% extra time)
        if (timingOk) score += 20f;
        else score += Mathf.Max(0f, 20f - (stats.ElapsedSeconds - targetTime) / (targetTime * 0.1f));

        // 2. Smoke Score (20%): Full 20 if 0 damage, drops to 0 if at max allowed
        score += Mathf.Max(0f, 20f * (1f - (stats.SmokeDamageTaken / maxSmoke)));

        // 3. Fire Score (20%): Full 20 if 0 damage, drops to 0 if at max allowed
        score += Mathf.Max(0f, 20f * (1f - (stats.FireDamageTaken / maxFire)));

        // 4. Doors Closed Score (20%): Ratio of closed vs required
        if (reqClosed > 0) score += Mathf.Clamp((float)currentlyClosedThatWereOpened / reqClosed, 0f, 1f) * 20f;
        else score += 20f; // Free points if none required

        // 5. Doors Checked Score (20%): Ratio of checked vs required
        if (reqChecked > 0) score += Mathf.Clamp((float)stats.HeatCheckedDoorCount / reqChecked, 0f, 1f) * 20f;
        else score += 20f; // Free points if none required

        if (scoreSlider != null)
        {
            scoreSlider.value = score / 100f;
        }

        if (scorePercentageText != null)
        {
            scorePercentageText.text = $"{Mathf.RoundToInt(score)}%";
        }
        // -------------------------------------------------------------

        RefreshStatsUI();
        EnsureMovementReplay();

        Debug.Log($"[EndPanel] Panel displayed. Final Score: {score}%");
    }

    private void HideHUD()
    {
        Debug.Log("[EndPanel] Hiding HUD...");
        if (hudCanvas != null)
        {
            hudCanvas.SetActive(false);
            Debug.Log("[EndPanel] HUD_Canvas hidden via inspector reference.");
        }
        else
        {
            // Fallback: search for HUD_Canvas if not assigned
            var hud = GameObject.Find("HUD_Canvas");
            if (hud != null)
            {
                hud.SetActive(false);
                Debug.Log("[EndPanel] HUD_Canvas hidden via fallback search.");
            }
        }
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

        // DOOR STATS CALCULATION
        int totalOpened = stats.OpenedDoorCount;
        int currentlyClosedThatWereOpened = 0;
        int totalChecked = stats.HeatCheckedDoorCount;

        // We find all doors to see their current state
        var allDoors = FindObjectsByType<DoorController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var openedIds = stats.OpenedDoorIdsSet;

        foreach (var door in allDoors)
        {
            if (door == null) continue;
            
            // If this specific door was opened at some point during the session
            if (openedIds.Contains(door.GetInstanceID()))
            {
                // And if it is currently closed
                if (!door.IsOpen)
                {
                    currentlyClosedThatWereOpened++;
                }
            }
        }

        // Display results: (Closed Doors / Total Opened) and (Checked Doors / Total Opened)
        // If 0 were opened, we show 0/0 to avoid confusion
        SetTmpText(doorsClosedText, $"{currentlyClosedThatWereOpened} / {totalOpened}");
        SetTmpText(doorsCheckedText, $"{totalChecked} / {totalOpened}");
    }

    private void ResolveUIReferences()
    {
        gameText    = gameText    ?? FindTmpTextByPath("GameText");
        nextButton  = nextButton  ?? transform.Find("Buttons/NextButton")?.GetComponent<Button>();
    }

    private void ResolveStatsReferences()
    {
        if (timeText != null && smokeDamageText != null && fireDamageText != null &&
            doorsClosedText != null && doorsCheckedText != null)
            return;

        timeText         = timeText         ?? FindTmpTextByPath("Stats/VariableText/TimeText");
        smokeDamageText  = smokeDamageText  ?? FindTmpTextByPath("Stats/VariableText/SmokeDamageText");
        fireDamageText   = fireDamageText   ?? FindTmpTextByPath("Stats/VariableText/FireDamageText");
        doorsClosedText  = doorsClosedText  ?? FindTmpTextByPath("Stats/VariableText/DoorsClosedText");
        doorsCheckedText = doorsCheckedText ?? FindTmpTextByPath("Stats/VariableText/DoorsCheckedText");
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
        return $"{mins}:{secs}";
    }

    /// <summary>
    /// Increments the SelectedLevel in PlayerPrefs and reloads the game scene.
    /// The GameManager will automatically load the new building configuration.
    /// </summary>
    public void NextLevel()
    {
        int currentLevel = PlayerPrefs.GetInt("SelectedLevel", 0);
        Debug.Log($"[EndPanel] Current level: {currentLevel}");
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
