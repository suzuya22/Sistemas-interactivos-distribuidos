using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class RetroTitleOscillation : MonoBehaviour
{
    [Header("Configuracion")]
    [SerializeField] private float amplitude = 8f;
    [SerializeField] private float speed = 1.2f;
    [SerializeField] private float phaseOffset = 0f;
    [SerializeField] private bool oscillateHorizontal = false;

    private RectTransform rectTransform;
    private Vector2 baseAnchoredPosition;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        baseAnchoredPosition = rectTransform.anchoredPosition;
    }

    private void Update()
    {
        float wave = Mathf.Sin((Time.time * speed) + phaseOffset);
        float offsetY = wave * amplitude;

        float offsetX = oscillateHorizontal ? Mathf.Cos((Time.time * speed * 0.75f) + phaseOffset) * (amplitude * 0.35f) : 0f;

        rectTransform.anchoredPosition = new Vector2(
            baseAnchoredPosition.x + offsetX,
            baseAnchoredPosition.y + offsetY
        );
    }
}
