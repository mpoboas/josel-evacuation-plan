using System;
using System.Collections;
using System.Collections.Generic;
using BuildingSystem;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

/// <summary>
/// One playable "real" floor: several <see cref="BuildingTool"/> child indices (e.g. Floor 0 + Floor 1) that share one timeline map.
/// </summary>
[Serializable]
public sealed class EndPanelLogicalFloorGroup
{
    [Tooltip("Shown in the map badge (e.g. FLOOR 1).")]
    public string displayLabel = "";

    [Tooltip("BuildingTool floor indices for this level, bottom → top (e.g. 0 and 1 when two tool floors are one real storey).")]
    public List<int> buildingFloors = new List<int>();
}

/// <summary>
/// EndPanel replay widget: plays back the player's path trace and a heat map over per-floor
/// top-down snapshots. Self-activates on <c>OnEnable</c> of the panel GameObject and supports
/// auto floor switching tied to the playback timeline.
/// </summary>
[DisallowMultipleComponent]
public class EndPanelMovementReplay : MonoBehaviour
{
    public enum ReplayMode { Path, Heat, Both }

    [Header("Map capture")]
    [Min(128)] public int floorTextureSize = 512;
    [Range(1f, 1.5f)] public float capturePadding = 1.1f;
    [Range(1f, 4f)] public float baseMapBrightness = 1f;
    public Color baseMapBackgroundColor = new Color(0.18f, 0.18f, 0.2f, 1f);

    [Header("Path & heat")]
    public Color pathColor = new Color(0.25f, 0.9f, 1f, 1f);
    [Min(1)] public int pathThickness = 6;
    public Gradient heatGradient;
    [Min(0.05f)] public float heatStampRadius = 1.0f;
    [Range(0f, 1f)] public float heatMaxAlpha = 0.85f;
    [Range(1f, 200f)] public float heatSaturationSamples = 25f;

    [Header("Logical floors (optional)")]
    [Tooltip("Leave empty: each Building floor is its own map/timeline step. When set, each entry merges those building indices into one map so stairs between split tool floors do not swap textures.")]
    public List<EndPanelLogicalFloorGroup> logicalFloorGroups = new List<EndPanelLogicalFloorGroup>();

    [Header("Playback & mode")]
    [Range(5f, 60f)] public float applyRateHz = 20f;
    [Range(-180f, 180f)] public float mapRotationDegrees = -90f;
    public ReplayMode mode = ReplayMode.Both;

    [Header("Legacy uGUI (optional)")]
    [Tooltip("Only used by the old Timeline/MapPanel hierarchy. Leave unset when using UI Toolkit.")]
    [HideInInspector] public RectTransform replayContent;
    [HideInInspector] public RectTransform mapPanel;
    [HideInInspector] public RawImage baseMapImage;
    [HideInInspector] public RawImage heatOverlayImage;
    [HideInInspector] public RawImage pathOverlayImage;
    [HideInInspector] public Text floorLabel;
    [HideInInspector] public UnityEngine.UI.Button pathButton;
    [HideInInspector] public UnityEngine.UI.Button heatButton;
    [HideInInspector] public UnityEngine.UI.Button bothButton;
    [HideInInspector] public UnityEngine.UI.Button replayButton;

    private Coroutine _playbackRoutine;
    private readonly Dictionary<int, FloorRender> _renders = new Dictionary<int, FloorRender>();
    private int _displayedFloor = int.MinValue;
    private int _currentSampleIdx;
    private bool _uiBound;
    private float _lastApplyTime;

    private UnityEngine.UIElements.Image _mapBaseUi;
    private UnityEngine.UIElements.Image _mapHeatUi;
    private UnityEngine.UIElements.Image _mapPathUi;
    private Label _floorLabelUi;
    private VisualElement _mapStackUi;

    private UnityEngine.UIElements.Slider _seekSlider;
    private Label _replayTimeLeftUi;
    private Label _replayTimeRightUi;
    private UnityEngine.UIElements.Button _btnReplayToolkit;
    private UnityEngine.UIElements.Button _btnPlayToolkit;
    private UnityEngine.UIElements.Button _btnPauseToolkit;
    private UnityEngine.UIElements.Button _btnModeTrace;
    private UnityEngine.UIElements.Button _btnModeHeat;
    private UnityEngine.UIElements.Button _btnModeBoth;
    private bool _playbackControlsBound;
    private bool _suppressSeekSliderEvents;
    private bool _paused;
    private float _nextTimelineUiUnscaledTime;
    private float _displayTime;
    private float _clipDisplayDuration = 1f;
    private float _sessionDurationMax = 1f;
    private float _renderedSessionTime = -1f;
    private IReadOnlyList<PlayerMovementRecorder.Sample> _playbackSamples;
    /// <summary>Previous sample's logical floor; used to break path strokes when revisiting a floor after time on another map.</summary>
    private int _lastPaintedSampleLogicalFloor = int.MinValue;

    private readonly Dictionary<int, int> _buildingToLogical = new Dictionary<int, int>();
    private readonly List<List<int>> _logicalBuildingOrder = new List<List<int>>();
    private readonly List<string> _logicalFloorLabels = new List<string>();

    private sealed class FloorRender
    {
        public int level;
        public Texture2D baseMap;
        public Texture2D pathTex;
        public Texture2D heatTex;
        public Color32[] pathBuf;
        public Color32[] heatBuf;
        public float[] heatDensity;
        public float heatMaxSeen;
        public int w, h;
        public float worldMinX;
        public float worldMinZ;
        public float worldSize; // legacy square fallback / heat scale hint
        public Vector3 mapOrigin;
        public Vector3 mapDirX;
        public Vector3 mapDirY;
        public bool useMapPlaneUv;
        public float heatWorldSpan;
        public bool pathDirty;
        public bool heatDirty;
        public Vector2Int lastPixel;
        public bool hasLastPixel;
        public bool baseMapDoNotDestroy;
    }

    private void Reset()
    {
        heatGradient = DefaultHeatGradient();
    }

    private void OnEnable()
    {
        // UIDocument often has no rootVisualElement on the same frame the host is enabled; defer one frame.
        if (GetComponent<UIDocument>() != null)
            StartCoroutine(StartReplayDeferredFrame());
        else
            StartReplay();
    }

