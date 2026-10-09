using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Componente para la plantilla (molde) de cada tarjeta de usuario en la lista.
/// </summary>
public class UserCardItem : MonoBehaviour
{
    [Header("Referencias de la Tarjeta")]
    [Tooltip("Texto para el nombre del usuario.")]
    [SerializeField] private TMP_Text usernameText;

    [Tooltip("Texto para el estado ('En linea' / 'Desconectado').")]
    [SerializeField] private TMP_Text statusText;

    [Tooltip("Puntito de color para el estado (verde / gris). Opcional.")]
    [SerializeField] private Image statusDotImage;

    [Tooltip("Botón de acción ('+' para agregar o 'VS' para retar).")]
    [SerializeField] private Button actionButton;

    [Tooltip("Texto dentro del botón de acción.")]
    [SerializeField] private TMP_Text actionButtonText;

    [Tooltip("Etiqueta de estado fija (ej: 'Amigo', 'Pendiente'). Opcional.")]
    [SerializeField] private TMP_Text statusBadgeText;

    /// <summary>
    /// Configura los datos visuales de la tarjeta según el usuario y su estado.
    /// </summary>
    public void Setup(string uid, string username, bool isOnline, bool isFriend, bool isPending, Action onActionClick)
    {
        if (usernameText != null)
        {
            usernameText.text = username;
            usernameText.enableWordWrapping = false;
            usernameText.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (statusText != null)
        {
            statusText.text = isOnline ? "<color=#00E676>●</color>" : "<color=#888888>●</color>";
        }

        if (statusDotImage != null)
        {
            statusDotImage.color = isOnline ? new Color(0f, 0.9f, 0.45f, 1f) : new Color(0.5f, 0.55f, 0.6f, 1f);
        }

        if (statusBadgeText != null)
        {
            statusBadgeText.gameObject.SetActive(false);
        }

        // 1. Si ya es AMIGO: el botón se convierte en "VS" para retar a duelo 1v1
        if (isFriend)
        {
            if (actionButton != null)
            {
                actionButton.gameObject.SetActive(true);
                actionButton.interactable = isOnline; // Solo puede retar si está en línea
                if (actionButtonText != null) actionButtonText.text = "VS";

                actionButton.onClick.RemoveAllListeners();
                if (onActionClick != null)
                {
                    actionButton.onClick.AddListener(() =>
                    {
                        actionButton.interactable = false;
                        if (actionButtonText != null) actionButtonText.text = "...";
                        onActionClick();
                    });
                }
            }
        }
        // 2. Si ya le enviamos solicitud de amistad y está pendiente
        else if (isPending)
        {
            if (actionButton != null)
            {
                actionButton.gameObject.SetActive(true);
                actionButton.interactable = false;
                if (actionButtonText != null) actionButtonText.text = "...";
            }
        }
        // 3. Usuario no agregado: botón "+" para mandar solicitud de amistad
        else
        {
            if (actionButton != null)
            {
                actionButton.gameObject.SetActive(true);
                actionButton.interactable = true;
                if (actionButtonText != null) actionButtonText.text = "+";

                actionButton.onClick.RemoveAllListeners();
                if (onActionClick != null)
                {
                    actionButton.onClick.AddListener(() =>
                    {
                        actionButton.interactable = false;
                        if (actionButtonText != null) actionButtonText.text = "...";
                        onActionClick();
                    });
                }
            }
        }
    }
}
