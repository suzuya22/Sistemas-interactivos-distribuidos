using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Componente para la plantilla de la card pequeña de notificación en la esquina.
/// </summary>
public class ToastCardItem : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Image stripeImage;

    public void Setup(string title, string message, Color accentColor)
    {
        if (titleText != null)
        {
            titleText.text = title;
            titleText.color = accentColor;
        }

        if (messageText != null)
        {
            messageText.text = message;
        }

        if (stripeImage != null)
        {
            stripeImage.color = accentColor;
        }
    }
}
