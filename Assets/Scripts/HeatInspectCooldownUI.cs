using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Thin progress bar above the reticle: fill shrinks symmetrically from both edges toward the centre as remaining time drops.
/// </summary>
public class HeatInspectCooldownUI : MonoBehaviour
{
    [Tooltip("If set, the bar is parented under the same canvas and placed above this rect (e.g. Reticle).")]
    public RectTransform alignAboveTarget;

    [Tooltip("Pixels above the reticle pivot (screen space).")]
    public float pixelsAboveReticle = 18f;

    [Tooltip("Total bar size in pixels.")]
    public Vector2 barSize = new Vector2(140f, 6f);

    public Color trackColor = new Color(0f, 0f, 0f, 0.45f);
    public Color fillColor = new Color(1f, 0.55f, 0.12f, 0.92f);

    private RectTransform _root;
    private RectTransform _fillRect;
    private Image _fillImage;
    private float _cachedHalfWidth;

    private void Awake()
    {
        BuildIfNeeded();
        Hide();
    }

    /// <summary>Find Reticle under the given root and attach the bar above it. Safe to call multiple times.</summary>
    public void AutoSetup(Transform searchRoot)
    {
        if (alignAboveTarget != null || searchRoot == null)
            return;

        foreach (var rt in searchRoot.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt.gameObject.name != "Reticle")
                continue;
            alignAboveTarget = rt;
            break;
        }

        BuildIfNeeded();
    }

    private void BuildIfNeeded()
    {
        if (_root != null)
            return;

        if (alignAboveTarget == null)
            return;

        Canvas canvas = alignAboveTarget.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        var go = new GameObject("HeatInspectBar");
        go.layer = alignAboveTarget.gameObject.layer;
        _root = go.AddComponent<RectTransform>();
        _root.SetParent(alignAboveTarget.parent, false);
        _root.SetAsLastSibling();

        _root.anchorMin = new Vector2(0.5f, 0.5f);
        _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = barSize;
        _root.anchoredPosition = alignAboveTarget.anchoredPosition + new Vector2(0f, pixelsAboveReticle);

        var trackGo = new GameObject("Track");
        trackGo.layer = go.layer;
        var trackRt = trackGo.AddComponent<RectTransform>();
        trackRt.SetParent(_root, false);
        trackRt.anchorMin = Vector2.zero;
        trackRt.anchorMax = Vector2.one;
        trackRt.offsetMin = Vector2.zero;
        trackRt.offsetMax = Vector2.zero;
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.color = trackColor;
        trackImg.raycastTarget = false;

        var fillGo = new GameObject("Fill");
        fillGo.layer = go.layer;
        _fillRect = fillGo.AddComponent<RectTransform>();
        _fillRect.SetParent(_root, false);
        _fillRect.anchorMin = Vector2.zero;
        _fillRect.anchorMax = Vector2.one;
        _fillRect.offsetMin = Vector2.zero;
        _fillRect.offsetMax = Vector2.zero;
        _fillImage = fillGo.AddComponent<Image>();
        _fillImage.color = fillColor;
        _fillImage.raycastTarget = false;

        _cachedHalfWidth = Mathf.Max(1f, barSize.x * 0.5f);

        // Root must start hidden: Awake.Hide() runs before AutoSetup may build this hierarchy.
        go.SetActive(false);
    }

    public void Show()
    {
        BuildIfNeeded();
        if (_root != null)
            _root.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (_root != null)
            _root.gameObject.SetActive(false);
        SetFillRemaining(1f);
    }

    /// <param name="remaining01">1 = full bar, 0 = fully depleted (both sides meet at centre).</param>
    public void SetFillRemaining(float remaining01)
    {
        if (_fillRect == null)
            return;

        remaining01 = Mathf.Clamp01(remaining01);
        float w = _root != null ? _root.rect.width : 0f;
        float half = w > 2f ? w * 0.5f : _cachedHalfWidth;
        float inset = (1f - remaining01) * half;
        _fillRect.offsetMin = new Vector2(inset, 0f);
        _fillRect.offsetMax = new Vector2(-inset, 0f);
    }
}
