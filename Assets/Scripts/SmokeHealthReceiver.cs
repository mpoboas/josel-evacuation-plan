using System.Reflection;
using UnityEngine;

public class SmokeHealthReceiver : MonoBehaviour
{
    public float health = 100f;

    [Header("Debug")]
    [SerializeField] private bool logPostureInfoOnDamage = true;
    [SerializeField] private bool immuneToSmokeWhileCrouched = true;

    private CharacterController characterController;
    private CapsuleCollider capsuleCollider;
    private Component firstPersonController;
    private PropertyInfo isCrouchedProperty;
    private FieldInfo isCrouchedField;
    private bool gameOverLogged;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        firstPersonController = GetComponent("FirstPersonController");

        if (firstPersonController != null)
        {
            isCrouchedProperty = firstPersonController.GetType().GetProperty(
                "IsCrouched",
                BindingFlags.Instance | BindingFlags.Public
            );
            isCrouchedField = firstPersonController.GetType().GetField(
                "isCrouched",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        }
    }

    public void TakeSmokeDamage(float damageAmount)
    {
        ApplyEnvironmentalDamage(damageAmount, ignoreWhenCrouched: true, sourceLabel: "Smoke");
    }

    public void TakeFlameDamage(float damageAmount)
    {
        ApplyEnvironmentalDamage(damageAmount, ignoreWhenCrouched: false, sourceLabel: "Flame");
    }

    private void ApplyEnvironmentalDamage(
        float damageAmount,
        bool ignoreWhenCrouched,
        string sourceLabel
    )
    {
        if (damageAmount <= 0f || gameOverLogged)
        {
            return;
        }

        if (ignoreWhenCrouched && immuneToSmokeWhileCrouched && IsPlayerCrouched())
        {
            return;
        }

        health -= damageAmount;
        var stats = GameplaySessionStats.Instance;
        if (stats != null)
        {
            if (sourceLabel == "Smoke")
                stats.RegisterSmokeDamage(damageAmount);
            else if (sourceLabel == "Flame")
                stats.RegisterFireDamage(damageAmount, transform.position);
        }

        if (logPostureInfoOnDamage)
        {
            float ccHeight = characterController != null ? characterController.height : -1f;
            float capsuleHeight = capsuleCollider != null ? capsuleCollider.height : -1f;
            bool? isCrouched = TryGetCrouchedState();

            Debug.Log(
                $"[SmokeHealthReceiver] Source={sourceLabel} | Damage={damageAmount:F3} | Health={health:F2} | CharacterController.height={ccHeight:F2} | CapsuleCollider.height={capsuleHeight:F2} | IsCrouched={isCrouched?.ToString() ?? "unknown"}"
            );
        }

        if (health <= 0f)
        {
            gameOverLogged = true;
            Debug.Log("Game Over: Inalação de Fumo");
        }
    }

    public bool IsPlayerCrouched()
    {
        return TryGetCrouchedState() == true;
    }

    private bool? TryGetCrouchedState()
    {
        if (firstPersonController == null)
        {
            return null;
        }

        if (isCrouchedProperty != null)
        {
            object propertyValue = isCrouchedProperty.GetValue(firstPersonController);
            if (propertyValue is bool crouchedFromProperty)
            {
                return crouchedFromProperty;
            }
        }

        if (isCrouchedField != null)
        {
            object fieldValue = isCrouchedField.GetValue(firstPersonController);
            if (fieldValue is bool crouchedFromField)
            {
                return crouchedFromField;
            }
        }

        return null;
    }
}
