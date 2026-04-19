using System.Collections.Generic;
using BuildingSystem;
using UnityEngine;

/// <summary>
/// Disables entire <c>Floor N</c> subtrees under <see cref="BuildingTool"/> when the player is not nearby in the building stack.
/// Complements baked occlusion culling (Occlusion window): floors handle vertical streaming; occlusion handles line-of-sight.
/// </summary>
[DisallowMultipleComponent]
public class BuildingRuntimeFloorCuller : MonoBehaviour
{
    [Header("References (Editor: also bake Occlusion Culling + static occluders)")]
    [Tooltip("Defaults to a BuildingTool on this GameObject.")]
    [SerializeField] private BuildingTool buildingTool;

    [Tooltip("Player transform. If empty, uses first object tagged Player.")]
    [SerializeField] private Transform player;

    [Header("Visibility band")]
    [Tooltip("How many floor levels below the inferred floor stay active (stairs / look-down).")]
    [Min(0)] public int floorsVisibleBelow = 1;

    [Tooltip("How many floor levels above the inferred floor stay active (stairs / look-up).")]
    [Min(0)] public int floorsVisibleAbove = 1;

    [Header("Update")]
    [Tooltip("How often to recompute inferred floor and toggle roots (seconds).")]
    [Min(0.05f)] public float recomputeInterval = 0.25f;

    [Tooltip("Rebuild renderer bounds caches; 0 = only on enable.")]
    [Min(0f)] public float boundsRefreshInterval = 60f;

    [Tooltip("Require this many consecutive identical inferences before changing the active band.")]
    [Min(1)] public int hysteresisSamples = 2;

    [Tooltip("Turn off to keep every Floor N active (debug).")]
    public bool enableFloorCulling = true;

    private struct FloorEntry
    {
        public Transform Root;
        public int Level;
        public Bounds WorldBounds;
    }

    private readonly List<FloorEntry> _floors = new List<FloorEntry>(16);
    private int _minLevel;
    private int _maxLevel;
    private float _recomputeTimer;
    private float _boundsTimer;
    private int _lastInferredLevel = int.MinValue;
    private int _inferenceMatchCount;
    private int _stableInferredLevel;
    private bool _loggedNoFloors;
    private bool _loggedNoPlayer;

