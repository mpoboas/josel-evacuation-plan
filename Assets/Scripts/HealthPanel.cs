using UnityEngine;
using TMPro;

public class HealthPanel : MonoBehaviour
{
    [SerializeField] private SmokeHealthReceiver playerHealthReceiver;
    [SerializeField] private TextMeshProUGUI healthText;

    private void Start()
    {
        if (playerHealthReceiver == null)
        {
            // Tenta encontrar o SmokeHealthReceiver no jogador pelo nome ou tag, caso não tenha sido atribuído no Inspector.
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                playerHealthReceiver = player.GetComponent<SmokeHealthReceiver>();
            }
        }
        
        if (healthText == null)
        {
            // Tenta obter o componente de texto neste mesmo GameObject
            healthText = GetComponent<TextMeshProUGUI>();
            if (healthText == null)
            {
                healthText = GetComponentInChildren<TextMeshProUGUI>();
            }
        }
    }

    private void Update()
    {
        if (playerHealthReceiver != null && healthText != null)
        {
            // Mostra a saúde arredondada para evitar muitas casas decimais
            healthText.text = Mathf.CeilToInt(playerHealthReceiver.health).ToString();
        }
    }
}
