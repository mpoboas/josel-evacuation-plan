using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Session-scoped gameplay stats + notable replay events for EndPanel.
/// Persists across scene reloads and resets when the active scene changes.
/// </summary>
public sealed class GameplaySessionStats : MonoBehaviour
{
    public enum ReplayEventKind
    {
        FireDamage = 0,
        DoorOpened = 1,
        DoorClosed = 2,
        DoorHeatChecked = 3,
    }

    public struct ReplayEvent
    {
        public float time;
        public Vector3 worldPos;
        public int floorLevel;
        public ReplayEventKind kind;
    }

    public static GameplaySessionStats Instance { get; private set; }

    [Header("Event Sampling")]
    [Min(0.05f)] public float minFireEventIntervalSeconds = 0.65f;

    private readonly List<ReplayEvent> _events = new List<ReplayEvent>(256);
    private readonly HashSet<int> _heatCheckedDoorIds = new HashSet<int>();

    private float _sessionStartTime;
    private float _lastFireEventTime = -999f;

    public IReadOnlyList<ReplayEvent> Events => _events;
    public float SmokeDamageTaken { get; private set; }
    public float FireDamageTaken { get; private set; }
    public int DoorOpenActions { get; private set; }
    public int DoorCloseActions { get; private set; }
    public int HeatCheckedDoorCount => _heatCheckedDoorIds.Count;
    public float ElapsedSeconds => Mathf.Max(0f, Time.time - _sessionStartTime);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapOnSceneLoad()
    {
        if (Instance != null)
            return;

        var go = new GameObject("[GameplaySessionStats]");
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<GameplaySessionStats>();
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
        if (Instance == this)
            Instance = null;
    }

    private void OnSceneChanged(Scene _, Scene __)
    {
        ResetSession();
    }

    public void ResetSession()
    {
        _events.Clear();
        _heatCheckedDoorIds.Clear();
        SmokeDamageTaken = 0f;
        FireDamageTaken = 0f;
        DoorOpenActions = 0;
        DoorCloseActions = 0;
        _sessionStartTime = Time.time;
        _lastFireEventTime = -999f;
    }

    public void RegisterSmokeDamage(float amount)
    {
        if (amount <= 0f)
            return;
        SmokeDamageTaken += amount;
    }

    public void RegisterFireDamage(float amount, Vector3 worldPos)
    {
        if (amount <= 0f)
            return;

        FireDamageTaken += amount;

        float t = ElapsedSeconds;
        if (t - _lastFireEventTime < minFireEventIntervalSeconds)
            return;

        _lastFireEventTime = t;
        AddEvent(ReplayEventKind.FireDamage, worldPos, t);
    }

    public void RegisterDoorStateChanged(DoorController door, bool isOpenNow, Vector3 worldPos)
    {
        if (door == null)
            return;

        if (isOpenNow)
            DoorOpenActions++;
        else
            DoorCloseActions++;

        AddEvent(isOpenNow ? ReplayEventKind.DoorOpened : ReplayEventKind.DoorClosed, worldPos, ElapsedSeconds);
    }

    public void RegisterDoorHeatChecked(DoorController door, Vector3 worldPos)
    {
        if (door == null)
            return;

        _heatCheckedDoorIds.Add(door.GetInstanceID());
        AddEvent(ReplayEventKind.DoorHeatChecked, worldPos, ElapsedSeconds);
    }

    private void AddEvent(ReplayEventKind kind, Vector3 worldPos, float eventTime)
    {
        int floor = InferFloor(worldPos);
        _events.Add(new ReplayEvent
        {
            kind = kind,
            time = eventTime,
            worldPos = worldPos,
            floorLevel = floor,
        });
    }

    private static int InferFloor(Vector3 worldPos)
    {
        var rec = PlayerMovementRecorder.Instance;
        if (rec != null && rec.TryInferFloor(worldPos, out int floor))
            return floor;
        return 0;
    }
}
