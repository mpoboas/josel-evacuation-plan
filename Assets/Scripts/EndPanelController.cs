using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using TMPro;

/// <summary>
/// Shared UI Toolkit panel configuration (theme + scale) for <see cref="EndPanelController"/> and
/// <see cref="EndPanelToolkitBootstrap"/>.
/// </summary>
internal static class EndPanelUiToolkitSetup
{
    /// <summary>Resources path (no extension) to <c>EndPanelRuntimeTheme.tss</c> — must import Unity's default theme.</summary>
    internal const string RuntimeThemeResourcesPath = "UI/EndPanel/EndPanelRuntimeTheme";

    internal static void ConfigurePanelSettings(PanelSettings ps)
    {
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2Int(1920, 1080);
        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        ps.match = 0.5f;

        var theme = Resources.Load<ThemeStyleSheet>(RuntimeThemeResourcesPath);
        ps.themeStyleSheet = theme;
        if (theme == null)
        {
            Debug.LogWarning(
                "[EndPanel] Assign a runtime theme: expected Resources asset at \"" + RuntimeThemeResourcesPath +
                "\" (ThemeStyleSheet). File: Assets/Resources/UI/EndPanel/EndPanelRuntimeTheme.tss with " +
                "@import url(\"unity-theme://default\"); — reimport if the console still warns.");
        }
    }

