using UnityEngine;

public class PlayerEvacuation : MonoBehaviour
{
    [Header("References")]
    public SmokeSimulator smokeSimulator;

    [Header("Player Stats")]
    public float health = 100f;
    public float standingHeadY = 1.8f;
    public float crouchingHeadY = 0.8f;

    [Header("Smoke Settings")]
    public float dangerThreshold = 0.3f;
    public float damagePerSecond = 15f;

    private bool isCrouching = false;
    private float dangerStartTime = -1f;

    void Update()
    {
        // 1. Input para Agachar (Muda isto se usares o novo Input System do Unity)
        isCrouching = Input.GetKey(KeyCode.Space);

        // 2. Calcular a posicao exata da cabeca do jogador no espaco
        float currentHeadY = isCrouching ? crouchingHeadY : standingHeadY;
        Vector3 headPosition = transform.position + new Vector3(0, currentHeadY, 0);

        // 3. Obter densidade de fumo a partir do Simulador
        float smokeAtHead = smokeSimulator.GetDensityAtWorldPosition(headPosition);

        // 4. Logica de Danos e Tempo de Reacao (US002 / US003)
        if (smokeAtHead > dangerThreshold)
        {
            // Perder Vida
            health -= damagePerSecond * Time.deltaTime;
            health = Mathf.Max(health, 0);

            // Iniciar cronometro de reacao se nao estiver agachado
            if (!isCrouching && dangerStartTime < 0)
            {
                dangerStartTime = Time.time;
            }

            Debug.Log($"[PERIGO] Vida: {health:F0} | Densidade na cabeca: {smokeAtHead:F2}");
        }
        else
        {
            // Ar limpo, resetar cronometro de perigo inicial
            dangerStartTime = -1f;
        }

        // Se o jogador agachou para escapar da camada de fumo
        if (isCrouching && dangerStartTime >= 0)
        {
            float reactionTimeMs = (Time.time - dangerStartTime) * 1000f;
            Debug.Log($"<color=green>[SUCESSO] Tempo de Reacao: {reactionTimeMs:F0} ms</color>");

            // Para o cronometro para nao registar continuamente enquanto esta agachado
            dangerStartTime = -1f;
        }
    }
}
