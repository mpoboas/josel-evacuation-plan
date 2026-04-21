using System.Collections.Generic;
using BuildingSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent runtime recorder that samples the player position + inferred floor level
/// at a fixed cadence for post-game replay (path trace and heat map).
/// </summary>
public sealed class PlayerMovementRecorder : MonoBehaviour
{
    public struct Sample
    {
        public float time;
        public Vector3 worldPos;
        public int floorLevel;
    }

    public struct FloorInfo
    {
        public int level;
        public Transform root;
        public Bounds worldBounds;
    }

    public static PlayerMovementRecorder Instance { get; private set; }

    [Header("Sampling")]
    [Tooltip("How many samples per second to record.")]
    [Range(1f, 30f)] public float samplesPerSecond = 10f;

    [Tooltip("Minimum planar (XZ) distance between samples; 0 to always sample at cadence.")]
    [Min(0f)] public float minDistanceBetweenSamples = 0.15f;

    [Tooltip("Maximum retained samples; oldest trimmed past this.")]
    [Min(256)] public int maxSamples = 20000;

    [Header("Runtime References (auto-resolved)")]
    [Tooltip("Leave empty to auto-find by Player tag.")]
    public Transform player;

    private readonly List<Sample> _samples = new List<Sample>(4096);
    private readonly List<FloorInfo> _floors = new List<FloorInfo>(8);
    private BuildingTool _buildingTool;
    private float _sessionStartTime;
    private float _nextSampleTime;
    private Vector3 _lastRecordedPos;
    private bool _hasAny;

    public IReadOnlyList<Sample> Samples => _samples;
    public IReadOnlyList<FloorInfo> Floors => _floors;
    public BuildingTool BuildingTool => _buildingTool;
    public float SessionDuration => _hasAny ? _samples[_samples.Count - 1].time : 0f;
    public bool HasData => _samples.Count >= 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapOnSceneLoad()
    {
        if (Instance != null) return;
        var go = new GameObject("[PlayerMovementRecorder]");
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<PlayerMovementRecorder>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ResetSession();
        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        if (Instance == this) Instance = null;
    }

    private void OnSceneChanged(Scene _, Scene __)
    {
        ResetSession();
    }

    public void ResetSession()
    {
        _samples.Clear();
        _floors.Clear();
        _buildingTool = null;
        _sessionStartTime = Time.time;
        _nextSampleTime = 0f;
        _hasAny = false;
        player = null;
    }

    private void Update()
    {
        if (player == null)
            TryResolvePlayer();
        if (player == null) return;

        if (_buildingTool == null || _floors.Count == 0)
        {
            TryResolveBuilding();
            if (_floors.Count == 0) return;
        }

        float t = Time.time - _sessionStartTime;
        if (t < _nextSampleTime) return;
        _nextSampleTime = t + 1f / Mathf.Max(0.5f, samplesPerSecond);

        Vector3 p = player.position;
        if (_hasAny && minDistanceBetweenSamples > 0f)
        {
            Vector3 dp = p - _lastRecordedPos;
            dp.y = 0f;
            if (dp.sqrMagnitude < minDistanceBetweenSamples * minDistanceBetweenSamples)
                return;
        }

        int floor = InferFloor(p);
        _samples.Add(new Sample { time = t, worldPos = p, floorLevel = floor });
        _lastRecordedPos = p;
        _hasAny = true;

        if (_samples.Count > maxSamples)
            _samples.RemoveRange(0, _samples.Count - maxSamples);
    }

    private void TryResolvePlayer()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) player = go.transform;
    }

    private void TryResolveBuilding()
    {
        _buildingTool = FindAnyObjectByType<BuildingTool>();
        if (_buildingTool == null) return;

        _floors.Clear();
        var root = _buildingTool.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            var ch = root.GetChild(i);
            if (!BuildingFloorNaming.TryParseFloorLevelFromName(ch.name, out int level))
                continue;
            _floors.Add(new FloorInfo
            {
                level = level,
                root = ch,
                worldBounds = ComputeFloorBounds(ch)
            });
        }
    }

    /// <summary>Tight floor bounds; prefers <c>Building</c>-layered renderers so furniture/props don't inflate the rect.</summary>
    private static Bounds ComputeFloorBounds(Transform floorRoot)
    {
        var rs = floorRoot.GetComponentsInChildren<Renderer>(true);
        int buildingLayer = LayerMask.NameToLayer("Building");
        bool hasAny = false;
        Bounds b = default;

        if (buildingLayer >= 0)
        {
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null || r.gameObject.layer != buildingLayer) continue;
                if (!hasAny) { b = r.bounds; hasAny = true; }
                else b.Encapsulate(r.bounds);
            }
        }

        if (!hasAny)
        {
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null) continue;
                if (!hasAny) { b = r.bounds; hasAny = true; }
                else b.Encapsulate(r.bounds);
            }
        }

        if (!hasAny) b = new Bounds(floorRoot.position, Vector3.one);
        return b;
    }

    private int InferFloor(Vector3 p)
    {
        int bestContained = int.MinValue;
        float bestDy = float.MaxValue;
        bool contained = false;

        for (int i = 0; i < _floors.Count; i++)
        {
            if (!_floors[i].worldBounds.Contains(p)) continue;
            contained = true;
            float dy = Mathf.Abs(p.y - _floors[i].worldBounds.center.y);
            if (dy < bestDy) { bestDy = dy; bestContained = _floors[i].level; }
        }

        if (contained) return bestContained;

        float bestD = float.MaxValue;
        int best = _floors[0].level;
        for (int i = 0; i < _floors.Count; i++)
        {
            Vector3 c = _floors[i].worldBounds.ClosestPoint(p);
            float d = (c - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = _floors[i].level; }
        }
        return best;
    }

    public bool TryInferFloor(Vector3 worldPos, out int floorLevel)
    {
        if (_floors.Count == 0)
            TryResolveBuilding();
        if (_floors.Count == 0)
        {
            floorLevel = 0;
            return false;
        }

        floorLevel = InferFloor(worldPos);
        return true;
    }
}