    private IEnumerator StartReplayDeferredFrame()
    {
        yield return null;
        if (isActiveAndEnabled && gameObject.activeInHierarchy)
            StartReplay();
    }

    private void OnDisable()
    {
        StopReplay();
    }

    private void OnDestroy()
    {
        StopReplay();
        ReleaseRenders();
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------
    /// <summary>
    /// Clears cached UI references so <see cref="EnsureUI"/> can re-bind after the UIDocument tree appears.
    /// </summary>
    public void InvalidateUiBindings()
    {
        UnwireToolkitPlaybackControls();
        _uiBound = false;
        _mapBaseUi = null;
        _mapHeatUi = null;
        _mapPathUi = null;
        _mapStackUi = null;
        _floorLabelUi = null;
        _seekSlider = null;
        _replayTimeLeftUi = null;
        _replayTimeRightUi = null;
        _btnReplayToolkit = null;
        _btnPlayToolkit = null;
        _btnPauseToolkit = null;
        _btnModeTrace = null;
        _btnModeHeat = null;
        _btnModeBoth = null;
    }

    public void StartReplay()
    {
        // Coroutines require an active GameObject; OnEnable will re-invoke us once the panel activates.
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            return;

        if (!EnsureUI())
            return;

        var rec = PlayerMovementRecorder.Instance;
        if (rec == null || !rec.HasData)
        {
            SetFloorLabel("No movement data");
            return;
        }

        _paused = false;

        if (_playbackRoutine != null) StopCoroutine(_playbackRoutine);

        ReleaseRenders();
        _currentSampleIdx = 0;
        _displayedFloor = int.MinValue;
        _lastApplyTime = 0f;
        _lastPaintedSampleLogicalFloor = int.MinValue;

        BakeAllFloors(rec);
        ApplyMode();

        _playbackRoutine = StartCoroutine(PlaybackDriverCoroutine(rec));
    }

    public void StopReplay()
    {
        if (_playbackRoutine == null)
            return;
        StopCoroutine(_playbackRoutine);
        _playbackRoutine = null;
    }

    public void SetMode(ReplayMode m)
    {
        mode = m;
        ApplyMode();
        UpdateToolkitModeButtonStyles();
        RefreshReplayTexturesForCurrentMode();
    }

    private void ApplyMode()
    {
        bool showPath = mode != ReplayMode.Heat;
        bool showHeat = mode != ReplayMode.Path;
        if (pathOverlayImage != null) pathOverlayImage.enabled = showPath;
        if (heatOverlayImage != null) heatOverlayImage.enabled = showHeat;
        if (_mapPathUi != null)
            _mapPathUi.style.display = showPath ? DisplayStyle.Flex : DisplayStyle.None;
        if (_mapHeatUi != null)
            _mapHeatUi.style.display = showHeat ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ------------------------------------------------------------------
    // Playback loop
    // ------------------------------------------------------------------
    private IEnumerator PlaybackDriverCoroutine(PlayerMovementRecorder rec)
    {
        _playbackSamples = rec.Samples;
        var samples = _playbackSamples;
        _sessionDurationMax = Mathf.Max(0.0001f, rec.SessionDuration);
        _clipDisplayDuration = Mathf.Max(0.01f, rec.SessionDuration);

        _displayTime = 0f;
        _paused = false;
        _renderedSessionTime = -1f;

        ConfigureTimelineUi();
        UpdateToolkitPlayPauseStyles();
        UpdateToolkitModeButtonStyles();

        float applyInterval = 1f / Mathf.Max(1f, applyRateHz);
        const float timelineUiMaxHz = 30f;
        float timelineUiInterval = 1f / timelineUiMaxHz;
        _nextTimelineUiUnscaledTime = 0f;

        if (samples.Count > 0)
            SwitchToFloor(MapBuildingFloorToLogical(samples[0].floorLevel));

        float sessionT0 = Mathf.Clamp(_displayTime, 0f, _sessionDurationMax);
        SyncVisualToSessionTime(samples, sessionT0);
        FlushDirtyTextures();
        _lastApplyTime = Time.unscaledTime;
        SetSeekSliderFromPlayback();
        UpdateTimelineTimeLabels();

        try
        {
            while (isActiveAndEnabled && gameObject.activeInHierarchy)
            {
                if (!_paused)
                {
                    _displayTime += Time.unscaledDeltaTime;
                    if (_displayTime >= _clipDisplayDuration)
                    {
                        _displayTime = _clipDisplayDuration;
                        _paused = true;
                        UpdateToolkitPlayPauseStyles();
                    }
                }

                float sessionT = Mathf.Clamp(_displayTime, 0f, _sessionDurationMax);
                SyncVisualToSessionTime(samples, sessionT);

                if (Time.unscaledTime - _lastApplyTime >= applyInterval)
                {
                    FlushDirtyTextures();
                    _lastApplyTime = Time.unscaledTime;
                }

                if (_paused || Time.unscaledTime >= _nextTimelineUiUnscaledTime)
                {
                    _nextTimelineUiUnscaledTime = Time.unscaledTime + timelineUiInterval;
                    SetSeekSliderFromPlayback();
                    UpdateTimelineTimeLabels();
                }

                yield return null;
            }
        }
        finally
        {
            _playbackRoutine = null;
        }
    }

    private void AdvanceToTime(IReadOnlyList<PlayerMovementRecorder.Sample> samples, float sessionTime)
    {
        while (_currentSampleIdx < samples.Count && samples[_currentSampleIdx].time <= sessionTime)
        {
            PaintSample(samples[_currentSampleIdx]);
            _currentSampleIdx++;
        }
    }

    private void SyncVisualToSessionTime(IReadOnlyList<PlayerMovementRecorder.Sample> samples, float sessionTime)
    {
        sessionTime = Mathf.Clamp(sessionTime, 0f, _sessionDurationMax);
        const float rewindEpsilon = 0.0005f;
        if (_renderedSessionTime < 0f || sessionTime < _renderedSessionTime - rewindEpsilon)
            HardRebuildToSessionTime(samples, sessionTime);
        else
        {
            AdvanceToTime(samples, sessionTime);
            _renderedSessionTime = sessionTime;
        }
    }

    private void HardRebuildToSessionTime(IReadOnlyList<PlayerMovementRecorder.Sample> samples, float sessionTime)
    {
        sessionTime = Mathf.Clamp(sessionTime, 0f, _sessionDurationMax);
        foreach (var kv in _renders)
        {
            var fr = kv.Value;
            if (fr.pathBuf != null)
                Array.Clear(fr.pathBuf, 0, fr.pathBuf.Length);
            if (HeatHasDisplayTarget() && fr.heatDensity != null)
            {
                Array.Clear(fr.heatDensity, 0, fr.heatDensity.Length);
                fr.heatMaxSeen = 0f;
            }

            fr.hasLastPixel = false;
            fr.pathDirty = true;
            fr.heatDirty = HeatHasDisplayTarget();
        }

        _lastPaintedSampleLogicalFloor = int.MinValue;
        _currentSampleIdx = 0;
        _displayedFloor = int.MinValue;
        AdvanceToTime(samples, sessionTime);
        _renderedSessionTime = sessionTime;
    }

    private void RestartPlaybackTimeline()
    {
        var rec = PlayerMovementRecorder.Instance;
        if (rec == null || !rec.HasData)
        {
            StartReplay();
            return;
        }

        if (_renders.Count == 0)
        {
            StartReplay();
            return;
        }

        StopReplay();
        _playbackRoutine = StartCoroutine(PlaybackDriverCoroutine(rec));
    }

    private void ConfigureTimelineUi()
    {
        if (_seekSlider == null)
            return;

        _seekSlider.lowValue = 0f;
        _seekSlider.highValue = Mathf.Max(0.01f, _clipDisplayDuration);
        SetSeekSliderFromPlayback();
        UpdateTimelineTimeLabels();
    }

    private void SetSeekSliderFromPlayback()
    {
        if (_seekSlider == null)
            return;

        _suppressSeekSliderEvents = true;
        _seekSlider.SetValueWithoutNotify(Mathf.Clamp(_displayTime, _seekSlider.lowValue, _seekSlider.highValue));
        _suppressSeekSliderEvents = false;
    }

    private void UpdateTimelineTimeLabels()
    {
        string left = FormatPlaybackTime(_displayTime);
        string right = FormatPlaybackTime(_clipDisplayDuration);
        if (_replayTimeLeftUi != null)
            _replayTimeLeftUi.text = left;
        if (_replayTimeRightUi != null)
            _replayTimeRightUi.text = right;
    }

    private static string FormatPlaybackTime(float seconds)
    {
        if (seconds < 0f)
            seconds = 0f;
        int total = Mathf.FloorToInt(seconds);
        int m = total / 60;
        int s = total % 60;
        return $"{m}:{s:D2}";
    }

    private void OnSeekSliderChanged(ChangeEvent<float> evt)
    {
        if (_suppressSeekSliderEvents || _playbackSamples == null)
            return;

        _displayTime = Mathf.Clamp(evt.newValue, 0f, _clipDisplayDuration);
        _paused = true;
        UpdateToolkitPlayPauseStyles();

        float sessionT = Mathf.Clamp(_displayTime, 0f, _sessionDurationMax);
        SyncVisualToSessionTime(_playbackSamples, sessionT);
        FlushDirtyTextures();
        UpdateTimelineTimeLabels();
    }

    private void OnToolkitPlayClicked()
    {
        _paused = false;
        UpdateToolkitPlayPauseStyles();
    }

    private void OnToolkitPauseClicked()
    {
        _paused = true;
        UpdateToolkitPlayPauseStyles();
    }

    private void OnToolkitModeTraceClicked() => SetMode(ReplayMode.Path);

    private void OnToolkitModeHeatClicked() => SetMode(ReplayMode.Heat);

    private void OnToolkitModeBothClicked() => SetMode(ReplayMode.Both);

    private void UpdateToolkitPlayPauseStyles()
    {
        bool playAccent = _paused;
        if (_btnPlayToolkit != null)
        {
            if (playAccent)
                _btnPlayToolkit.AddToClassList("replay-control-btn-accent");
            else
                _btnPlayToolkit.RemoveFromClassList("replay-control-btn-accent");
        }

        if (_btnPauseToolkit != null)
        {
            if (!playAccent)
                _btnPauseToolkit.AddToClassList("replay-control-btn-accent");
            else
                _btnPauseToolkit.RemoveFromClassList("replay-control-btn-accent");
        }
    }

    private void UpdateToolkitModeButtonStyles()
    {
        void SetOn(UnityEngine.UIElements.Button btn, bool on)
        {
            if (btn == null)
                return;
            if (on)
                btn.AddToClassList("mode-btn-active");
            else
                btn.RemoveFromClassList("mode-btn-active");
        }

        SetOn(_btnModeTrace, mode == ReplayMode.Path);
        SetOn(_btnModeHeat, mode == ReplayMode.Heat);
        SetOn(_btnModeBoth, mode == ReplayMode.Both);
    }

    private void RefreshReplayTexturesForCurrentMode()
    {
        if (_playbackSamples == null || _renders.Count == 0)
            return;
        float sessionT = Mathf.Clamp(_displayTime, 0f, _sessionDurationMax);
        _renderedSessionTime = -1f;
        SyncVisualToSessionTime(_playbackSamples, sessionT);
        FlushDirtyTextures();
    }

    private void PaintSample(PlayerMovementRecorder.Sample s)
    {
        int logical = MapBuildingFloorToLogical(s.floorLevel);
        if (!_renders.TryGetValue(logical, out var fr))
            return;

        if (_lastPaintedSampleLogicalFloor != int.MinValue && _lastPaintedSampleLogicalFloor != logical)
            fr.hasLastPixel = false;

        if (logical != _displayedFloor)
            SwitchToFloor(logical);

        Vector2Int px = WorldToPixel(fr, s.worldPos);
        if (px.x < 0 || px.x >= fr.w || px.y < 0 || px.y >= fr.h)
        {
            fr.hasLastPixel = false;
            _lastPaintedSampleLogicalFloor = logical;
            return;
        }

        if (fr.hasLastPixel)
            DrawLine(fr, fr.lastPixel, px, pathColor, pathThickness);
        else
            DrawDisc(fr.pathBuf, fr.w, fr.h, px.x, px.y, pathThickness, pathColor);

        fr.lastPixel = px;
        fr.hasLastPixel = true;
        fr.pathDirty = true;
        _lastPaintedSampleLogicalFloor = logical;

        if (HeatHasDisplayTarget())
        {
            float span = fr.useMapPlaneUv ? fr.heatWorldSpan : fr.worldSize;
            float radiusPx = Mathf.Max(1f, heatStampRadius / Mathf.Max(0.001f, span) * fr.w);
            StampHeat(fr, px.x, px.y, radiusPx);
            fr.heatDirty = true;
        }
    }

    // ------------------------------------------------------------------
    // Floor display
    // ------------------------------------------------------------------
    private void SwitchToFloor(int level)
    {
        if (level == _displayedFloor) return;
        if (!_renders.TryGetValue(level, out var fr)) return;

        _displayedFloor = level;
        if (baseMapImage != null) baseMapImage.texture = fr.baseMap;
        if (pathOverlayImage != null) pathOverlayImage.texture = fr.pathTex;
        if (heatOverlayImage != null) heatOverlayImage.texture = fr.heatTex;
        if (_mapBaseUi != null) _mapBaseUi.image = fr.baseMap;
        if (_mapPathUi != null) _mapPathUi.image = fr.pathTex;
        if (_mapHeatUi != null) _mapHeatUi.image = fr.heatTex;
        SetFloorLabel(BuildFloorLabelText(level));
    }

    private string BuildFloorLabelText(int logicalFloorIndex)
    {
        var scene = SceneManager.GetActiveScene().name;
        string mapTag = "?";
        if (!string.IsNullOrEmpty(scene))
            mapTag = scene.Length == 1 ? scene.ToUpperInvariant() : char.ToUpperInvariant(scene[0]).ToString();
        if (_logicalFloorLabels.Count > 0 &&
            logicalFloorIndex >= 0 &&
            logicalFloorIndex < _logicalFloorLabels.Count)
            return $"MAP: {mapTag} | {_logicalFloorLabels[logicalFloorIndex]}";
        return $"MAP: {mapTag} | LEVEL {logicalFloorIndex + 1}";
    }

    private int MapBuildingFloorToLogical(int buildingFloorLevel)
    {
        if (logicalFloorGroups == null || logicalFloorGroups.Count == 0)
            return buildingFloorLevel;
        return _buildingToLogical.TryGetValue(buildingFloorLevel, out int logical) ? logical : buildingFloorLevel;
    }

    private void RebuildLogicalFloorMaps(PlayerMovementRecorder rec)
    {
        _buildingToLogical.Clear();
        _logicalBuildingOrder.Clear();
        _logicalFloorLabels.Clear();

        if (logicalFloorGroups == null || logicalFloorGroups.Count == 0)
            return;

        var assigned = new HashSet<int>();
        int logical = 0;
        foreach (var group in logicalFloorGroups)
        {
            if (group == null || group.buildingFloors == null || group.buildingFloors.Count == 0)
                continue;

            var order = new List<int>();
            foreach (int b in group.buildingFloors)
            {
                if (assigned.Contains(b))
                    continue;
                assigned.Add(b);
                _buildingToLogical[b] = logical;
                order.Add(b);
            }

            if (order.Count == 0)
                continue;

            _logicalBuildingOrder.Add(order);
            string lab = string.IsNullOrWhiteSpace(group.displayLabel)
                ? $"LEVEL {logical + 1}"
                : group.displayLabel.Trim();
            _logicalFloorLabels.Add(lab);
            logical++;
        }

        var extras = new List<int>();
        foreach (var fi in rec.Floors)
        {
            if (!assigned.Contains(fi.level))
                extras.Add(fi.level);
        }

        extras.Sort();
        foreach (int b in extras)
        {
            _buildingToLogical[b] = logical;
            _logicalBuildingOrder.Add(new List<int> { b });
            _logicalFloorLabels.Add($"LEVEL {logical + 1}");
            logical++;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (logicalFloorGroups == null || logicalFloorGroups.Count == 0)
            return;
        var seen = new HashSet<int>();
        foreach (var g in logicalFloorGroups)
        {
            if (g?.buildingFloors == null)
                continue;
            foreach (int b in g.buildingFloors)
            {
                if (!seen.Add(b))
                    Debug.LogWarning($"[EndPanelReplay] Building floor index {b} appears in more than one logical floor group.", this);
            }
        }
    }
#endif

    private void SetFloorLabel(string text)
    {
        if (floorLabel != null) floorLabel.text = text;
        if (_floorLabelUi != null) _floorLabelUi.text = text;
    }

    // ------------------------------------------------------------------
    // Baking
    // ------------------------------------------------------------------
    private void BakeAllFloors(PlayerMovementRecorder rec)
    {
        var floors = rec.Floors;
        if (floors.Count == 0)
            return;

        RebuildLogicalFloorMaps(rec);

        if (rec.BuildingTool != null)
            StaticMapGenerator.EnsureOffscreenFloorBakes(rec.BuildingTool, floors);

        if (logicalFloorGroups == null || logicalFloorGroups.Count == 0 || _logicalFloorLabels.Count == 0)
        {
            for (int i = 0; i < floors.Count; i++)
            {
                var fi = floors[i];
                var fr = BakeFloor(fi, rec.BuildingTool);
                if (fr != null)
                    _renders[fi.level] = fr;
            }

            return;
        }

        for (int logical = 0; logical < _logicalFloorLabels.Count; logical++)
        {
            var parts = CollectFloorInfosForLogical(logical, floors);
            if (parts.Count == 0)
                continue;

            FloorRender fr = parts.Count == 1
                ? BakeFloor(parts[0], rec.BuildingTool)
                : BakeMergedLogicalFloor(logical, parts, rec.BuildingTool);
            if (fr != null)
                _renders[logical] = fr;
        }
    }

    private List<PlayerMovementRecorder.FloorInfo> CollectFloorInfosForLogical(
        int logicalIndex,
        IReadOnlyList<PlayerMovementRecorder.FloorInfo> allFloors)
    {
        var list = new List<PlayerMovementRecorder.FloorInfo>();
        if (logicalIndex < 0 || logicalIndex >= _logicalBuildingOrder.Count)
            return list;

        var byLevel = new Dictionary<int, PlayerMovementRecorder.FloorInfo>(allFloors.Count);
        for (int i = 0; i < allFloors.Count; i++)
            byLevel[allFloors[i].level] = allFloors[i];

        var order = _logicalBuildingOrder[logicalIndex];
        for (int i = 0; i < order.Count; i++)
        {
            if (byLevel.TryGetValue(order[i], out var fi))
                list.Add(fi);
        }

        return list;
    }

    /// <summary>
    /// Builds one logical-floor map by compositing the same wall-board textures used for each Building floor index.
    /// </summary>
    private FloorRender BakeMergedLogicalFloor(
        int logicalIndex,
        List<PlayerMovementRecorder.FloorInfo> parts,
        BuildingTool buildingTool)
    {
        float planeY = 0f;
        for (int i = 0; i < parts.Count; i++)
            planeY += parts[i].worldBounds.center.y;
        planeY /= Mathf.Max(1, parts.Count);

        var layers = new List<(Texture2D tex, Vector3 v00, Vector3 v10, Vector3 v01)>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            var fi = parts[i];
            float py = fi.worldBounds.center.y;
            if (!StaticMapGenerator.TryResolveEndPanelFloorMapPixels(
                    buildingTool,
                    fi.level,
                    fi.root,
                    py,
                    out var tex,
                    out var v0,
                    out var v1,
                    out var v2))
            {
                Debug.LogError(
                    $"[EndPanelReplay] No wall map for building floor {fi.level} (logical group {logicalIndex}). " +
                    "Add a StaticMapGenerator wall board for that floor, or ensure the floor exists under BuildingTool.",
                    this);
                return null;
            }

            layers.Add((tex, v0, v1, v2));
        }

        const int mergedCompositeMaxDim = 512;
        int dim = floorTextureSize;
        for (int i = 0; i < layers.Count; i++)
            dim = Mathf.Max(dim, layers[i].tex.width);
        dim = Mathf.Min(dim, mergedCompositeMaxDim);

        if (!TryCompositeWallMapLayers(layers, dim, planeY, capturePadding, baseMapBackgroundColor, out var merged, out var mv00, out var mv10, out var mv01))
        {
            Debug.LogError($"[EndPanelReplay] Failed to composite wall maps for logical group {logicalIndex}.", this);
            return null;
        }

        ApplyBrightness(merged, baseMapBrightness);
        return CreateFloorRenderWithMap(logicalIndex, dim, merged, mv00, mv10, mv01, baseMapDoNotDestroy: false);
    }

    private FloorRender BakeFloor(PlayerMovementRecorder.FloorInfo fi, BuildingTool buildingTool)
    {
        float planeY = fi.worldBounds.center.y;
        if (!StaticMapGenerator.TryResolveEndPanelFloorMapPixels(
                buildingTool,
                fi.level,
                fi.root,
                planeY,
                out var baseTex,
                out var v00,
                out var v10,
                out var v01))
        {
            Debug.LogError(
                $"[EndPanelReplay] No wall map for building floor {fi.level}. " +
                "Add a StaticMapGenerator wall board for that floor index, or ensure the floor exists under BuildingTool.",
                this);
            return null;
        }

        int dim = baseTex.width;
        return CreateFloorRenderWithMap(fi.level, dim, baseTex, v00, v10, v01, baseMapDoNotDestroy: true);
    }

    private static FloorRender CreateFloorRenderWithMap(
        int level,
        int dim,
        Texture2D baseMap,
        Vector3 v00,
        Vector3 v10,
        Vector3 v01,
        bool baseMapDoNotDestroy)
    {
        int n = dim;
        var fr = new FloorRender
        {
            level = level,
            w = n,
            h = n,
            baseMap = baseMap,
            baseMapDoNotDestroy = baseMapDoNotDestroy,
            pathBuf = new Color32[n * n],
            heatBuf = new Color32[n * n],
            heatDensity = new float[n * n],
            heatMaxSeen = 0f,
        };
        ApplyMapPlaneToFloorRender(fr, v00, v10, v01);
        fr.pathTex = CreateOverlayTexture(n);
        fr.heatTex = CreateOverlayTexture(n);
        return fr;
    }

    private static void ApplyMapPlaneToFloorRender(FloorRender fr, Vector3 v00, Vector3 v10, Vector3 v01)
    {
        fr.mapOrigin = v00;
        fr.mapDirX = v10 - v00;
        fr.mapDirY = v01 - v00;
        fr.useMapPlaneUv = true;
        fr.heatWorldSpan = Mathf.Max(fr.mapDirX.magnitude, fr.mapDirY.magnitude);
        fr.worldMinX = v00.x;
        fr.worldMinZ = v00.z;
        fr.worldSize = Mathf.Max(fr.heatWorldSpan, 0.01f);
    }

    /// <summary>
    /// Wall-board bakes often call <c>Apply(..., makeNoLongerReadable: true)</c>; compositing needs a CPU-side copy.
    /// </summary>
    private static bool TryExtractReadableColor32Grid(Texture2D tex, out Color32[] pixels)
    {
        pixels = null;
        if (tex == null || tex.width < 1 || tex.height < 1)
            return false;

        if (tex.isReadable)
        {
            try
            {
                pixels = tex.GetPixels32();
                return pixels != null && pixels.Length == tex.width * tex.height;
            }
            catch (UnityException)
            {
                // Fall through — importer/runtime flags can disagree with isReadable.
            }
        }

        int w = tex.width;
        int h = tex.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var cpu = new Texture2D(w, h, TextureFormat.RGBA32, false);
        cpu.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        cpu.Apply(false, false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        pixels = cpu.GetPixels32();
        UnityEngine.Object.Destroy(cpu);
        return pixels != null && pixels.Length == w * h;
    }

    private static bool TryCompositeWallMapLayers(
        List<(Texture2D tex, Vector3 v00, Vector3 v10, Vector3 v01)> layers,
        int dim,
        float planeY,
        float padding,
        Color background,
        out Texture2D merged,
        out Vector3 outV00,
        out Vector3 outV10,
        out Vector3 outV01)
    {
        merged = null;
        outV00 = outV10 = outV01 = default;
        if (layers == null || layers.Count == 0)
            return false;

        // Anchor the composite on the layer with the largest world footprint. All wall maps for the same logical floor
        // describe roughly the same building footprint, so picking the largest gives us a square canvas that already
        // contains the per-floor textures without any extra zoom-out (which is what made the content shrink into a
        // corner before).
        int anchor = 0;
        float bestArea = -1f;
        for (int li = 0; li < layers.Count; li++)
        {
            var (_, a, b, c) = layers[li];
            float area = (b - a).magnitude * (c - a).magnitude;
            if (area > bestArea)
            {
                bestArea = area;
                anchor = li;
            }
        }

        var anchorLayer = layers[anchor];
        Vector3 av00 = anchorLayer.v00;
        Vector3 av10 = anchorLayer.v10;
        Vector3 av01 = anchorLayer.v01;
        outV00 = new Vector3(av00.x, planeY, av00.z);
        outV10 = new Vector3(av10.x, planeY, av10.z);
        outV01 = new Vector3(av01.x, planeY, av01.z);

        var bg = (Color32)background;
        var scratch = new (Color32[] px, int w, int h, Vector3 v00, Vector3 v10, Vector3 v01)[layers.Count];
        for (int li = 0; li < layers.Count; li++)
        {
            var (tex, v0, v1, v2) = layers[li];
            if (!TryExtractReadableColor32Grid(tex, out var px))
                return false;
            scratch[li] = (px, tex.width, tex.height, v0, v1, v2);
        }

        var dst = new Color32[dim * dim];
        for (int i = 0; i < dst.Length; i++)
            dst[i] = bg;

        for (int iy = 0; iy < dim; iy++)
        {
            float v = (iy + 0.5f) / dim;
            for (int ix = 0; ix < dim; ix++)
            {
                float u = (ix + 0.5f) / dim;
                Vector3 world = outV00 + u * (outV10 - outV00) + v * (outV01 - outV00);
                float wx = world.x;
                float wz = world.z;
                int dstIdx = iy * dim + ix;
                for (int li = 0; li < scratch.Length; li++)
                {
                    var sl = scratch[li];
                    if (!TryWorldXZToNormalizedUvInQuad(wx, wz, sl.v00, sl.v10, sl.v01, out float nu, out float nv))
                        continue;
                    dst[dstIdx] = SampleWallMapBilinear(sl.px, sl.w, sl.h, nu, nv);
                }
            }
        }

        merged = new Texture2D(dim, dim, TextureFormat.RGBA32, false);
        merged.SetPixels32(dst);
        merged.Apply(false, false);
        merged.wrapMode = TextureWrapMode.Clamp;
        merged.filterMode = FilterMode.Bilinear;
        return true;
    }

    private static bool TryWorldXZToNormalizedUvInQuad(
        float wx,
        float wz,
        Vector3 v00,
        Vector3 v10,
        Vector3 v01,
        out float u,
        out float v)
    {
        u = v = 0f;
        Vector2 ex = new Vector2(v10.x - v00.x, v10.z - v00.z);
        Vector2 ey = new Vector2(v01.x - v00.x, v01.z - v00.z);
        Vector2 du = new Vector2(wx - v00.x, wz - v00.z);
        float det = ex.x * ey.y - ex.y * ey.x;
        if (Mathf.Abs(det) < 1e-10f)
            return false;
        float uu = (du.x * ey.y - du.y * ey.x) / det;
        float vv = (ex.x * du.y - ex.y * du.x) / det;
        if (uu < -0.02f || uu > 1.02f || vv < -0.02f || vv > 1.02f)
            return false;
        u = Mathf.Clamp01(uu);
        v = Mathf.Clamp01(vv);
        return true;
    }

    private static Color32 SampleWallMapBilinear(Color32[] px, int w, int h, float u, float v)
    {
        float x = u * (w - 1);
        float y = v * (h - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, w - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, w - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, h - 1);
        int y1 = Mathf.Clamp(y0 + 1, 0, h - 1);
        float tx = x - x0;
        float ty = y - y0;
        Color c00 = px[y0 * w + x0];
        Color c10 = px[y0 * w + x1];
        Color c01 = px[y1 * w + x0];
        Color c11 = px[y1 * w + x1];
        Color c = Color.Lerp(Color.Lerp(c00, c10, tx), Color.Lerp(c01, c11, tx), ty);
        return (Color32)c;
    }

    private static Texture2D CreateOverlayTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var clear = new Color32[size * size];
        // Color32 default is transparent black.
        tex.SetPixels32(clear);
        tex.Apply(false, false);
        return tex;
    }

    private static void ApplyBrightness(Texture2D tex, float brightness)
    {
        if (tex == null || brightness <= 1.001f)
            return;

        var px = tex.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            Color32 c = px[i];
            c.r = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * brightness), 0, 255);
            c.g = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * brightness), 0, 255);
            c.b = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * brightness), 0, 255);
            px[i] = c;
        }
        tex.SetPixels32(px);
    }

    // ------------------------------------------------------------------
    // Rendering primitives (CPU)
    // ------------------------------------------------------------------
    private static Vector2Int WorldToPixel(FloorRender fr, Vector3 world)
    {
        if (fr.useMapPlaneUv)
        {
            var ex = new Vector2(fr.mapDirX.x, fr.mapDirX.z);
            var ey = new Vector2(fr.mapDirY.x, fr.mapDirY.z);
            var du = new Vector2(world.x - fr.mapOrigin.x, world.z - fr.mapOrigin.z);
            float det = ex.x * ey.y - ex.y * ey.x;
            if (Mathf.Abs(det) > 1e-8f)
            {
                float uu = (du.x * ey.y - du.y * ey.x) / det;
                float vv = (ex.x * du.y - ex.y * du.x) / det;
                int x = Mathf.Clamp(Mathf.RoundToInt(uu * (fr.w - 1)), 0, fr.w - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(vv * (fr.h - 1)), 0, fr.h - 1);
                return new Vector2Int(x, y);
            }
        }

        float u = (world.x - fr.worldMinX) / Mathf.Max(0.0001f, fr.worldSize);
        float v = (world.z - fr.worldMinZ) / Mathf.Max(0.0001f, fr.worldSize);
        int x0 = Mathf.Clamp(Mathf.RoundToInt(u * (fr.w - 1)), 0, fr.w - 1);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(v * (fr.h - 1)), 0, fr.h - 1);
        return new Vector2Int(x0, y0);
    }

    private static void DrawLine(FloorRender fr, Vector2Int a, Vector2Int b, Color color, int thickness)
    {
        int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        int safety = fr.w * fr.h;
        while (safety-- > 0)
        {
            DrawDisc(fr.pathBuf, fr.w, fr.h, x0, y0, thickness, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private static void DrawDisc(Color32[] buf, int w, int h, int cx, int cy, int radius, Color color)
    {
        int r2 = radius * radius;
        var c = (Color32)color;
        for (int y = -radius; y <= radius; y++)
        {
            int py = cy + y;
            if (py < 0 || py >= h) continue;
            int row = py * w;
            for (int x = -radius; x <= radius; x++)
            {
                int px = cx + x;
                if (px < 0 || px >= w) continue;
                if (x * x + y * y > r2) continue;
                buf[row + px] = c;
            }
        }
    }

    private static void StampHeat(FloorRender fr, int cx, int cy, float radiusPx)
    {
        int r = Mathf.Max(1, Mathf.CeilToInt(radiusPx));
        float invR2 = 1f / (radiusPx * radiusPx);

        for (int y = -r; y <= r; y++)
        {
            int py = cy + y;
            if (py < 0 || py >= fr.h) continue;
            int row = py * fr.w;
            for (int x = -r; x <= r; x++)
            {
                int px = cx + x;
                if (px < 0 || px >= fr.w) continue;
                float d2 = (x * x + y * y) * invR2;
                if (d2 > 1f) continue;
                // Gaussian-ish falloff: 1 at center, 0 at edge.
                float k = 1f - d2;
                k *= k;
                fr.heatDensity[row + px] += k;
                if (fr.heatDensity[row + px] > fr.heatMaxSeen)
                    fr.heatMaxSeen = fr.heatDensity[row + px];
            }
        }
    }

    private void FlushDirtyTextures()
    {
        foreach (var kv in _renders)
        {
            var fr = kv.Value;
            if (fr.pathDirty && fr.pathTex != null)
            {
                fr.pathTex.SetPixels32(fr.pathBuf);
                fr.pathTex.Apply(false, false);
                fr.pathDirty = false;
            }
            if (fr.heatDirty && fr.heatTex != null)
            {
                if (HeatHasDisplayTarget())
                {
                    ComposeHeatBuffer(fr);
                    fr.heatTex.SetPixels32(fr.heatBuf);
                    fr.heatTex.Apply(false, false);
                }
                fr.heatDirty = false;
            }
        }
    }

    private void ComposeHeatBuffer(FloorRender fr)
    {
        float sat = Mathf.Max(1f, heatSaturationSamples);
        var density = fr.heatDensity;
        var buf = fr.heatBuf;
        var g = heatGradient ?? DefaultHeatGradient();

        int count = density.Length;
        for (int i = 0; i < count; i++)
        {
            float d = density[i];
            if (d <= 0f)
            {
                buf[i] = default;
                continue;
            }
            float n = Mathf.Clamp01(d / sat);
            var col = g.Evaluate(n);
            col.a *= heatMaxAlpha * Mathf.Clamp01(n * 1.2f);
            buf[i] = col;
        }
    }

    // ------------------------------------------------------------------
    // Lifecycle helpers
    // ------------------------------------------------------------------
    private void ReleaseRenders()
    {
        foreach (var kv in _renders)
        {
            var fr = kv.Value;
            if (fr.baseMap != null && !fr.baseMapDoNotDestroy)
                Destroy(fr.baseMap);
            if (fr.pathTex != null) Destroy(fr.pathTex);
            if (fr.heatTex != null) Destroy(fr.heatTex);
        }
        _renders.Clear();
        _displayedFloor = int.MinValue;
        _currentSampleIdx = 0;

        if (_mapBaseUi != null) _mapBaseUi.image = null;
        if (_mapPathUi != null) _mapPathUi.image = null;
        if (_mapHeatUi != null) _mapHeatUi.image = null;
    }

    // ------------------------------------------------------------------
    // UI scaffolding
    // ------------------------------------------------------------------
    private bool HeatHasDisplayTarget()
    {
        if (mode == ReplayMode.Path)
            return false;
        return heatOverlayImage != null || _mapHeatUi != null;
    }

    private void TryBindUiToolkit()
    {
        var doc = GetComponent<UIDocument>();
        if (doc == null)
            return;

        var root = doc.rootVisualElement;
        if (root == null)
            return;

        _mapStackUi = root.Q<VisualElement>("map-stack");
        _mapBaseUi = root.Q<UnityEngine.UIElements.Image>("map-base");
        _mapHeatUi = root.Q<UnityEngine.UIElements.Image>("map-heat");
        _mapPathUi = root.Q<UnityEngine.UIElements.Image>("map-path");
        _floorLabelUi = root.Q<Label>("map-floor-label");

        _seekSlider = root.Q<UnityEngine.UIElements.Slider>("replay-seek-slider");
        _replayTimeLeftUi = root.Q<Label>("replay-time-left");
        _replayTimeRightUi = root.Q<Label>("replay-time-right");
        _btnReplayToolkit = root.Q<UnityEngine.UIElements.Button>("btn-replay-restart");
        _btnPlayToolkit = root.Q<UnityEngine.UIElements.Button>("btn-play");
        _btnPauseToolkit = root.Q<UnityEngine.UIElements.Button>("btn-pause");
        _btnModeTrace = root.Q<UnityEngine.UIElements.Button>("btn-mode-trace");
        _btnModeHeat = root.Q<UnityEngine.UIElements.Button>("btn-mode-heat");
        _btnModeBoth = root.Q<UnityEngine.UIElements.Button>("btn-mode-both");
    }

    private void TryBindToolkitPlaybackControls()
    {
        if (_playbackControlsBound)
            return;
        if (_seekSlider == null)
            return;

        _seekSlider.RegisterValueChangedCallback(OnSeekSliderChanged);
        if (_btnReplayToolkit != null)
            _btnReplayToolkit.clicked += RestartPlaybackTimeline;
        if (_btnPlayToolkit != null)
            _btnPlayToolkit.clicked += OnToolkitPlayClicked;
        if (_btnPauseToolkit != null)
            _btnPauseToolkit.clicked += OnToolkitPauseClicked;
        if (_btnModeTrace != null)
            _btnModeTrace.clicked += OnToolkitModeTraceClicked;
        if (_btnModeHeat != null)
            _btnModeHeat.clicked += OnToolkitModeHeatClicked;
        if (_btnModeBoth != null)
            _btnModeBoth.clicked += OnToolkitModeBothClicked;

        _playbackControlsBound = true;
    }

    private void UnwireToolkitPlaybackControls()
    {
        if (!_playbackControlsBound)
            return;

        if (_seekSlider != null)
            _seekSlider.UnregisterValueChangedCallback(OnSeekSliderChanged);
        if (_btnReplayToolkit != null)
            _btnReplayToolkit.clicked -= RestartPlaybackTimeline;
        if (_btnPlayToolkit != null)
            _btnPlayToolkit.clicked -= OnToolkitPlayClicked;
        if (_btnPauseToolkit != null)
            _btnPauseToolkit.clicked -= OnToolkitPauseClicked;
        if (_btnModeTrace != null)
            _btnModeTrace.clicked -= OnToolkitModeTraceClicked;
        if (_btnModeHeat != null)
            _btnModeHeat.clicked -= OnToolkitModeHeatClicked;
        if (_btnModeBoth != null)
            _btnModeBoth.clicked -= OnToolkitModeBothClicked;

        _playbackControlsBound = false;
    }

    private bool EnsureUI()
    {
        if (_uiBound)
        {
            if (mapPanel != null)
                mapPanel.localEulerAngles = new Vector3(0f, 0f, mapRotationDegrees);
            ApplyMapStackRotation();
            return true;
        }

        TryBindUiToolkit();

        // Resolve references from scene hierarchy under EndPanel/Timeline.
        if (replayContent == null)
        {
            var t = transform.Find("Timeline");
            if (t != null) replayContent = t as RectTransform;
        }
        if (replayContent == null)
            replayContent = transform as RectTransform;

        if (mapPanel == null)
        {
            var t = replayContent != null ? replayContent.Find("MapPanel") : null;
            if (t != null) mapPanel = t as RectTransform;
        }
        if (baseMapImage == null)
            baseMapImage = replayContent != null ? replayContent.Find("MapPanel/BaseMap")?.GetComponent<RawImage>() : null;
        if (heatOverlayImage == null)
            heatOverlayImage = replayContent != null ? replayContent.Find("MapPanel/HeatOverlay")?.GetComponent<RawImage>() : null;
        if (pathOverlayImage == null)
            pathOverlayImage = replayContent != null ? replayContent.Find("MapPanel/PathOverlay")?.GetComponent<RawImage>() : null;
        if (floorLabel == null)
            floorLabel = replayContent != null ? replayContent.Find("FloorLabel")?.GetComponent<Text>() : null;
        if (pathButton == null)
            pathButton = replayContent != null ? replayContent.Find("Buttons/PathButton")?.GetComponent<UnityEngine.UI.Button>() : null;
        if (heatButton == null)
            heatButton = replayContent != null ? replayContent.Find("Buttons/HeatButton")?.GetComponent<UnityEngine.UI.Button>() : null;
        if (bothButton == null)
            bothButton = replayContent != null ? replayContent.Find("Buttons/BothButton")?.GetComponent<UnityEngine.UI.Button>() : null;
        if (replayButton == null)
            replayButton = replayContent != null ? replayContent.Find("Buttons/ReplayButton")?.GetComponent<UnityEngine.UI.Button>() : null;

        if (mapPanel != null)
            mapPanel.localEulerAngles = new Vector3(0f, 0f, mapRotationDegrees);
        ApplyMapStackRotation();

        bool hasUguiMap = baseMapImage != null && pathOverlayImage != null && heatOverlayImage != null;
        bool hasToolkitMap = _mapBaseUi != null && _mapPathUi != null;
        if (!hasUguiMap && !hasToolkitMap)
        {
            Debug.LogError(
                "[EndPanelReplay] Missing map UI (need uGUI Base/Heat/Path RawImages or UI Toolkit map-base + map-path).",
                this);
            return false;
        }

        bool hasAllModeButtons = pathButton != null && heatButton != null && bothButton != null;
        if (hasAllModeButtons)
        {
            pathButton.onClick.RemoveAllListeners();
            heatButton.onClick.RemoveAllListeners();
            bothButton.onClick.RemoveAllListeners();
            pathButton.onClick.AddListener(() => SetMode(ReplayMode.Path));
            heatButton.onClick.AddListener(() => SetMode(ReplayMode.Heat));
            bothButton.onClick.AddListener(() => SetMode(ReplayMode.Both));
        }

        if (replayButton != null)
        {
            replayButton.onClick.RemoveAllListeners();
            replayButton.onClick.AddListener(StartReplay);
        }

        if (heatGradient == null) heatGradient = DefaultHeatGradient();

        TryBindToolkitPlaybackControls();

        _uiBound = true;
        ApplyMode();
        UpdateToolkitModeButtonStyles();
        UpdateToolkitPlayPauseStyles();
        return true;
    }

    private void ApplyMapStackRotation()
    {
        if (_mapStackUi == null)
            return;
        _mapStackUi.style.rotate = new Rotate(new Angle(mapRotationDegrees));
        _mapStackUi.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50), 0);
    }

    private static Gradient DefaultHeatGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0f, 0.2f, 1f), 0f),
                new GradientColorKey(new Color(0f, 1f, 1f), 0.35f),
                new GradientColorKey(new Color(1f, 1f, 0f), 0.65f),
                new GradientColorKey(new Color(1f, 0.2f, 0f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0.25f, 0f),
                new GradientAlphaKey(0.65f, 0.5f),
                new GradientAlphaKey(1f, 1f),
            });
        return g;
    }
}
