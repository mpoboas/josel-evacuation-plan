using System;
using System.Collections;
using System.Collections.Generic;
using BuildingSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
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

    private sealed class OffscreenFloorCacheEntry
    {
        public Texture2D texture;
        public Vector3 v00;
        public Vector3 v10;
        public Vector3 v01;
    }

    private static readonly Dictionary<int, OffscreenFloorCacheEntry> s_OffscreenFloorMaps =
        new Dictionary<int, OffscreenFloorCacheEntry>(8);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_CaptureQueue.Clear();
        s_CaptureRunning = false;
        s_Runner = null;
        ClearOffscreenFloorMapCache();
    }

    private static void ClearOffscreenFloorMapCache()
    {
        foreach (var kv in s_OffscreenFloorMaps)
        {
            if (kv.Value?.texture != null)
                UnityEngine.Object.Destroy(kv.Value.texture);
        }

        s_OffscreenFloorMaps.Clear();
    }

    private const string SignageTag = "Signage";
    private const string BuildingLayerName = "Building";

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

    /// <summary>
    /// Runs the same snap as <see cref="GenerateMap"/> immediately (bypasses the queue). Used by end-panel replay
    /// so it always samples the same pixels as the in-world wall board.
    /// </summary>
    public void CaptureMapImmediate()
    {
        GenerateMapImmediate();
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
            PushSiblingFloorStates(buildingTool.transform, floorIndex, floorsBelowToInclude, floorStates);

        RenderTexture rt = null;
        var previousTarget = mapCamera.targetTexture;
        var wasCamEnabled = mapCamera.enabled;
        var previousCullingMask = mapCamera.cullingMask;
        var previousUseOcclusionCulling = mapCamera.useOcclusionCulling;

        SetBoardVisualsForCapture(false);

        var vfxFireStates = new List<(GameObject go, bool active)>(8);
        var vfxSmokeStates = new List<(SmokeSimulator sim, bool wasEnabled)>(4);
        var forcedLayerStates = new List<(GameObject go, int layer)>(64);
        var floorCullerStates = new List<(BuildingRuntimeFloorCuller culler, bool wasEnabled)>(4);

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

            // Restrict the capture to Building layer, but include Signage by temporarily forcing it to that layer.
            int buildingLayer = LayerMask.NameToLayer(BuildingLayerName);
            if (buildingLayer >= 0)
            {
                mapCamera.cullingMask = 1 << buildingLayer;
                // Signage must always be visible even if it is not authored on Building.
                PushSignageLayersToBuilding(forcedLayerStates, buildingLayer);
                // Floor / Wall / Door FBX meshes are on Default under the PlaceableObject root; lift them for the shot.
                if (floorRoot != null)
                {
                    var placeableVisited = new HashSet<GameObject>();
                    PushPlaceableObjectHierarchyToBuilding(forcedLayerStates, placeableVisited, floorRoot, buildingLayer);
                }
            }
            else
            {
                Debug.LogWarning("[StaticMapGenerator] Layer \"Building\" not found; using camera culling mask as-is.", this);
            }

            // Top-down map captures should not be affected by runtime occlusion or vertical floor streaming.
            mapCamera.useOcclusionCulling = false;
            PushFloorCullerSuppression(floorCullerStates);

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
            mapCamera.cullingMask = previousCullingMask;
            mapCamera.useOcclusionCulling = previousUseOcclusionCulling;
            mapCamera.gameObject.SetActive(false); // Snap & Sleep: camera off after shot

            RestoreForcedLayers(forcedLayerStates);
            PopFloorCullerSuppression(floorCullerStates);

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

    private static void PushHierarchyLayersToBuilding(
        List<(GameObject go, int layer)> states,
        HashSet<GameObject> visited,
        Transform root,
        int buildingLayer)
    {
        if (root == null)
            return;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t.gameObject == null)
                continue;
            if (!visited.Add(t.gameObject))
                continue;

            states.Add((t.gameObject, t.gameObject.layer));
            t.gameObject.layer = buildingLayer;
        }
    }

    /// <summary>
    /// BuildingTool placeable prefabs (Floor/Wall/Corner/Door/Stairs/Window) set their root <c>m_Layer</c> to
    /// <c>Building</c>, but the imported FBX meshes underneath remain on <c>Default</c>. The ortho bake culls to
    /// <c>Building</c> only, which is why the <c>Floor_Prefab</c> surface never rendered in map captures. This
    /// lifts only the renderer hierarchies under <see cref="PlaceableObject"/> roots onto <paramref name="buildingLayer"/>
    /// (pair with <see cref="RestoreForcedLayers"/>). Furniture / plants / VFX do not carry <see cref="PlaceableObject"/>,
    /// so they stay off the Building layer and out of the map.
    /// </summary>
    private static void PushPlaceableObjectHierarchyToBuilding(
        List<(GameObject go, int layer)> states,
        HashSet<GameObject> visited,
        Transform floorRoot,
        int buildingLayer)
    {
        if (floorRoot == null || buildingLayer < 0)
            return;

        var placeables = floorRoot.GetComponentsInChildren<PlaceableObject>(true);
        for (int i = 0; i < placeables.Length; i++)
        {
            var p = placeables[i];
            if (p == null)
                continue;
            PushHierarchyLayersToBuilding(states, visited, p.transform, buildingLayer);
        }
    }

    private static void PushSignageLayersToBuilding(
        List<(GameObject go, int layer)> states,
        int buildingLayer)
    {
        var visited = new HashSet<GameObject>();
        try
        {
            var taggedRoots = GameObject.FindGameObjectsWithTag(SignageTag);
            for (int i = 0; i < taggedRoots.Length; i++)
                PushHierarchyLayersToBuilding(states, visited, taggedRoots[i]?.transform, buildingLayer);
        }
        catch (UnityException)
        {
            // Tag not defined in this project build.
        }

        var placements = FindObjectsByType<MapSignagePlacement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < placements.Length; i++)
            PushHierarchyLayersToBuilding(states, visited, placements[i]?.transform, buildingLayer);
    }

    private static void RestoreForcedLayers(List<(GameObject go, int layer)> states)
    {
        for (int i = states.Count - 1; i >= 0; i--)
        {
            var (go, layer) = states[i];
            if (go != null)
                go.layer = layer;
        }
        states.Clear();
    }

    /// <summary>URP: avoid shadow maps and full-screen post on one-off ortho bakes (cleaner floor plans).</summary>
    public static void ConfigureUniversalOrthoBakeCamera(Camera cam)
    {
        if (cam == null)
            return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderShadows = false;
            data.renderPostProcessing = false;
        }
    }

    private static void PushFloorCullerSuppression(List<(BuildingRuntimeFloorCuller culler, bool wasEnabled)> states)
    {
        states.Clear();
        var cullers = FindObjectsByType<BuildingRuntimeFloorCuller>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cullers.Length; i++)
        {
            var c = cullers[i];
            if (c == null)
                continue;
            states.Add((c, c.enabled));
            c.enabled = false;
        }
    }

    private static void PopFloorCullerSuppression(List<(BuildingRuntimeFloorCuller culler, bool wasEnabled)> states)
    {
        for (int i = states.Count - 1; i >= 0; i--)
        {
            var (c, wasEnabled) = states[i];
            if (c != null)
                c.enabled = wasEnabled;
        }
        states.Clear();
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


    /// <summary>Floor index for this board (same rules as map capture).</summary>
    public bool TryResolveFloorIndex(out int floorIndex)
    {
        Transform floorRoot = floorRootOverride;
        if (floorRoot == null)
            return BuildingFloorNaming.TryFindFloorRoot(transform, out floorRoot, out floorIndex);

        if (!BuildingFloorNaming.TryParseFloorLevelFromName(floorRoot.name, out floorIndex))
        {
            floorIndex = 0;
            return false;
        }

        return true;
    }

    /// <summary>Texture currently shown on the wall board (same pixels as in-world map).</summary>
    public Texture2D GetDisplayedMapTexture()
    {
        if (_ownedTexture != null)
            return _ownedTexture;
        if (mapImage != null && mapImage.texture is Texture2D td)
            return td;
        return null;
    }

    /// <summary>Drop a cached offscreen bake so <see cref="TryResolveEndPanelFloorMapPixels"/> can retry capture.</summary>
    public static void InvalidateOffscreenFloorMap(int floorLevel)
    {
        if (s_OffscreenFloorMaps.TryGetValue(floorLevel, out var e) && e?.texture != null)
            UnityEngine.Object.Destroy(e.texture);

        s_OffscreenFloorMaps.Remove(floorLevel);
    }

    /// <summary>
    /// Resolves the same map texture and world corners as the wall board for <paramref name="floorLevel"/>:
    /// existing board bake, synchronous <see cref="CaptureMapImmediate"/>, offscreen cache, or a fresh offscreen capture.
    /// </summary>
    public static bool TryResolveEndPanelFloorMapPixels(
        BuildingTool buildingTool,
        int floorLevel,
        Transform floorRoot,
        float planeY,
        out Texture2D texture,
        out Vector3 v00,
        out Vector3 v10,
        out Vector3 v01)
    {
        texture = null;
        v00 = v10 = v01 = default;

        bool TryCorners(StaticMapGenerator g, Texture2D tex, float py, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            a = b = c = default;
            if (g == null || g.mapCamera == null || tex == null)
                return false;
            return TryGetMapViewportCornersXZ(g.mapCamera, py, out a, out b, out c, out _);
        }

        bool ValidTex(Texture2D tex)
        {
            return tex != null && tex.width >= 64 && tex.height >= 64 && tex.width == tex.height;
        }

        var planeCandidates = new List<float>(4);
        void AddPlane(float y)
        {
            if (planeCandidates.Count >= 8)
                return;
            for (int i = 0; i < planeCandidates.Count; i++)
            {
                if (Mathf.Abs(planeCandidates[i] - y) < 0.0005f)
                    return;
            }

            planeCandidates.Add(y);
        }

        AddPlane(planeY);
        if (floorRoot != null)
            AddPlane(floorRoot.position.y);

        foreach (var g in FindAllGenerators())
        {
            if (g == null || !g.TryResolveFloorIndex(out int idx) || idx != floorLevel)
                continue;
            var tex = g.GetDisplayedMapTexture();
            if (!ValidTex(tex))
                continue;
            for (int pi = 0; pi < planeCandidates.Count; pi++)
            {
                if (TryCorners(g, tex, planeCandidates[pi], out v00, out v10, out v01))
                {
                    texture = tex;
                    return true;
                }
            }
        }

        foreach (var g in FindAllGenerators())
        {
            if (g == null || !g.TryResolveFloorIndex(out int idx) || idx != floorLevel)
                continue;
            try
            {
                g.CaptureMapImmediate();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StaticMapGenerator] CaptureMapImmediate failed: " + ex.Message, g);
            }

            var tex = g.GetDisplayedMapTexture();
            if (!ValidTex(tex))
                continue;
            for (int pi = 0; pi < planeCandidates.Count; pi++)
            {
                if (TryCorners(g, tex, planeCandidates[pi], out v00, out v10, out v01))
                {
                    texture = tex;
                    return true;
                }
            }
        }

        if (TryGetOffscreenFloorMap(floorLevel, out texture, out v00, out v10, out v01) && ValidTex(texture))
            return true;

        InvalidateOffscreenFloorMap(floorLevel);
        var template = FindFirstBoardForTemplate();
        if (buildingTool != null && floorRoot != null &&
            TryCaptureFloorOffscreen(buildingTool, floorRoot, floorLevel, planeY, template, out var entry) &&
            entry != null &&
            ValidTex(entry.texture))
        {
            s_OffscreenFloorMaps[floorLevel] = entry;
            texture = entry.texture;
            v00 = entry.v00;
            v10 = entry.v10;
            v01 = entry.v01;
            return true;
        }

        return false;
    }

    /// <summary>
    /// World XZ positions at the bottom-left, bottom-right, and top-left viewport corners projected onto a horizontal plane.
    /// Matches texture (0,0), (1,0), (0,1) texel axes after <see cref="Camera.Render"/> into a square target.
    /// </summary>
    public static bool TryGetMapViewportCornersXZ(Camera cam, float planeY, out Vector3 v00, out Vector3 v10, out Vector3 v01, out Vector3 v11)
    {
        v00 = v10 = v01 = v11 = default;
        if (cam == null || !cam.orthographic)
            return false;

        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));

        bool Hit(Vector2 uv, out Vector3 world)
        {
            var ray = cam.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
            if (!plane.Raycast(ray, out float dist))
            {
                world = default;
                return false;
            }

            world = ray.GetPoint(dist);
            return true;
        }

        return Hit(Vector2.zero, out v00) &&
               Hit(Vector2.right, out v10) &&
               Hit(Vector2.up, out v01) &&
               Hit(Vector2.one, out v11);
    }

    /// <summary>
    /// Bakes any building floors that do not already have a wall-board map texture, using the same capture pipeline
    /// as <see cref="GenerateMap"/> (so end-panel replay can reuse pixels + framing without a visible board).
    /// </summary>
    public static void EnsureOffscreenFloorBakes(BuildingTool buildingTool, IReadOnlyList<PlayerMovementRecorder.FloorInfo> floors)
    {
        if (buildingTool == null || floors == null || floors.Count == 0)
            return;

        var template = FindFirstBoardForTemplate();
        for (int i = 0; i < floors.Count; i++)
            TryEnsureOffscreenFloorCacheForSingleFloor(buildingTool, floors[i], template);
    }

    /// <summary>
    /// Spreads offscreen bakes and wall-board snaps across frames so opening the end panel does not hitch the game.
    /// Run from <see cref="PlayerMovementRecorder"/> once the building is resolved.
    /// </summary>
    public static IEnumerator PrewarmEndPanelMapsDeferred(
        BuildingTool buildingTool,
        IReadOnlyList<PlayerMovementRecorder.FloorInfo> floors)
    {
        if (buildingTool == null || floors == null || floors.Count == 0)
            yield break;

        yield return null;

        var template = FindFirstBoardForTemplate();
        for (int i = 0; i < floors.Count; i++)
        {
            TryEnsureOffscreenFloorCacheForSingleFloor(buildingTool, floors[i], template);
            yield return null;
        }

        var gens = FindAllGenerators();
        for (int i = 0; i < gens.Length; i++)
        {
            var g = gens[i];
            if (g == null)
                continue;
            try
            {
                g.CaptureMapImmediate();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StaticMapGenerator] Prewarm CaptureMapImmediate failed: " + ex.Message, g);
            }

            yield return null;
        }
    }

    private static bool TryEnsureOffscreenFloorCacheForSingleFloor(
        BuildingTool buildingTool,
        PlayerMovementRecorder.FloorInfo fi,
        StaticMapGenerator template)
    {
        if (fi.root == null)
            return false;
        int level = fi.level;
        if (FloorHasBoardBakedTexture(level))
            return false;
        if (s_OffscreenFloorMaps.ContainsKey(level))
            return false;

        if (!TryCaptureFloorOffscreen(buildingTool, fi.root, level, fi.worldBounds.center.y, template, out var entry) ||
            entry == null ||
            entry.texture == null)
            return false;

        s_OffscreenFloorMaps[level] = entry;
        return true;
    }

    /// <summary>Offscreen bake for end-panel replay (do not destroy; owned by <see cref="StaticMapGenerator"/> cache).</summary>
    public static bool TryGetOffscreenFloorMap(
        int floorLevel,
        out Texture2D texture,
        out Vector3 v00,
        out Vector3 v10,
        out Vector3 v01)
    {
        texture = null;
        v00 = v10 = v01 = default;
        if (!s_OffscreenFloorMaps.TryGetValue(floorLevel, out var e) || e == null || e.texture == null)
            return false;
        texture = e.texture;
        v00 = e.v00;
        v10 = e.v10;
        v01 = e.v01;
        return true;
    }

    private static bool FloorHasBoardBakedTexture(int floorLevel)
    {
        foreach (var g in FindAllGenerators())
        {
            if (g == null || !g.TryResolveFloorIndex(out int idx) || idx != floorLevel)
                continue;
            if (g.GetDisplayedMapTexture() != null)
                return true;
        }

        return false;
    }

    private static StaticMapGenerator FindFirstBoardForTemplate()
    {
        foreach (var g in FindAllGenerators())
        {
            if (g != null && g.mapCamera != null)
                return g;
        }

        return null;
    }

    private static void CopyCaptureCameraFromTemplate(Camera dst, Camera template)
    {
        dst.orthographic = true;
        if (template != null)
        {
            dst.clearFlags = template.clearFlags;
            dst.backgroundColor = template.backgroundColor;
            dst.cullingMask = template.cullingMask;
            dst.nearClipPlane = template.nearClipPlane;
            dst.farClipPlane = template.farClipPlane;
            dst.allowHDR = false;
            dst.allowMSAA = false;
        }
        else
        {
            dst.clearFlags = CameraClearFlags.SolidColor;
            dst.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);
            int buildingLayer = LayerMask.NameToLayer(BuildingLayerName);
            dst.cullingMask = buildingLayer >= 0 ? (1 << buildingLayer) : ~0;
            dst.nearClipPlane = 0.01f;
            dst.farClipPlane = 80f;
            dst.allowHDR = false;
            dst.allowMSAA = false;
        }
    }

    private static bool TryCaptureFloorOffscreen(
        BuildingTool buildingTool,
        Transform floorRoot,
        int floorIndex,
        float planeY,
        StaticMapGenerator template,
        out OffscreenFloorCacheEntry entry)
    {
        entry = null;
        var camGo = new GameObject("[OffscreenFloorMapCam]");
        camGo.hideFlags = HideFlags.HideAndDontSave;
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        CopyCaptureCameraFromTemplate(cam, template != null ? template.mapCamera : null);
        cam.depthTextureMode = DepthTextureMode.None;
        ConfigureUniversalOrthoBakeCamera(cam);

        int captureResolution = template != null ? Mathf.Max(64, template.captureResolution) : 1024;
        float orthoPadding = template != null ? template.orthoPadding : 1.15f;
        int floorsBelow = template != null ? template.floorsBelowToInclude : 1;
        bool excludeFireAndSmoke = template == null || template.excludeFireAndSmokeFromMap;

        var floorStates = new List<(Transform t, bool active)>(16);
        PushSiblingFloorStates(buildingTool.transform, floorIndex, floorsBelow, floorStates);

        RenderTexture rt = null;
        var vfxFireStates = new List<(GameObject go, bool active)>(8);
        var vfxSmokeStates = new List<(SmokeSimulator sim, bool wasEnabled)>(4);
        var forcedLayerStates = new List<(GameObject go, int layer)>(64);
        var floorCullerStates = new List<(BuildingRuntimeFloorCuller culler, bool wasEnabled)>(8);

        try
        {
            HideAllYouAreHereMarkers();
            foreach (var g in FindAllGenerators())
                g.SetBoardVisualsForCapture(false);

            if (buildingTool != null && floorRoot != null)
                FitOrthoCameraToVisibleFloors(cam, buildingTool.transform, floorIndex, floorsBelow, orthoPadding);
            else if (floorRoot != null)
                FitOrthoCameraToFloor(cam, floorRoot, orthoPadding);
            else
            {
                var refT = floorRoot != null ? floorRoot : buildingTool != null ? buildingTool.transform : null;
                if (refT != null)
                    DefaultTopDownCamera(cam, refT);
            }

            rt = RenderTexture.GetTemporary(captureResolution, captureResolution, 16, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.gameObject.SetActive(true);
            cam.enabled = true;

            int buildingLayer = LayerMask.NameToLayer(BuildingLayerName);
            if (buildingLayer >= 0)
            {
                cam.cullingMask = 1 << buildingLayer;
                PushSignageLayersToBuilding(forcedLayerStates, buildingLayer);
                if (floorRoot != null)
                {
                    var placeableVisited = new HashSet<GameObject>();
                    PushPlaceableObjectHierarchyToBuilding(forcedLayerStates, placeableVisited, floorRoot, buildingLayer);
                }
            }

            cam.useOcclusionCulling = false;
            PushFloorCullerSuppression(floorCullerStates);

            if (Application.isPlaying)
                SetAllTaggedSignageRenderersEnabled(true);

            if (excludeFireAndSmoke)
                PushFireAndSmokeSuppression(vfxFireStates, vfxSmokeStates);

            cam.Render();

            var prevRt = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false, true);
            RenderTexture.active = prevRt;

            if (!TryGetMapViewportCornersXZ(cam, planeY, out var c00, out var c10, out var c01, out _))
            {
                UnityEngine.Object.Destroy(tex);
                return false;
            }

            entry = new OffscreenFloorCacheEntry
            {
                texture = tex,
                v00 = c00,
                v10 = c10,
                v01 = c01,
            };
            return true;
        }
        finally
        {
            if (excludeFireAndSmoke)
                PopFireAndSmokeSuppression(vfxFireStates, vfxSmokeStates);

            if (Application.isPlaying)
                SetAllTaggedSignageRenderersEnabled(false);

            foreach (var g in FindAllGenerators())
                g.SetBoardVisualsForCapture(true);

            cam.targetTexture = null;
            cam.enabled = false;
            cam.gameObject.SetActive(false);
            RestoreForcedLayers(forcedLayerStates);
            PopFloorCullerSuppression(floorCullerStates);

            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
                rt = null;
            }

            RestoreFloorStates(floorStates);
            HideAllYouAreHereMarkers();
            UnityEngine.Object.Destroy(camGo);
        }
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

    /// <summary>Disables floor roots whose level is outside <c>[currentFloorLevel − floorsBelow, currentFloorLevel]</c>.</summary>
    private static void PushSiblingFloorStates(Transform buildingRoot, int currentFloorLevel, int floorsBelow, List<(Transform t, bool active)> states)
    {
        int minVisible = currentFloorLevel - floorsBelow;
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
