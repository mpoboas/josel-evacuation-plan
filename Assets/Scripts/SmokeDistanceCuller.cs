using System.Collections.Generic;
using UnityEngine;

public class SmokeDistanceCuller : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float activeDistance = 30f;
    [SerializeField] private float checkInterval = 0.25f;
    [SerializeField] private bool hardDistanceCulling = true;
    [SerializeField] private bool includeNonSmokeParticleSystems;

    private readonly List<ParticleSystem> smokeSystems = new List<ParticleSystem>(16);
    private readonly List<ParticleSystemRenderer> smokeRenderers = new List<ParticleSystemRenderer>(16);
    private readonly List<SmokeDamageTrigger> smokeTriggers = new List<SmokeDamageTrigger>(16);

    private float timer;
    private float rescanTimer;

    private void Awake()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        ResolvePlayerCamera();

        ScanSmokeSystems();
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        if (playerCamera == null)
        {
            ResolvePlayerCamera();
        }

        timer += Time.deltaTime;
        rescanTimer += Time.deltaTime;

        if (rescanTimer >= 2f)
        {
            rescanTimer = 0f;
            ScanSmokeSystems();
        }

        if (timer < checkInterval)
        {
            return;
        }

        timer = 0f;
        ApplyCulling();
    }

    private void ScanSmokeSystems()
    {
        smokeSystems.Clear();
        smokeRenderers.Clear();
        smokeTriggers.Clear();

        ParticleSystem[] all = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            ParticleSystem ps = all[i];
            if (ps == null)
            {
                continue;
            }

            bool isSmokeNamed = ps.name.ToLowerInvariant().Contains("smoke");
            bool hasSmokeTrigger = ps.GetComponent<SmokeDamageTrigger>() != null;
            if (!includeNonSmokeParticleSystems && !isSmokeNamed && !hasSmokeTrigger)
            {
                continue;
            }

            smokeSystems.Add(ps);
            smokeRenderers.Add(ps.GetComponent<ParticleSystemRenderer>());
            smokeTriggers.Add(ps.GetComponent<SmokeDamageTrigger>());
        }
    }

    private void ApplyCulling()
    {
        float distanceSqr = activeDistance * activeDistance;

        for (int i = 0; i < smokeSystems.Count; i++)
        {
            ParticleSystem ps = smokeSystems[i];
            if (ps == null)
            {
                continue;
            }

            Vector3 delta = ps.transform.position - player.position;
            bool inRange = delta.sqrMagnitude <= distanceSqr;

            ParticleSystemRenderer psRenderer = smokeRenderers[i];
            bool visibleToPlayer = IsVisibleToPlayerCamera(psRenderer);
            bool shouldBeActive = hardDistanceCulling ? inRange : (inRange || visibleToPlayer);

            ToggleSystem(ps, psRenderer, smokeTriggers[i], shouldBeActive);
        }
    }

    private void ResolvePlayerCamera()
    {
        if (player == null)
        {
            return;
        }

        Component controller = player.GetComponent("FirstPersonController");
        if (controller != null)
        {
            var field = controller.GetType().GetField("playerCamera");
            if (field != null)
            {
                playerCamera = field.GetValue(controller) as Camera;
            }
        }

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
    }

    private bool IsVisibleToPlayerCamera(ParticleSystemRenderer psRenderer)
    {
        if (psRenderer == null || playerCamera == null)
        {
            return false;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(playerCamera);
        return GeometryUtility.TestPlanesAABB(planes, psRenderer.bounds);
    }

    private static void ToggleSystem(
        ParticleSystem ps,
        ParticleSystemRenderer rendererRef,
        SmokeDamageTrigger smokeTrigger,
        bool enabledState
    )
    {
        var emission = ps.emission;
        emission.enabled = enabledState;

        var collision = ps.collision;
        collision.enabled = enabledState;

        var trigger = ps.trigger;
        trigger.enabled = enabledState;

        if (rendererRef != null)
        {
            rendererRef.enabled = enabledState;
        }

        if (smokeTrigger != null)
        {
            smokeTrigger.enabled = enabledState;
        }

        if (enabledState)
        {
            if (!ps.isPlaying)
            {
                ps.Play(true);
            }
        }
        else if (ps.isPlaying)
        {
            ps.Pause(true);
        }
    }
}
