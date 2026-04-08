using UnityEngine;
using TMPro;

public class SliderScript : MonoBehaviour
{
    [Header("Referência do Texto")]
    public TextMeshProUGUI valueText;

    // Esta função será ativada automaticamente pelo Slider
    public void UpdateText(float value)
    {
        // Converte o número do slider para texto. 
        // O "0" formata como número inteiro. Se quiser casas decimais, use "0.0" ou "0.00".
        valueText.text = value.ToString("0"); 
    }
}
