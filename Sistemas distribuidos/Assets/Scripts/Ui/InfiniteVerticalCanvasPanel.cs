using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Repite verticalmente un panel de UI para crear un scroll infinito.
/// Usa un panel plantilla y tres copias para evitar cortes visibles.
/// </summary>
public class InfiniteVerticalCanvasPanel : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private RectTransform sourcePanel;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform contentRoot;

    [Header("Movimiento")]
    [SerializeField] private float scrollSpeed = 120f;
    [SerializeField] private bool useUnscaledTime = true;

    private readonly RectTransform[] layers = new RectTransform[3];
    private Vector2 startAnchoredPosition;
    private float panelHeight;
    private float scrollOffsetY;
    private bool isInitialized;

    private void Awake()
    {
        InitializePanel();
    }

    private void Update()
    {
        if (!isInitialized)
            return;

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float movement = scrollSpeed * deltaTime;
        scrollOffsetY = Mathf.Repeat(scrollOffsetY + movement, panelHeight); //[cite: 3]

        ApplyLayerPositions();
    }

    public void RefreshPanel()
    {
        ClearGeneratedLayers();
        InitializePanel();
    }

    private void InitializePanel()
    {
        if (sourcePanel == null)
            sourcePanel = GetComponentInChildren<RectTransform>(true);

        if (sourcePanel == null)
        {
            Debug.LogError("[InfiniteVerticalCanvasPanel] No hay RectTransform plantilla asignado.");
            return;
        }

        if (sourcePanel == transform as RectTransform)
        {
            Debug.LogError("[InfiniteVerticalCanvasPanel] La plantilla no puede ser el mismo objeto que tiene este script. Usa un panel hijo como plantilla.");
            return;
        }

        if (viewport == null)
            viewport = sourcePanel.parent as RectTransform;

        if (viewport == null)
            viewport = transform as RectTransform;

        if (contentRoot == null)
            contentRoot = viewport;

        if (viewport == null || contentRoot == null)
        {
            Debug.LogError("[InfiniteVerticalCanvasPanel] No se pudo resolver el contenedor del panel.");
            return;
        }

        RectMask2D mask = viewport.GetComponent<RectMask2D>();
        if (mask == null)
            mask = viewport.gameObject.AddComponent<RectMask2D>();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(sourcePanel);
        LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);

        panelHeight = GetPanelHeight(sourcePanel);
        if (panelHeight <= 0.01f)
        {
            Debug.LogError("[InfiniteVerticalCanvasPanel] No se pudo calcular la altura del panel.");
            return;
        }

        startAnchoredPosition = sourcePanel.anchoredPosition;
        scrollOffsetY = 0f;

        layers[0] = CreateLayer("Panel_0", Vector2.zero);
        layers[1] = CreateLayer("Panel_1", Vector2.up * panelHeight);
        layers[2] = CreateLayer("Panel_2", Vector2.up * panelHeight * 2f); //[cite: 3]

        sourcePanel.gameObject.SetActive(false); //[cite: 3]
        ApplyLayerPositions();

        isInitialized = true;
    }

    private RectTransform CreateLayer(string layerName, Vector2 localOffset)
    {
        GameObject layerObject = Instantiate(sourcePanel.gameObject, contentRoot); //[cite: 3]
        layerObject.name = layerName;

        // MODIFICACIÓN CLAVE: Destruir todos los hijos (contenido) del panel clonado
        // Esto asegura que solo se repita la imagen base del panel.
        foreach (Transform child in layerObject.transform)
        {
            Destroy(child.gameObject);
        }

        RectTransform rectTransform = layerObject.GetComponent<RectTransform>();
        rectTransform.anchoredPosition = startAnchoredPosition + localOffset;
        rectTransform.localScale = sourcePanel.localScale;
        rectTransform.localRotation = sourcePanel.localRotation;
        rectTransform.SetAsFirstSibling(); //[cite: 3]

        return rectTransform;
    }

    private float GetPanelHeight(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return 0f;

        float height = rectTransform.rect.height;
        if (height > 0.01f)
            return height;

        return rectTransform.sizeDelta.y;
    }

    private void ApplyLayerPositions()
    {
        if (!isInitialized)
            return;

        Vector2 basePosition = startAnchoredPosition + Vector2.down * scrollOffsetY;

        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == null)
                continue;

            layers[i].anchoredPosition = basePosition + Vector2.up * (panelHeight * i);
        }
    }

    private void ClearGeneratedLayers()
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] != null)
                Destroy(layers[i].gameObject);

            layers[i] = null;
        }

        isInitialized = false;
    }

    private void OnValidate()
    {
        if (scrollSpeed < 0f)
            scrollSpeed = 0f;
    }

    private void OnDestroy()
    {
        ClearGeneratedLayers();
    }
}