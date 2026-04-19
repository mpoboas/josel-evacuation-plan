using System;
using System.Collections.Generic;
using BuildingSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Snap &amp; Sleep: one orthographic render from above the current floor, read back to a
/// <see cref="Texture2D"/> on a <see cref="RawImage"/>, then release GPU RT and disable the capture camera.
/// Place this on the wall board root (duplicable prefab). Multiple boards on the same floor share floor
/// geometry for the shot but each gets its own "You are here" XZ at this board's position.
/// </summary>
[DisallowMultipleComponent]
public class StaticMapGenerator : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Orthographic top-down camera; usually a child of this board. Disabled after capture.")]
    public Camera mapCamera;

    [Tooltip("Where the baked map texture is shown.")]
    public RawImage mapImage;

    [Tooltip("World-space marker (e.g. small sphere) placed at ceiling height before rendering.")]
    public Transform youAreHereMarker;

    [Tooltip("Optional: explicit floor root (e.g. Floor 2). If null, resolved by walking parents for name \"Floor N\".")]
    public Transform floorRootOverride;

    [Tooltip("Optional: BuildingTool root. If null, resolved via GetComponentInParent.")]
    public BuildingTool buildingToolOverride;

    [Tooltip("Canvas root to hide during capture so the board UI is not visible from above. Auto-finds child \"MapCanvas\" if null.")]
    public Transform mapCanvasRoot;

    [Header("Generation")]
    [Min(64)] public int captureResolution = 1024;
    public bool generateOnStart = true;

    [Tooltip("Extra Y added to the analytic ceiling height from gridSize * (floorIndex + 1).")]
    public float ceilingHeightExtraOffset;

    [Tooltip("Multiplies half-extents when fitting the orthographic view to floor renderers.")]
    [Min(1.01f)] public float orthoPadding = 1.15f;

    [Tooltip("Also keeps this many floor levels below the board visible during capture (e.g. stairs). 0 = current floor only; 1 = current + one floor below (default).")]
    [Min(0)] public int floorsBelowToInclude = 1;

    [Tooltip("While capturing the map, disables GameObjects tagged Fire and all SmokeSimulator components (voxel smoke uses Graphics.DrawMeshInstanced and ignores camera culling masks).")]
    public bool excludeFireAndSmokeFromMap = true;

    [Header("Zoom (click RawImage)")]
    [Min(1.01f)] public float zoomFactor = 2f;

    // ---------------------------------------------------------------------
    // Snap & Sleep: queue so multiple boards never interleave floor hide/restore
    // ---------------------------------------------------------------------
    private static readonly Queue<Action> s_CaptureQueue = new Queue<Action>();
    private static bool s_CaptureRunning;
    private static CaptureRunner s_Runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_CaptureQueue.Clear();
        s_CaptureRunning = false;
        s_Runner = null;
    }

    private const string SignageTag = "Signage";

    /// <summary>World-space signage is editor-only visibility; hide for the player as soon as the play scene loads.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HideTaggedSignageAfterSceneLoad()
    {
        if (!Application.isPlaying)
            return;
        SetAllTaggedSignageRenderersEnabled(false);
    }

    private static void SetAllTaggedSignageRenderersEnabled(bool enabled)
    {
        try
        {
            var roots = GameObject.FindGameObjectsWithTag(SignageTag);
            for (int i = 0; i < roots.Length; i++)
            {
                var go = roots[i];
                if (go == null)
                    continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    r.enabled = enabled;
            }
        }
        catch (UnityException)
        {
            // Tag not defined in this project build.
        }
    }

    /// <summary>Runs before default scripts so voxel smoke can be disabled before <see cref="SmokeSimulator"/> submits draws this frame.</summary>
    [DefaultExecutionOrder(-10000)]
    private sealed class CaptureRunner : MonoBehaviour
    {
        private void Update()
        {
            if (s_CaptureRunning || s_CaptureQueue.Count == 0)
                return;

            s_CaptureRunning = true;
            try
            {
                s_CaptureQueue.Dequeue()?.Invoke();
            }
            finally
            {
                s_CaptureRunning = false;
            }
        }
    }

    private static void EnqueueCapture(Action work)
    {
        if (work == null) return;
        s_CaptureQueue.Enqueue(work);
        if (s_Runner == null)
        {
            var go = new GameObject("[StaticMapCaptureQueue]");
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
            DontDestroyOnLoad(go);
            s_Runner = go.AddComponent<CaptureRunner>();
        }
    }

    // ---------------------------------------------------------------------
    // Zoom state
    // ---------------------------------------------------------------------
    private RectTransform _mapImageRect;
    private Vector3 _originalLocalScale;
    private Vector2 _originalAnchoredPosition;
    private bool _zoomed;

    private Texture2D _ownedTexture;

    private void Awake()
    {
        if (mapCanvasRoot == null)
        {
            var t = transform.Find("MapCanvas");
            if (t != null)
                mapCanvasRoot = t;
        }

        if (mapImage != null)
        {
            _mapImageRect = mapImage.rectTransform;
            _originalLocalScale = _mapImageRect.localScale;
            _originalAnchoredPosition = _mapImageRect.anchoredPosition;
        }

        EnsurePointerRelay();

        // Markers are only for the snap pass; never show them to the main camera in play mode.
        if (Application.isPlaying && youAreHereMarker != null)
            youAreHereMarker.gameObject.SetActive(false);
    }

    private void Start()
    {
        EnsurePointerRelay();
        if (generateOnStart)
            GenerateMap();
    }

    private void EnsurePointerRelay()
    {
        if (mapImage == null)
            return;
        if (mapImage.gameObject.GetComponent<MapClickRelay>() != null)
            return;
        var relay = mapImage.gameObject.AddComponent<MapClickRelay>();
        relay.Initialize(this);
    }

    /// <summary>Queues a static capture (serialized with other boards).</summary>
    public void GenerateMap()
    {
        EnqueueCapture(GenerateMapImmediate);
    }

    private void GenerateMapImmediate()
    {
        if (mapCamera == null || mapImage == null || youAreHereMarker == null)
        {
            Debug.LogWarning("[StaticMapGenerator] Missing references on " + name, this);
            return;
        }

        // --- Snap & Sleep flow (see class summary) ---
        // 1) Resolve BuildingTool + this board's Floor N; turn off Floor* siblings outside [N−X, N] for a clean top-down (X = floorsBelowToInclude).
        // 2) Hide this board's frame + UI so the capture camera does not paint the map surface into the RT.
        // 3) Place the red marker at ceiling Y; aim/fit the ortho camera to this floor's renderers.
        // 4) Allocate a temporary RenderTexture, Camera.Render() once, ReadPixels into a Texture2D.
        // 5) Assign Texture2D to RawImage, release RT, disable the capture camera (sleep).
        // 6) Restore floor sibling active states and board visuals.

        // --- Resolve BuildingTool + floor ---
        var buildingTool = buildingToolOverride != null
            ? buildingToolOverride
            : GetComponentInParent<BuildingTool>();

        Transform floorRoot = floorRootOverride;
        int floorIndex = 0;
        if (floorRoot == null)
        {
            if (!BuildingFloorNaming.TryFindFloorRoot(transform, out floorRoot, out floorIndex))
                Debug.LogWarning("[StaticMapGenerator] Could not resolve Floor N parent; capturing without floor isolation.", this);
        }
        else if (!BuildingFloorNaming.TryParseFloorLevelFromName(floorRoot.name, out floorIndex))
        {
            Debug.LogWarning("[StaticMapGenerator] floorRootOverride is not named \"Floor N\"; using floor index 0 for capture range.", this);
            floorIndex = 0;
        }

        var floorStates = new List<(Transform t, bool active)>();
        if (buildingTool != null && floorRoot != null)
            PushSiblingFloorStates(buildingTool.transform, floorIndex, floorStates);

        RenderTexture rt = null;
        var previousTarget = mapCamera.targetTexture;
        var wasCamEnabled = mapCamera.enabled;

        SetBoardVisualsForCapture(false);

        var vfxFireStates = new List<(GameObject go, bool active)>(8);
        var vfxSmokeStates = new List<(SmokeSimulator sim, bool wasEnabled)>(4);

        try
        {
            // Only this board's marker may be visible during Render(); others stay off so they are not baked into this texture.
            PrepareSoloYouAreHereMarkerForCapture();

            // --- Marker at analytic ceiling (BuildingTool storey height) ---
            float ceilingY = transform.position.y + ceilingHeightExtraOffset;
            if (buildingTool != null && floorRoot != null)
            {
                var localCeiling = new Vector3(0f, (floorIndex + 1) * buildingTool.gridSize, 0f);
                ceilingY = buildingTool.transform.TransformPoint(localCeiling).y + ceilingHeightExtraOffset;
            }

            youAreHereMarker.position = new Vector3(transform.position.x, ceilingY, transform.position.z);

            // --- Fit orthographic camera to visible floor stack (current + floorsBelowToInclude) ---
            if (buildingTool != null && floorRoot != null)
                FitOrthoCameraToVisibleFloors(mapCamera, buildingTool.transform, floorIndex, floorsBelowToInclude, orthoPadding);
            else if (floorRoot != null)
                FitOrthoCameraToFloor(mapCamera, floorRoot, orthoPadding);
            else
                DefaultTopDownCamera(mapCamera, transform);

            rt = RenderTexture.GetTemporary(captureResolution, captureResolution, 16, RenderTextureFormat.ARGB32);
            mapCamera.targetTexture = rt;
            mapCamera.gameObject.SetActive(true);
            mapCamera.enabled = true;

            if (Application.isPlaying)
                SetAllTaggedSignageRenderersEnabled(true);

            if (excludeFireAndSmokeFromMap)
                PushFireAndSmokeSuppression(vfxFireStates, vfxSmokeStates);

            // --- One manual frame into the RT ---
            mapCamera.Render();

            // --- Readback to CPU Texture2D (release GPU RT from long-term use) ---
            if (_ownedTexture != null)
            {
                Destroy(_ownedTexture);
                _ownedTexture = null;
            }

            var prevRt = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false, true);
            RenderTexture.active = prevRt;

            _ownedTexture = tex;
            mapImage.texture = tex;
        }
        finally
        {
            if (excludeFireAndSmokeFromMap)
                PopFireAndSmokeSuppression(vfxFireStates, vfxSmokeStates);

            if (Application.isPlaying)
                SetAllTaggedSignageRenderersEnabled(false);

            SetBoardVisualsForCapture(true);

            mapCamera.targetTexture = previousTarget;
            mapCamera.enabled = wasCamEnabled;
            mapCamera.gameObject.SetActive(false); // Snap & Sleep: camera off after shot

            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
                rt = null;
            }

            RestoreFloorStates(floorStates);

            // Markers are capture-only; never leave them visible to the player camera.
            HideAllYouAreHereMarkers();
        }
    }

    private static StaticMapGenerator[] FindAllGenerators()
    {
        return FindObjectsByType<StaticMapGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    /// <summary>Turns off every board's marker, then turns on only this board's marker for the upcoming render.</summary>
    private void PrepareSoloYouAreHereMarkerForCapture()
    {
        foreach (var g in FindAllGenerators())
        {
            if (g.youAreHereMarker == null)
                continue;
            g.youAreHereMarker.gameObject.SetActive(g == this);
        }
    }

    private static void HideAllYouAreHereMarkers()
    {
        foreach (var g in FindAllGenerators())
        {
            if (g.youAreHereMarker != null)
                g.youAreHereMarker.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Fire uses the <c>Fire</c> tag (see <c>GameManager</c>). Smoke voxel rendering uses <see cref="Graphics.DrawMeshInstanced"/>,
    /// which is not culled by <see cref="Camera.cullingMask"/>; disabling <see cref="SmokeSimulator"/> prevents draws for this frame.
    /// </summary>
    private static void PushFireAndSmokeSuppression(
        List<(GameObject go, bool active)> fireStates,
        List<(SmokeSimulator sim, bool wasEnabled)> smokeStates)
    {
        fireStates.Clear();
        smokeStates.Clear();

        try
        {
            var tagged = GameObject.FindGameObjectsWithTag("Fire");
            for (int i = 0; i < tagged.Length; i++)
            {
                var go = tagged[i];
                if (go == null)
                    continue;
                fireStates.Add((go, go.activeSelf));
                go.SetActive(false);
            }
        }
        catch (UnityException)
        {
            // Project has no "Fire" tag — skip.
        }

        var sims = FindObjectsByType<SmokeSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sims.Length; i++)
        {
            var sim = sims[i];
            if (sim == null)
                continue;
            smokeStates.Add((sim, sim.enabled));
            sim.enabled = false;
        }
    }

    private static void PopFireAndSmokeSuppression(
        List<(GameObject go, bool active)> fireStates,
        List<(SmokeSimulator sim, bool wasEnabled)> smokeStates)
    {
        for (int i = smokeStates.Count - 1; i >= 0; i--)
        {
            var (sim, was) = smokeStates[i];
            if (sim != null)
                sim.enabled = was;
        }

        smokeStates.Clear();

        for (int i = fireStates.Count - 1; i >= 0; i--)
        {
            var (go, was) = fireStates[i];
            if (go != null)
                go.SetActive(was);
        }

        fireStates.Clear();
    }

    private void SetBoardVisualsForCapture(bool visible)
    {
        if (mapCanvasRoot != null)
            mapCanvasRoot.gameObject.SetActive(visible);

        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsUnderMarker(r.transform))
                continue;
            r.enabled = visible;
        }

        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (IsUnderMarker(r.transform))
                continue;
            r.enabled = visible;
        }
    }

    private bool IsUnderMarker(Transform t)
    {
        if (youAreHereMarker == null)
            return false;
        return t == youAreHereMarker || t.IsChildOf(youAreHereMarker);
    }

    /// <summary>Disables floor roots whose level is outside <c>[currentFloorLevel − floorsBelowToInclude, currentFloorLevel]</c>.</summary>
    private void PushSiblingFloorStates(Transform buildingRoot, int currentFloorLevel, List<(Transform, bool)> states)
    {
        int minVisible = currentFloorLevel - floorsBelowToInclude;
        for (int i = 0; i < buildingRoot.childCount; i++)
        {
            var ch = buildingRoot.GetChild(i);
            if (!BuildingFloorNaming.TryParseFloorLevelFromName(ch.name, out int level))
                continue;
            if (level >= minVisible && level <= currentFloorLevel)
                continue;
            states.Add((ch, ch.gameObject.activeSelf));
            ch.gameObject.SetActive(false);
        }
    }

    private static void RestoreFloorStates(List<(Transform t, bool active)> states)
    {
        for (int i = states.Count - 1; i >= 0; i--)
        {
            var (t, a) = states[i];
            if (t != null)
                t.gameObject.SetActive(a);
        }
    }

    /// <summary>Bounds all renderers under each <c>Floor K</c> child where <c>K</c> is in <c>[currentFloor − floorsBelow, currentFloor]</c>.</summary>
    private static void FitOrthoCameraToVisibleFloors(Camera cam, Transform buildingRoot, int currentFloor, int floorsBelow, float padding)
    {
        int minLevel = currentFloor - floorsBelow;
        var bounds = new Bounds(buildingRoot.position, Vector3.zero);
        var hasAny = false;

        for (int i = 0; i < buildingRoot.childCount; i++)
        {
            var ch = buildingRoot.GetChild(i);
            if (!BuildingFloorNaming.TryParseFloorLevelFromName(ch.name, out int level))
                continue;
            if (level < minLevel || level > currentFloor)
                continue;
            if (!ch.gameObject.activeInHierarchy)
                continue;

            foreach (var r in ch.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                if (!hasAny)
                {
                    bounds = r.bounds;
                    hasAny = true;
                }
                else
                    bounds.Encapsulate(r.bounds);
            }
        }

        if (!hasAny)
        {
            DefaultTopDownCamera(cam, buildingRoot);
            return;
        }

        var c = bounds.center;
        var ext = bounds.extents;
        var halfSpan = Mathf.Max(ext.x, ext.z) * padding;

        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(halfSpan, 0.5f);
        var camY = bounds.max.y + Mathf.Max(ext.y, 1f) + 2f;
        cam.transform.SetPositionAndRotation(
            new Vector3(c.x, camY, c.z),
            Quaternion.Euler(90f, 0f, 0f));
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = camY - bounds.min.y + 5f;
    }

    private static void FitOrthoCameraToFloor(Camera cam, Transform floorRoot, float padding)
    {
        var bounds = new Bounds(floorRoot.position, Vector3.zero);
        var renderers = floorRoot.GetComponentsInChildren<Renderer>();
        var hasAny = false;
        foreach (var r in renderers)
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            bounds.Encapsulate(r.bounds);
            hasAny = true;
        }

        if (!hasAny)
        {
            DefaultTopDownCamera(cam, floorRoot);
            return;
        }

        var c = bounds.center;
        var ext = bounds.extents;
        var halfSpan = Mathf.Max(ext.x, ext.z) * padding;

        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(halfSpan, 0.5f);
        var camY = bounds.max.y + Mathf.Max(ext.y, 1f) + 2f;
        cam.transform.SetPositionAndRotation(
            new Vector3(c.x, camY, c.z),
            Quaternion.Euler(90f, 0f, 0f));
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = camY - bounds.min.y + 5f;
    }

    private static void DefaultTopDownCamera(Camera cam, Transform reference)
    {
        cam.orthographic = true;
        if (cam.orthographicSize < 1f)
            cam.orthographicSize = 8f;
        var p = reference.position;
        cam.transform.SetPositionAndRotation(new Vector3(p.x, p.y + 25f, p.z), Quaternion.Euler(90f, 0f, 0f));
    }

    // ---------------------------------------------------------------------
    // Zoom
    // ---------------------------------------------------------------------
    internal void ToggleZoom()
    {
        if (_mapImageRect == null)
            return;

        if (!_zoomed)
        {
            _mapImageRect.localScale = _originalLocalScale * zoomFactor;
            _mapImageRect.anchoredPosition = Vector2.zero;
            _zoomed = true;
        }
        else
        {
            _mapImageRect.localScale = _originalLocalScale;
            _mapImageRect.anchoredPosition = _originalAnchoredPosition;
            _zoomed = false;
        }
    }

    private void OnDestroy()
    {
        if (_ownedTexture != null)
        {
            Destroy(_ownedTexture);
            _ownedTexture = null;
        }
    }

    /// <summary>Receives UI clicks on the RawImage and forwards to <see cref="StaticMapGenerator"/>.</summary>
    private sealed class MapClickRelay : MonoBehaviour, IPointerClickHandler
    {
        private StaticMapGenerator _owner;

        public void Initialize(StaticMapGenerator owner) => _owner = owner;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_owner != null)
                _owner.ToggleZoom();
        }
    }
}
