using System.Collections;
using UnityEngine;

/// <summary>
/// First-person cosmetic hand: reaches into view when the player uses interact (E) or inspect (R).
/// Parent on the player camera (or assign <see cref="handRoot"/>). Does not delay gameplay logic.
/// </summary>
public class PlayerHandFeedback : MonoBehaviour
{
    public enum HandGestureKind
    {
        Interact,
        HeatInspect
    }

    [Header("References")]
    [Tooltip("Usually an empty child of the player camera. If null, one is created at runtime.")]
    public Transform handRoot;

    [Tooltip("Drag the hand prefab asset from the Project window (root GameObject). Must be a GameObject prefab, not a sub-asset.")]
    public GameObject handModelPrefab;

    [Header("Timing (seconds)")]
    [Min(0.01f)] public float moveInDuration = 0.35f;
    [Min(0f)] public float holdDuration = 0.25f;
    [Min(0.01f)] public float moveOutDuration = 0.35f;

    [Header("Local pose (relative to handRoot)")]
    [Tooltip("Local position when the hand is hidden (off-screen / tucked).")]
    public Vector3 hiddenLocalPosition = new Vector3(0.55f, -0.55f, 0.15f);

    [Tooltip("Local position when the hand is fully in view.")]
    public Vector3 visibleLocalPosition = new Vector3(0.28f, -0.22f, 0.42f);

    [Tooltip("Uniform scale applied to the instantiated hand model.")]
    [Min(0.01f)] public float handUniformScale = 0.22f;

    [Tooltip("Local rotation (Euler) for open / interact (E).")]
    public Vector3 interactHandEuler = new Vector3(12f, -18f, -6f);

    [Tooltip("Local rotation (Euler) for heat inspect (R) — tune so the back of the hand reads toward the door.")]
    public Vector3 inspectHandEuler = new Vector3(8f, 168f, -20f);

    public enum DebugTuningPose
    {
        Hidden,
        VisibleInteract,
        VisibleInspect
    }

    [Header("Debug")]
    [Tooltip("Play Mode: keeps the instantiated hand visible on the pose below so you can tune vectors/Eulers live. Pauses while a gesture coroutine is running.")]
    public bool debugHoldPoseForTuning;

    [Tooltip("Pose applied every frame while hold is enabled (uses current hidden/visible/Euler fields).")]
    public DebugTuningPose debugTuningPose = DebugTuningPose.VisibleInteract;

    private Transform _handInstanceRoot;
    private Coroutine _gestureRoutine;
    private bool _gesturePlaying;
    private bool _debugHoldWasActive;

    private void Awake()
    {
        EnsureHandRoot();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        if (!debugHoldPoseForTuning)
        {
            if (_debugHoldWasActive && !_gesturePlaying && _handInstanceRoot != null)
                _handInstanceRoot.gameObject.SetActive(false);
            _debugHoldWasActive = false;
            return;
        }

        _debugHoldWasActive = true;
        if (_gesturePlaying || handModelPrefab == null)
            return;

        EnsureHandRoot();
        EnsureHandInstance();
        ApplyDebugFrozenPose();
        _handInstanceRoot.gameObject.SetActive(true);
    }

    private void EnsureHandRoot()
    {
        if (handRoot != null)
            return;

        var t = transform.Find("HandAnchor");
        if (t != null)
        {
            handRoot = t;
            return;
        }

        var go = new GameObject("HandAnchor");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        handRoot = go.transform;
    }

    /// <summary>Starts a reach-in / hold / pull-back gesture. Ignored if a gesture is already playing.</summary>
    public void PlayGesture(HandGestureKind kind)
    {
        if (handModelPrefab == null)
            return;

        EnsureHandRoot();

        if (_gesturePlaying)
            return;

        if (_gestureRoutine != null)
            StopCoroutine(_gestureRoutine);

        _gestureRoutine = StartCoroutine(GestureRoutine(kind));
    }

    private IEnumerator GestureRoutine(HandGestureKind kind)
    {
        _gesturePlaying = true;

        EnsureHandInstance();

        _handInstanceRoot.gameObject.SetActive(true);

        var euler = kind == HandGestureKind.HeatInspect ? inspectHandEuler : interactHandEuler;
        var hiddenRot = Quaternion.Euler(interactHandEuler);
        var targetRot = Quaternion.Euler(euler);

        _handInstanceRoot.localPosition = hiddenLocalPosition;
        _handInstanceRoot.localRotation = hiddenRot;

        yield return LerpLocalPose(hiddenLocalPosition, hiddenRot, visibleLocalPosition, targetRot, moveInDuration);
        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);
        yield return LerpLocalPose(visibleLocalPosition, targetRot, hiddenLocalPosition, hiddenRot, moveOutDuration);

        _handInstanceRoot.gameObject.SetActive(false);
        _gesturePlaying = false;
        _gestureRoutine = null;
    }

    private IEnumerator LerpLocalPose(
        Vector3 fromPos, Quaternion fromRot,
        Vector3 toPos, Quaternion toRot,
        float duration)
    {
        if (duration <= 0f)
        {
            _handInstanceRoot.localPosition = toPos;
            _handInstanceRoot.localRotation = toRot;
            yield break;
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            _handInstanceRoot.localPosition = Vector3.LerpUnclamped(fromPos, toPos, s);
            _handInstanceRoot.localRotation = Quaternion.SlerpUnclamped(fromRot, toRot, s);
            yield return null;
        }

        _handInstanceRoot.localPosition = toPos;
        _handInstanceRoot.localRotation = toRot;
    }

    private void EnsureHandInstance()
    {
        if (_handInstanceRoot != null)
            return;
        if (handModelPrefab == null || handRoot == null)
            return;

        var inst = Instantiate(handModelPrefab, handRoot);
        inst.name = "HandModel";
        _handInstanceRoot = inst.transform;
        _handInstanceRoot.localScale = Vector3.one * handUniformScale;
    }

    private void ApplyDebugFrozenPose()
    {
        if (_handInstanceRoot == null)
            return;

        switch (debugTuningPose)
        {
            case DebugTuningPose.Hidden:
                _handInstanceRoot.localPosition = hiddenLocalPosition;
                _handInstanceRoot.localRotation = Quaternion.Euler(interactHandEuler);
                break;
            case DebugTuningPose.VisibleInteract:
                _handInstanceRoot.localPosition = visibleLocalPosition;
                _handInstanceRoot.localRotation = Quaternion.Euler(interactHandEuler);
                break;
            case DebugTuningPose.VisibleInspect:
                _handInstanceRoot.localPosition = visibleLocalPosition;
                _handInstanceRoot.localRotation = Quaternion.Euler(inspectHandEuler);
                break;
        }

        _handInstanceRoot.localScale = Vector3.one * handUniformScale;
    }
}
