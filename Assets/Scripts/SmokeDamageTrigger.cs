using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class SmokeDamageTrigger : MonoBehaviour
{
    [Header("Smoke Damage")]
    [SerializeField] private bool applySmokeDamageToPlayer = false;
    [SerializeField] private SmokeHealthReceiver smokeHealthReceiver;
    [SerializeField] private SmokeVisionEffect smokeVisionEffect;
    [SerializeField] private Collider playerCollider;
    [SerializeField] private float baseDamagePerParticle = 1f;
    [SerializeField] private int initialBufferSize = 256;

    [Header("Flame Damage")]
    [SerializeField] private bool applyFlameDamageToPlayer = true;
    [SerializeField] private float baseFlameDamagePerSecond = 20f;
    [SerializeField] private float flameEffectRadius = 1.35f;

    private ParticleSystem particleSystemRef;
    private List<ParticleSystem.Particle> insideParticles;
    private Transform playerTransform;
    private readonly List<Transform> flameSources = new List<Transform>(4);

    public void Configure(SmokeHealthReceiver receiver, float damagePerParticle = -1f)
    {
        smokeHealthReceiver = receiver;
        if (damagePerParticle > 0f)
        {
            baseDamagePerParticle = damagePerParticle;
        }
    }

    private void Awake()
    {
        particleSystemRef = GetComponent<ParticleSystem>();
        insideParticles = new List<ParticleSystem.Particle>(Mathf.Max(16, initialBufferSize));
        TryResolveReceiver();
        TryResolveVisionEffect();
        TryResolvePlayerCollider();
        TryResolvePlayerTransform();
        EnsureTriggerColliderAssigned();
        ScanFlameSources();
    }

    private void OnParticleTrigger()
    {
        if (particleSystemRef == null)
        {
            return;
        }

        if (playerCollider == null)
        {
            TryResolvePlayerCollider();
            EnsureTriggerColliderAssigned();
        }

        int insideCount = particleSystemRef.GetTriggerParticles(
            ParticleSystemTriggerEventType.Inside,
            insideParticles
        );

        if (insideCount <= 0)
        {
            return;
        }

        if (smokeVisionEffect == null)
        {
            TryResolveVisionEffect();
        }

        if (smokeVisionEffect != null)
        {
            smokeVisionEffect.SetSmokeExposure(insideCount);
        }

        if (!applySmokeDamageToPlayer)
        {
            return;
        }

        if (smokeHealthReceiver == null)
        {
            TryResolveReceiver();
            if (smokeHealthReceiver == null)
            {
                return;
            }
        }

        float damage = insideCount * baseDamagePerParticle * Time.deltaTime;
        smokeHealthReceiver.TakeSmokeDamage(damage);
    }

    private void Update()
    {
        if (playerTransform == null)
        {
            TryResolvePlayerTransform();
        }

        if (smokeVisionEffect == null)
        {
            TryResolveVisionEffect();
        }

        if (smokeHealthReceiver == null)
        {
            TryResolveReceiver();
        }

        if (smokeVisionEffect != null)
        {
            bool suppressSmokeVisual = smokeHealthReceiver != null && smokeHealthReceiver.IsPlayerCrouched();
            smokeVisionEffect.SetSmokeVisualSuppressed(suppressSmokeVisual);
        }

        if (flameSources.Count == 0)
        {
            ScanFlameSources();
        }

        ApplyFlameHazard();
    }

    private void TryResolveReceiver()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            smokeHealthReceiver = player.GetComponent<SmokeHealthReceiver>();
        }
    }

    private void TryResolveVisionEffect()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            smokeVisionEffect = player.GetComponent<SmokeVisionEffect>();
        }
    }

    private void TryResolvePlayerTransform()
    {
        if (playerTransform != null)
        {
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
    }

    private void TryResolvePlayerCollider()
    {
        if (playerCollider != null)
        {
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            return;
        }

        playerCollider = player.GetComponent<Collider>();
        if (playerCollider == null)
        {
            playerCollider = player.GetComponentInChildren<Collider>();
        }
    }

    private void EnsureTriggerColliderAssigned()
    {
        if (particleSystemRef == null || playerCollider == null)
        {
            return;
        }

        var trigger = particleSystemRef.trigger;
        if (!trigger.enabled)
        {
            trigger.enabled = true;
        }

        trigger.SetCollider(0, playerCollider);
    }

    private void ScanFlameSources()
    {
        flameSources.Clear();

        Transform root = transform.root;
        if (root == null)
        {
            return;
        }

        ParticleSystem[] allSystems = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < allSystems.Length; i++)
        {
            ParticleSystem ps = allSystems[i];
            if (ps == null || ps == particleSystemRef)
            {
                continue;
            }

            if (!ps.name.ToLowerInvariant().Contains("flame"))
            {
                continue;
            }

            flameSources.Add(ps.transform);
        }
    }

    private void ApplyFlameHazard()
    {
        if (playerTransform == null || flameSources.Count == 0)
        {
            return;
        }

        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < flameSources.Count; i++)
        {
            Transform source = flameSources[i];
            if (source == null)
            {
                continue;
            }

            float distance = Vector3.Distance(playerTransform.position, source.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
            }
        }

        if (!float.IsFinite(nearestDistance) || flameEffectRadius <= 0f)
        {
            return;
        }

        float normalizedExposure = Mathf.Clamp01(1f - (nearestDistance / flameEffectRadius));
        if (normalizedExposure <= 0f)
        {
            return;
        }

        if (smokeVisionEffect != null)
        {
            smokeVisionEffect.SetFlameExposure01(normalizedExposure);
        }

        if (applyFlameDamageToPlayer && smokeHealthReceiver != null)
        {
            float flameDamage = baseFlameDamagePerSecond * normalizedExposure * Time.deltaTime;
            smokeHealthReceiver.TakeFlameDamage(flameDamage);
        }
    }
}