    /// <summary>Fills in theme when an existing PanelSettings asset was created without one (e.g. older bootstrap).</summary>
    internal static void EnsureThemeAssigned(UIDocument doc)
    {
        if (doc == null || doc.panelSettings == null)
            return;
        var ps = doc.panelSettings;
        if (ps.themeStyleSheet != null)
            return;
        var theme = Resources.Load<ThemeStyleSheet>(RuntimeThemeResourcesPath);
        if (theme == null)
            return;
        ps.themeStyleSheet = theme;
    }
}

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

    private void Awake()
    {
        // Ensures the panel starts hidden when the game runs
        gameObject.SetActive(false);
    }

    [Header("Stats Text (auto-resolved by default)")]
    public TMP_Text timeText;
    public TMP_Text smokeDamageText;
    public TMP_Text fireDamageText;
    public TMP_Text doorsClosedText;
    public TMP_Text doorsCheckedText;

    private UIDocument _uiDocument;
    private Coroutine _showToolkitPassRoutine;
    private VisualElement _panelEvaluation;
    private VisualElement _panelLogs;
    private UnityEngine.UIElements.Button _tabEvaluation;
    private UnityEngine.UIElements.Button _tabLogs;

    /// <summary>
    /// Call this method to display the end panel, pause the game, and unlock the cursor.
    /// </summary>
    public void Show()
    {
        // OnEnable (movement replay) runs during SetActive before the rest of this method — ensure UIDocument
        // exists first so the visual tree can bind. Bootstrap may be missing from the scene or run too late.
        EnsureUiDocumentForToolkit();
        EndPanelUiToolkitSetup.EnsureThemeAssigned(GetComponent<UIDocument>());

        // Make sure the panel (and any ancestor) is active before starting coroutines.
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        // If this object was inactive since load, Awake runs now and may call SetActive(false) (legacy hide).
        // Awake does not run again on a second activation, so we can recover by activating once more.
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        Time.timeScale = 0f;

        // Unlock and show cursor for button interaction
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        TryWireUiToolkit();
        RefreshStatsUI();
        EnsureMovementReplay();

        if (_showToolkitPassRoutine != null)
            StopCoroutine(_showToolkitPassRoutine);
        _showToolkitPassRoutine = StartCoroutine(ShowDeferredToolkitPass());
    }

    /// <summary>
    /// Creates <see cref="UIDocument"/> + panel settings when absent (same as <see cref="EndPanelToolkitBootstrap"/>).
    /// Must run before <see cref="GameObject.SetActive"/> so <see cref="EndPanelMovementReplay.OnEnable"/> can see the tree.
    /// </summary>
    private void EnsureUiDocumentForToolkit()
    {
        if (GetComponent<UIDocument>() != null)
            return;

        const string resourcesPath = "UI/EndPanel/EndPanel";
        var vta = Resources.Load<VisualTreeAsset>(resourcesPath);
        if (vta == null)
        {
            Debug.LogError(
                $"[EndPanel] Could not load VisualTreeAsset at Resources/{resourcesPath}. " +
                "Expected file: Assets/Resources/UI/EndPanel/EndPanel.uxml",
                this);
            return;
        }

        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        EndPanelUiToolkitSetup.ConfigurePanelSettings(ps);

        var doc = gameObject.AddComponent<UIDocument>();
        doc.visualTreeAsset = vta;
        doc.panelSettings = ps;
        doc.sortingOrder = 100;
    }

    private IEnumerator ShowDeferredToolkitPass()
    {
        // With timeScale == 0, use unscaled waits so this coroutine still advances.
        yield return new WaitForSecondsRealtime(0.02f);
        TryWireUiToolkit();
        RefreshStatsUI();
        var replay = GetComponent<EndPanelMovementReplay>();
        if (replay != null)
            replay.InvalidateUiBindings();
        EnsureMovementReplay();
        yield return new WaitForSecondsRealtime(0.02f);
        TryWireUiToolkit();
        RefreshStatsUI();
        if (replay != null)
            replay.InvalidateUiBindings();
        EnsureMovementReplay();
        _showToolkitPassRoutine = null;
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

    private void TryWireUiToolkit()
    {
        _uiDocument = GetComponent<UIDocument>();
        if (_uiDocument == null)
            return;

        var root = _uiDocument.rootVisualElement;
        if (root == null)
            return;

        var btnRetry = root.Q<Button>("btn-retry");
        if (btnRetry == null)
            return;

        var btnNext = root.Q<Button>("btn-next-level");
        var btnMenu = root.Q<Button>("btn-menu");
        btnRetry.clicked -= RestartLevel;
        btnRetry.clicked += RestartLevel;
        if (btnNext != null)
        {
            btnNext.clicked -= NextLevel;
            btnNext.clicked += NextLevel;
        }
        if (btnMenu != null)
        {
            btnMenu.clicked -= BackToMenu;
            btnMenu.clicked += BackToMenu;
        }

        _tabEvaluation = root.Q<Button>("tab-evaluation");
        _tabLogs = root.Q<Button>("tab-logs");
        _panelEvaluation = root.Q<VisualElement>("panel-evaluation");
        _panelLogs = root.Q<VisualElement>("panel-logs");

        if (_tabEvaluation != null)
        {
            _tabEvaluation.clicked -= ShowEvaluationTab;
            _tabEvaluation.clicked += ShowEvaluationTab;
        }
        if (_tabLogs != null)
        {
            _tabLogs.clicked -= ShowLogsTab;
            _tabLogs.clicked += ShowLogsTab;
        }
    }

    private void ShowEvaluationTab()
    {
        if (_panelEvaluation != null) _panelEvaluation.style.display = DisplayStyle.Flex;
        if (_panelLogs != null) _panelLogs.style.display = DisplayStyle.None;
        if (_tabEvaluation != null) _tabEvaluation.AddToClassList("tab-active");
        if (_tabLogs != null) _tabLogs.RemoveFromClassList("tab-active");
    }

    private void ShowLogsTab()
    {
        if (_panelEvaluation != null) _panelEvaluation.style.display = DisplayStyle.None;
        if (_panelLogs != null) _panelLogs.style.display = DisplayStyle.Flex;
        if (_tabEvaluation != null) _tabEvaluation.RemoveFromClassList("tab-active");
        if (_tabLogs != null) _tabLogs.AddToClassList("tab-active");
    }

    private void RefreshStatsUI()
    {
        ResolveStatsReferences();
        var stats = GameplaySessionStats.Instance;
        if (stats == null)
            return;

        var doors = FindObjectsByType<DoorController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int currentlyOpen = 0;
        int hotDoors = 0;
        int hotChecked = 0;
        for (int i = 0; i < doors.Length; i++)
        {
            var d = doors[i];
            if (d == null)
                continue;
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
        string timeStr = FormatPlaytime(stats.ElapsedSeconds);
        string smokeStr = Mathf.RoundToInt(stats.SmokeDamageTaken).ToString();
        string fireStr = Mathf.RoundToInt(stats.FireDamageTaken).ToString();
        string doorsClosedStr = $"{openedActions} / {currentlyNotClosed}";
        string doorsCheckedStr = $"{hotChecked} / {hotNotChecked}";

        SetTmpText(timeText, timeStr);
        SetTmpText(smokeDamageText, smokeStr);
        SetTmpText(fireDamageText, fireStr);
        SetTmpText(doorsClosedText, doorsClosedStr);
        SetTmpText(doorsCheckedText, doorsCheckedStr);

        RefreshUiToolkitStats(
            timeStr,
            smokeStr,
            fireStr,
            doorsClosedStr,
            doorsCheckedStr,
            stats);
    }

    private void RefreshUiToolkitStats(
        string timeStr,
        string smokeStr,
        string fireStr,
        string doorsClosedStr,
        string doorsCheckedStr,
        GameplaySessionStats stats)
    {
        if (_uiDocument == null)
            _uiDocument = GetComponent<UIDocument>();
        if (_uiDocument == null)
            return;

        var root = _uiDocument.rootVisualElement;
        if (root == null)
            return;

        SetLabelText(root, "stats-time-value", timeStr);
        SetLabelText(root, "stats-smoke-value", smokeStr);
        SetLabelText(root, "stats-fire-value", fireStr);
        SetLabelText(root, "stats-doors-closed-value", doorsClosedStr);
        SetLabelText(root, "stats-doors-checked-value", doorsCheckedStr);

        string tier = ComputeEvaluationTier(stats, doorsCheckedStr);
        SetLabelText(root, "score-value", $"EVALUATION SCORE: {tier}");
    }

    private static void SetLabelText(VisualElement root, string name, string value)
    {
        var label = root.Q<Label>(name);
        if (label != null)
            label.text = value;
    }

    private static string ComputeEvaluationTier(GameplaySessionStats stats, string doorsCheckedDisplay)
    {
        int fire = Mathf.RoundToInt(stats.FireDamageTaken);
        int smoke = Mathf.RoundToInt(stats.SmokeDamageTaken);
        int elapsed = Mathf.RoundToInt(stats.ElapsedSeconds);

        int penalty = 0;
        penalty += fire * 4;
        penalty += smoke * 2;
        penalty += Mathf.Min(35, elapsed / 4);

        if (TryParseSlashPair(doorsCheckedDisplay, out _, out int hotNotChecked) && hotNotChecked > 0)
            penalty += hotNotChecked * 10;

        int score = Mathf.Clamp(100 - penalty, 0, 100);
        if (score >= 82)
            return "EXCELLENT";
        if (score >= 68)
            return "GOOD";
        if (score >= 50)
            return "FAIR";
        return "POOR";
    }

    private static bool TryParseSlashPair(string text, out int left, out int right)
    {
        left = 0;
        right = 0;
        if (string.IsNullOrEmpty(text) || !text.Contains("/"))
            return false;
        var parts = text.Split('/');
        if (parts.Length != 2)
            return false;
        return int.TryParse(parts[0].Trim(), out left) && int.TryParse(parts[1].Trim(), out right);
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
        return $"{mins}:{secs:00}";
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

/// <summary>
/// Adds a <see cref="UIDocument"/> at runtime so the scene does not depend on package script GUIDs.
/// Runs before <see cref="EndPanelController"/> so the visual tree exists when the panel is shown.
/// </summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class EndPanelToolkitBootstrap : MonoBehaviour
{
    [Tooltip("Path passed to Resources.Load (omit extension), relative to a Resources folder.")]
    [SerializeField] string resourcesLayoutPath = "UI/EndPanel/EndPanel";

    [SerializeField] int sortingOrder = 50;

    private void Awake()
    {
        if (GetComponent<UIDocument>() != null)
            return;

        var vta = Resources.Load<VisualTreeAsset>(resourcesLayoutPath);
        if (vta == null)
        {
            Debug.LogError(
                $"[EndPanelToolkitBootstrap] Missing VisualTreeAsset at Resources/{resourcesLayoutPath}.",
                this);
            return;
        }

        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        EndPanelUiToolkitSetup.ConfigurePanelSettings(ps);

        var doc = gameObject.AddComponent<UIDocument>();
        doc.visualTreeAsset = vta;
        doc.panelSettings = ps;
        doc.sortingOrder = sortingOrder;
    }
}
