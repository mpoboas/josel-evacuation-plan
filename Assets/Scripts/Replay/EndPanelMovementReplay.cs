using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EndPanel replay widget: plays back the player's path trace and a heat map over per-floor
/// top-down snapshots. Self-activates on <c>OnEnable</c> of the panel GameObject and supports
/// auto floor switching tied to the playback timeline.
/// </summary>
[DisallowMultipleComponent]
public class EndPanelMovementReplay : MonoBehaviour
{
    public enum ReplayMode { Path, Heat, Both }

    [Header("Texture")]
    [Min(128)] public int floorTextureSize = 512;
    [Range(1f, 1.5f)] public float capturePadding = 1.1f;
    [Range(1f, 4f)] public float baseMapBrightness = 2.2f;
    public Color baseMapBackgroundColor = new Color(0.18f, 0.18f, 0.2f, 1f);

    [Header("Path Trace")]
    public Color pathColor = new Color(0.25f, 0.9f, 1f, 1f);
    [Min(1)] public int pathThickness = 3;

    [Header("Heat Map")]
    public Gradient heatGradient;
    [Tooltip("Stamp radius in world units.")]
    [Min(0.05f)] public float heatStampRadius = 1.0f;
    [Tooltip("Maximum alpha of the composited heat map.")]
    [Range(0f, 1f)] public float heatMaxAlpha = 0.85f;
    [Tooltip("How many samples saturate the heat color ramp.")]
    [Range(1f, 200f)] public float heatSaturationSamples = 25f;

    [Header("Playback")]
    [Range(1f, 30f)] public float maxPlaybackSeconds = 8f;
    [Range(1f, 30f)] public float minPlaybackSeconds = 3f;
    [Tooltip("Approximate texture Apply() rate during playback.")]
    [Range(5f, 60f)] public float applyRateHz = 20f;

    [Header("UI (scene-bound)")]
    public RectTransform replayContent;
    public RectTransform mapPanel;
    public RawImage baseMapImage;
    public RawImage heatOverlayImage;
    public RawImage pathOverlayImage;
    public Text floorLabel;
    public Button pathButton;
    public Button heatButton;
    public Button bothButton;
    public Button replayButton;
    [Range(-180f, 180f)] public float mapRotationDegrees = 90f;

    [Header("State")]
    public ReplayMode mode = ReplayMode.Both;