    private void Awake()
    {
        if (buildingTool == null)
            buildingTool = GetComponent<BuildingTool>();

        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null)
                player = go.transform;
        }
    }

    private void OnEnable()
    {
        RebuildFloorCache();
        Vector3 seed = player != null
            ? player.position
            : (buildingTool != null ? buildingTool.transform.position : Vector3.zero);
        _stableInferredLevel = _floors.Count > 0 ? InferFloorLevel(seed) : 0;
        _lastInferredLevel = int.MinValue;
        _inferenceMatchCount = 0;
        _recomputeTimer = recomputeInterval;
        ApplyVisibility(forceAllOn: !enableFloorCulling);
    }

    private void OnDisable()
    {
        ApplyVisibility(forceAllOn: true);
    }

    private void Update()
    {
        if (!enableFloorCulling)
        {
            ApplyVisibility(forceAllOn: true);
            return;
        }

        if (buildingTool == null || player == null)
        {
            if (player == null && !_loggedNoPlayer)
            {
                _loggedNoPlayer = true;
                Debug.LogWarning("[BuildingRuntimeFloorCuller] No player transform; assign or tag Player. All floors stay active.", this);
            }

            ApplyVisibility(forceAllOn: true);
            return;
        }

        if (_floors.Count == 0)
        {
            if (!_loggedNoFloors)
            {
                _loggedNoFloors = true;
                Debug.LogWarning("[BuildingRuntimeFloorCuller] No direct children named \"Floor N\" under BuildingTool.", this);
            }

            return;
        }

        if (boundsRefreshInterval > 0f)
        {
            _boundsTimer += Time.deltaTime;
            if (_boundsTimer >= boundsRefreshInterval)
            {
                _boundsTimer = 0f;
                RebuildFloorCache();
            }
        }

        _recomputeTimer += Time.deltaTime;
        if (_recomputeTimer < recomputeInterval)
            return;

        _recomputeTimer = 0f;

        int inferred = InferFloorLevel(player.position);
        if (inferred == _lastInferredLevel)
            _inferenceMatchCount++;
        else
        {
            _lastInferredLevel = inferred;
            _inferenceMatchCount = 1;
        }

        if (_inferenceMatchCount >= hysteresisSamples)
            _stableInferredLevel = inferred;

        ApplyVisibility(forceAllOn: false);
    }

    private void RebuildFloorCache()
    {
        _floors.Clear();
        if (buildingTool == null)
            return;

        Transform root = buildingTool.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform ch = root.GetChild(i);
            if (!BuildingFloorNaming.TryParseFloorLevelFromName(ch.name, out int level))
                continue;

            var entry = new FloorEntry
            {
                Root = ch,
                Level = level,
                WorldBounds = ComputeWorldBounds(ch)
            };
            _floors.Add(entry);
        }

        if (_floors.Count == 0)
        {
            _minLevel = 0;
            _maxLevel = 0;
            return;
        }

        _minLevel = int.MaxValue;
        _maxLevel = int.MinValue;
        for (int i = 0; i < _floors.Count; i++)
        {
            int lv = _floors[i].Level;
            if (lv < _minLevel) _minLevel = lv;
            if (lv > _maxLevel) _maxLevel = lv;
        }
    }

    private static Bounds ComputeWorldBounds(Transform floorRoot)
    {
        Renderer[] renderers = floorRoot.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        Bounds b = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (!has)
            {
                b = r.bounds;
                has = true;
            }
            else
            {
                b.Encapsulate(r.bounds);
            }
        }

        if (!has)
            return new Bounds(floorRoot.position, Vector3.one * 0.25f);

        return b;
    }

    private int InferFloorLevel(Vector3 p)
    {
        int bestContained = int.MinValue;
        float bestDy = float.MaxValue;
        bool anyContains = false;

        for (int i = 0; i < _floors.Count; i++)
        {
            Bounds b = _floors[i].WorldBounds;
            if (!b.Contains(p))
                continue;
            anyContains = true;
            float dy = Mathf.Abs(p.y - b.center.y);
            if (dy < bestDy)
            {
                bestDy = dy;
                bestContained = _floors[i].Level;
            }
        }

        if (anyContains)
            return bestContained;

        float bestDist = float.MaxValue;
        int bestLevel = _floors[0].Level;
        for (int i = 0; i < _floors.Count; i++)
        {
            Bounds b = _floors[i].WorldBounds;
            Vector3 c = b.ClosestPoint(p);
            float d = (c - p).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                bestLevel = _floors[i].Level;
            }
        }

        return bestLevel;
    }

    private void ApplyVisibility(bool forceAllOn)
    {
        if (buildingTool == null || _floors.Count == 0)
            return;

        int activeMin = _minLevel;
        int activeMax = _maxLevel;

        if (!forceAllOn)
        {
            activeMin = Mathf.Max(_minLevel, _stableInferredLevel - floorsVisibleBelow);
            activeMax = Mathf.Min(_maxLevel, _stableInferredLevel + floorsVisibleAbove);
        }

        Vector3 p = player != null ? player.position : Vector3.zero;

        for (int i = 0; i < _floors.Count; i++)
        {
            FloorEntry e = _floors[i];
            if (e.Root == null)
                continue;

            bool inBand = forceAllOn || (e.Level >= activeMin && e.Level <= activeMax);
            if (!inBand && player != null && e.WorldBounds.Contains(p))
                inBand = true;

            if (e.Root.gameObject.activeSelf != inBand)
                e.Root.gameObject.SetActive(inBand);
        }
    }
}
