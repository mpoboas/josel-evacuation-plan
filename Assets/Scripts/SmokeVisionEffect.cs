using UnityEngine;
using UnityEngine.UI;

public class SmokeVisionEffect : MonoBehaviour
{
    [Header("Trigger To Vision Mapping")]
    [SerializeField] private float particlesForFullEffect = 6f;
    [SerializeField, Range(0f, 1f)] private float minSmokeExposureWhileInside = 0.92f;
    [SerializeField] private float riseSpeed = 6f;
    [SerializeField] private float decaySpeed = 2f;

    [Header("Smoke Visual Strength")]
    [SerializeField] private float maxFogAlpha = 0.96f;
    [SerializeField] private float maxVignetteAlpha = 1f;
    
    [Header("Flame Visual Strength")]
    [SerializeField] private float maxFlameTintAlpha = 0.35f;

    private Image fogImage;
    private Image vignetteImage;
    private Image flameTintImage;
    private float smokeTargetExposure;
    private float smokeCurrentExposure;
    private float flameTargetExposure;
    private float flameCurrentExposure;
    private bool suppressSmokeVisual;

    private void Awake()
    {
        CreateOverlay();
    }

    private void Update()
    {
        if (suppressSmokeVisual)
        {
            smokeTargetExposure = 0f;
            smokeCurrentExposure = Mathf.MoveTowards(smokeCurrentExposure, 0f, riseSpeed * Time.deltaTime);
        }

        smokeTargetExposure = Mathf.MoveTowards(smokeTargetExposure, 0f, decaySpeed * Time.deltaTime);
        float smokeSpeed = smokeCurrentExposure < smokeTargetExposure ? riseSpeed : decaySpeed;
        smokeCurrentExposure = Mathf.MoveTowards(
            smokeCurrentExposure,
            smokeTargetExposure,
            smokeSpeed * Time.deltaTime
        );

        flameTargetExposure = Mathf.MoveTowards(flameTargetExposure, 0f, decaySpeed * Time.deltaTime);
        float flameSpeed = flameCurrentExposure < flameTargetExposure ? riseSpeed : decaySpeed;
        flameCurrentExposure = Mathf.MoveTowards(
            flameCurrentExposure,
            flameTargetExposure,
            flameSpeed * Time.deltaTime
        );

        ApplyExposure(smokeCurrentExposure, flameCurrentExposure);
    }

    public void SetParticleExposure(int insideParticleCount)
    {
        SetSmokeExposure(insideParticleCount);
    }

    public void SetSmokeExposure(int insideParticleCount)
    {
        if (suppressSmokeVisual)
        {
            return;
        }

        if (insideParticleCount <= 0)
        {
            return;
        }

        float normalizedFromParticles = Mathf.Clamp01(insideParticleCount / Mathf.Max(1f, particlesForFullEffect));
        float normalized = Mathf.Max(minSmokeExposureWhileInside, normalizedFromParticles);
        smokeTargetExposure = Mathf.Max(smokeTargetExposure, normalized);
    }

    public void SetSmokeVisualSuppressed(bool suppressed)
    {
        suppressSmokeVisual = suppressed;
        if (suppressed)
        {
            smokeTargetExposure = 0f;
        }
    }

    public void SetFlameExposure01(float normalizedExposure)
    {
        if (normalizedExposure <= 0f)
        {
            return;
        }

        flameTargetExposure = Mathf.Max(flameTargetExposure, Mathf.Clamp01(normalizedExposure));
    }

    private void CreateOverlay()
    {
        GameObject canvasGO = new GameObject("SmokeVisionCanvas");
        canvasGO.transform.SetParent(transform, false);

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.AddComponent<GraphicRaycaster>().enabled = false;

        fogImage = CreateFullscreenImage(canvasGO.transform, "SmokeFog");
        fogImage.color = new Color(0.02f, 0.02f, 0.02f, 0f);

        vignetteImage = CreateFullscreenImage(canvasGO.transform, "SmokeVignette");
        vignetteImage.sprite = CreateVignetteSprite();
        vignetteImage.type = Image.Type.Simple;
        vignetteImage.color = new Color(0.01f, 0.01f, 0.01f, 0f);

        flameTintImage = CreateFullscreenImage(canvasGO.transform, "FlameTint");
        flameTintImage.color = new Color(0.95f, 0.12f, 0.06f, 0f);
    }

    private static Image CreateFullscreenImage(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go.AddComponent<Image>();
    }

    private void ApplyExposure(float smokeExposure, float flameExposure)
    {
        if (fogImage != null)
        {
            Color c = fogImage.color;
            c.a = smokeExposure * maxFogAlpha;
            fogImage.color = c;
        }

        if (vignetteImage != null)
        {
            Color c = vignetteImage.color;
            c.a = smokeExposure * maxVignetteAlpha;
            vignetteImage.color = c;
        }

        if (flameTintImage != null)
        {
            Color c = flameTintImage.color;
            c.a = flameExposure * maxFlameTintAlpha;
            flameTintImage.color = c;
        }
    }

    private static Sprite CreateVignetteSprite()
    {
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxR = center.magnitude;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / maxR;
                float edge = Mathf.SmoothStep(0.45f, 1f, d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, edge));
            }
        }

        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