    private Coroutine _playbackRoutine;
    private readonly Dictionary<int, FloorRender> _renders = new Dictionary<int, FloorRender>();
    private int _displayedFloor = int.MinValue;
    private int _currentSampleIdx;
    private bool _uiBound;
    private float _lastApplyTime;

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
        public float worldSize; // full side length (2 * halfSpan)
        public bool pathDirty;
        public bool heatDirty;
        public Vector2Int lastPixel;
        public bool hasLastPixel;
    }

    private void Reset()
    {
        heatGradient = DefaultHeatGradient();
    }

    private void OnEnable()
    {
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

        if (_playbackRoutine != null) StopCoroutine(_playbackRoutine);

        ReleaseRenders();
        _currentSampleIdx = 0;
        _displayedFloor = int.MinValue;
        _lastApplyTime = 0f;

        BakeAllFloors(rec);
        ApplyMode();

        _playbackRoutine = StartCoroutine(PlaybackCoroutine(rec));
    }

    public void StopReplay()
    {
        if (_playbackRoutine != null)
        {
            StopCoroutine(_playbackRoutine);
            _playbackRoutine = null;
        }
    }

    public void SetMode(ReplayMode m)
    {
        mode = m;
        ApplyMode();
    }

    private void ApplyMode()
    {
        if (pathOverlayImage != null) pathOverlayImage.enabled = (mode != ReplayMode.Heat);
        if (heatOverlayImage != null) heatOverlayImage.enabled = (mode != ReplayMode.Path);
    }

    // ------------------------------------------------------------------
    // Playback loop
    // ------------------------------------------------------------------
    private IEnumerator PlaybackCoroutine(PlayerMovementRecorder rec)
    {
        var samples = rec.Samples;
        float duration = Mathf.Clamp(rec.SessionDuration, minPlaybackSeconds, maxPlaybackSeconds);
        float scale = rec.SessionDuration > 0.001f ? (rec.SessionDuration / duration) : 1f;
        float applyInterval = 1f / Mathf.Max(1f, applyRateHz);

        if (samples.Count > 0)
            SwitchToFloor(samples[0].floorLevel);

        float t = 0f;
        while (t <= duration)
        {
            float sessionT = t * scale;
            AdvanceToTime(samples, sessionT);

            if (Time.unscaledTime - _lastApplyTime >= applyInterval)
            {
                FlushDirtyTextures();
                _lastApplyTime = Time.unscaledTime;
            }

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // Drain remaining samples and push final frame.
        while (_currentSampleIdx < samples.Count)
        {
            PaintSample(samples[_currentSampleIdx]);
            _currentSampleIdx++;
        }
        FlushDirtyTextures();
        _playbackRoutine = null;
    }

    private void AdvanceToTime(IReadOnlyList<PlayerMovementRecorder.Sample> samples, float sessionTime)
    {
        while (_currentSampleIdx < samples.Count && samples[_currentSampleIdx].time <= sessionTime)
        {
            PaintSample(samples[_currentSampleIdx]);
            _currentSampleIdx++;
        }
    }

    private void PaintSample(PlayerMovementRecorder.Sample s)
    {
        if (!_renders.TryGetValue(s.floorLevel, out var fr)) return;

        if (s.floorLevel != _displayedFloor)
            SwitchToFloor(s.floorLevel);

        Vector2Int px = WorldToPixel(fr, s.worldPos);
        if (px.x < 0 || px.x >= fr.w || px.y < 0 || px.y >= fr.h)
        {
            fr.hasLastPixel = false;
            return;
        }

        if (fr.hasLastPixel)
            DrawLine(fr, fr.lastPixel, px, pathColor, pathThickness);
        else
            DrawDisc(fr.pathBuf, fr.w, fr.h, px.x, px.y, pathThickness, pathColor);

        fr.lastPixel = px;
        fr.hasLastPixel = true;
        fr.pathDirty = true;

        float radiusPx = Mathf.Max(1f, heatStampRadius / Mathf.Max(0.001f, fr.worldSize) * fr.w);
        StampHeat(fr, px.x, px.y, radiusPx);
        fr.heatDirty = true;
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
        SetFloorLabel($"Floor {level}");
    }

    private void SetFloorLabel(string text)
    {
        if (floorLabel != null) floorLabel.text = text;
    }

    // ------------------------------------------------------------------
    // Baking
    // ------------------------------------------------------------------
    private void BakeAllFloors(PlayerMovementRecorder rec)
    {
        var floors = rec.Floors;
        if (floors.Count == 0) return;

        int buildingLayer = LayerMask.NameToLayer("Building");
        int cullingMask = buildingLayer >= 0 ? (1 << buildingLayer) : ~0;

        for (int i = 0; i < floors.Count; i++)
        {
            var fi = floors[i];
            var fr = BakeFloor(fi, cullingMask);
            if (fr != null)
                _renders[fi.level] = fr;
        }
    }

    private FloorRender BakeFloor(PlayerMovementRecorder.FloorInfo fi, int cullingMask)
    {
        var b = fi.worldBounds;
        float halfSpan = Mathf.Max(Mathf.Max(b.extents.x, b.extents.z), 0.5f) * capturePadding;
        float worldSize = 2f * halfSpan;

        var fr = new FloorRender
        {
            level = fi.level,
            w = floorTextureSize,
            h = floorTextureSize,
            worldMinX = b.center.x - halfSpan,
            worldMinZ = b.center.z - halfSpan,
            worldSize = worldSize,
            pathBuf = new Color32[floorTextureSize * floorTextureSize],
            heatBuf = new Color32[floorTextureSize * floorTextureSize],
            heatDensity = new float[floorTextureSize * floorTextureSize],
            heatMaxSeen = 0f,
        };

        fr.baseMap = RenderFloorTopDown(fi.root, b, halfSpan, cullingMask);
        fr.pathTex = CreateOverlayTexture(floorTextureSize);
        fr.heatTex = CreateOverlayTexture(floorTextureSize);

        return fr;
    }

    private Texture2D RenderFloorTopDown(Transform floorRoot, Bounds b, float halfSpan, int cullingMask)
    {
        // Temporary camera renders only Building-layered renderers under this floor band.
        var camGo = new GameObject("[EndPanelReplayBakeCam]");
        camGo.hideFlags = HideFlags.HideAndDontSave;
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = baseMapBackgroundColor;
        cam.orthographic = true;
        cam.orthographicSize = halfSpan;
        cam.cullingMask = cullingMask;
        cam.useOcclusionCulling = false;
        cam.allowHDR = false;
        cam.allowMSAA = false;

        float camY = b.max.y + Mathf.Max(b.extents.y, 1f) + 2f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = camY - b.min.y + 5f;
        cam.transform.SetPositionAndRotation(
            new Vector3(b.center.x, camY, b.center.z),
            Quaternion.Euler(90f, 0f, 0f));

        var rt = RenderTexture.GetTemporary(floorTextureSize, floorTextureSize, 16, RenderTextureFormat.ARGB32);
        Texture2D tex = null;
        try
        {
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex = new Texture2D(floorTextureSize, floorTextureSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, floorTextureSize, floorTextureSize), 0, 0);
            ApplyBrightness(tex, baseMapBrightness);
            tex.Apply(false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            RenderTexture.active = prev;
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(camGo);
        }

        return tex;
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
        float u = (world.x - fr.worldMinX) / fr.worldSize;
        float v = (world.z - fr.worldMinZ) / fr.worldSize;
        int x = Mathf.RoundToInt(u * (fr.w - 1));
        int y = Mathf.RoundToInt(v * (fr.h - 1));
        return new Vector2Int(x, y);
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
                ComposeHeatBuffer(fr);
                fr.heatTex.SetPixels32(fr.heatBuf);
                fr.heatTex.Apply(false, false);
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
            if (fr.baseMap != null) Destroy(fr.baseMap);
            if (fr.pathTex != null) Destroy(fr.pathTex);
            if (fr.heatTex != null) Destroy(fr.heatTex);
        }
        _renders.Clear();
        _displayedFloor = int.MinValue;
        _currentSampleIdx = 0;
    }

    // ------------------------------------------------------------------
    // UI scaffolding
    // ------------------------------------------------------------------
    private bool EnsureUI()
    {
        if (_uiBound)
        {
            if (mapPanel != null)
                mapPanel.localEulerAngles = new Vector3(0f, 0f, mapRotationDegrees);
            return true;
        }

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
            pathButton = replayContent != null ? replayContent.Find("Buttons/PathButton")?.GetComponent<Button>() : null;
        if (heatButton == null)
            heatButton = replayContent != null ? replayContent.Find("Buttons/HeatButton")?.GetComponent<Button>() : null;
        if (bothButton == null)
            bothButton = replayContent != null ? replayContent.Find("Buttons/BothButton")?.GetComponent<Button>() : null;
        if (replayButton == null)
            replayButton = replayContent != null ? replayContent.Find("Buttons/ReplayButton")?.GetComponent<Button>() : null;

        if (mapPanel != null)
            mapPanel.localEulerAngles = new Vector3(0f, 0f, mapRotationDegrees);

        if (pathButton == null || heatButton == null || bothButton == null || baseMapImage == null || heatOverlayImage == null || pathOverlayImage == null)
        {
            Debug.LogError("[EndPanelReplay] Missing required Timeline UI references. Please create/wire EndPanel/Timeline scene objects.", this);
            return false;
        }

        pathButton.onClick.RemoveAllListeners();
        heatButton.onClick.RemoveAllListeners();
        bothButton.onClick.RemoveAllListeners();
        pathButton.onClick.AddListener(() => SetMode(ReplayMode.Path));
        heatButton.onClick.AddListener(() => SetMode(ReplayMode.Heat));
        bothButton.onClick.AddListener(() => SetMode(ReplayMode.Both));
        if (replayButton != null)
        {
            replayButton.onClick.RemoveAllListeners();
            replayButton.onClick.AddListener(StartReplay);
        }

        if (heatGradient == null) heatGradient = DefaultHeatGradient();

        _uiBound = true;
        ApplyMode();
        return true;
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
