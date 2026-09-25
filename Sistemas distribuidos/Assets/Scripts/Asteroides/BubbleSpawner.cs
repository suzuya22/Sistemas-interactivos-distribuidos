using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BubbleSpawner : MonoBehaviour
{
    [Header("Configuración de Spawner")]
    public GameObject bubblePrefab;
    public RectTransform spawnArea;
    public Canvas spawnCanvas;
    public float spawnRate = 1.1f;

    [Header("Modificadores Globales de Eventos Espaciales")]
    public static Vector2 CurrentWindVelocity = Vector2.zero;
    public static float CurrentSpeedMultiplier = 1.0f;
    public static float CurrentSpawnRateMultiplier = 1.0f;
    public static bool IsEmpStunActive = false;
    public static bool IsSupernovaActive = false;

    private float timer = 0f;
    private readonly List<GameObject> activeBubbles = new List<GameObject>();
    private CodigoAPI apiManager;
    private Coroutine eventRoutine;

    private void Start()
    {
        if (spawnCanvas == null)
        {
            spawnCanvas = GetComponentInParent<Canvas>();
        }

        apiManager = FindObjectOfType<CodigoAPI>();
    }

    private void OnEnable()
    {
        ResetEventModifiers();

        if (eventRoutine != null) StopCoroutine(eventRoutine);
        eventRoutine = StartCoroutine(SpatialEventCycleRoutine());
    }

    private void OnDisable()
    {
        ResetEventModifiers();
        if (eventRoutine != null) StopCoroutine(eventRoutine);
    }

    private void ResetEventModifiers()
    {
        CurrentWindVelocity = Vector2.zero;
        CurrentSpeedMultiplier = 1.0f;
        CurrentSpawnRateMultiplier = 1.0f;
        IsEmpStunActive = false;
        IsSupernovaActive = false;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float effectiveSpawnRate = spawnRate / Mathf.Max(0.1f, CurrentSpawnRateMultiplier);

        if (timer >= effectiveSpawnRate)
        {
            SpawnMoonWave();
            timer = 0f;
        }
    }

    public void ClearBubbles()
    {
        ResetEventModifiers();

        for (int i = activeBubbles.Count - 1; i >= 0; i--)
        {
            if (activeBubbles[i] != null)
            {
                Destroy(activeBubbles[i]);
            }
        }

        activeBubbles.Clear();
    }

    private void SpawnMoonWave()
    {
        if (bubblePrefab == null || spawnArea == null || spawnCanvas == null) return;

        // Probabilidades: Todas las lunas nacen buenas
        // 45% Normal, 25% Orbital, 15% Evasive, 15% Twins
        float roll = Random.value;

        if (roll < 0.45f)
        {
            SpawnSingleMoon(MoonType.Normal);
        }
        else if (roll < 0.70f)
        {
            SpawnSingleMoon(MoonType.Orbital);
        }
        else if (roll < 0.85f)
        {
            SpawnSingleMoon(MoonType.Evasive);
        }
        else
        {
            SpawnTwinMoons();
        }
    }

    private GameObject SpawnSingleMoon(MoonType type, Vector2? customOffset = null)
    {
        GameObject bubble = Instantiate(bubblePrefab, spawnCanvas.transform, false);
        RectTransform bubbleRT = bubble.GetComponent<RectTransform>();
        if (bubbleRT == null) return bubble;

        bubbleRT.SetAsLastSibling();

        Vector3 worldMin = spawnArea.TransformPoint(spawnArea.rect.min);
        Vector3 worldMax = spawnArea.TransformPoint(spawnArea.rect.max);

        float randomX = Random.Range(worldMin.x, worldMax.x);
        float randomY = Random.Range(worldMin.y, worldMax.y);

        Vector3 localPos = spawnCanvas.transform.InverseTransformPoint(new Vector3(randomX, randomY, 0f));
        if (customOffset.HasValue)
        {
            localPos += (Vector3)customOffset.Value;
        }

        bubbleRT.anchoredPosition = new Vector2(localPos.x, localPos.y);

        BubbleBehavior behavior = bubble.GetComponent<BubbleBehavior>();
        if (behavior != null)
        {
            behavior.Setup(type);
        }

        activeBubbles.Add(bubble);
        return bubble;
    }

    private void SpawnTwinMoons()
    {
        GameObject moonA = SpawnSingleMoon(MoonType.Twin, new Vector2(-25f, 0f));
        GameObject moonB = SpawnSingleMoon(MoonType.Twin, new Vector2(25f, 0f));

        BubbleBehavior behA = moonA.GetComponent<BubbleBehavior>();
        BubbleBehavior behB = moonB.GetComponent<BubbleBehavior>();

        if (behA != null && behB != null)
        {
            behA.Setup(MoonType.Twin, behB, 0f);
            behB.Setup(MoonType.Twin, behA, Mathf.PI);
        }
    }

    #region Ciclo Aleatorio de 5 Eventos Espaciales (Bolsa sin repetición)

    private IEnumerator SpatialEventCycleRoutine()
    {
        // Espera inicial de 5 segundos
        yield return new WaitForSeconds(5.0f);

        // Bolsa de eventos barajada para garantizar variedad total sin repetir el mismo evento 2 veces seguidas
        List<int> eventDeck = new List<int>();

        while (true)
        {
            if (apiManager == null)
            {
                apiManager = FindObjectOfType<CodigoAPI>();
            }

            if (eventDeck.Count == 0)
            {
                eventDeck.AddRange(new int[] { 0, 1, 2, 3, 4 });
                // Barajado Fisher-Yates
                for (int i = 0; i < eventDeck.Count; i++)
                {
                    int randIdx = Random.Range(i, eventDeck.Count);
                    int temp = eventDeck[i];
                    eventDeck[i] = eventDeck[randIdx];
                    eventDeck[randIdx] = temp;
                }
            }

            int chosenEvent = eventDeck[0];
            eventDeck.RemoveAt(0);

            switch (chosenEvent)
            {
                case 0: // 1. VIENTO SOLAR (Fuerza horizontal notable de 480f)
                    int direction = Random.value > 0.5f ? 1 : -1;
                    CurrentWindVelocity = new Vector2(direction * 480f, 0f);
                    if (apiManager != null) apiManager.TriggerSolarWindEvent(direction);
                    yield return new WaitForSeconds(4.5f);
                    CurrentWindVelocity = Vector2.zero;
                    break;

                case 1: // 2. GRAVEDAD CERO (Inercia lenta y ratón pesado)
                    CurrentSpeedMultiplier = 0.5f;
                    if (apiManager != null) apiManager.TriggerZeroGravityEvent();
                    yield return new WaitForSeconds(4.5f);
                    CurrentSpeedMultiplier = 1.0f;
                    break;

                case 2: // 3. LLUVIA DE LUNAS (Aparecen 3 veces más rápido para ganar combos masivos)
                    CurrentSpawnRateMultiplier = 3.0f;
                    if (apiManager != null) apiManager.TriggerMoonRainEvent();
                    yield return new WaitForSeconds(5.0f);
                    CurrentSpawnRateMultiplier = 1.0f;
                    break;

                case 3: // 4. TORMENTA ELECTROMAGNÉTICA (Aturde a las lunas rojas y da zona segura)
                    IsEmpStunActive = true;
                    if (apiManager != null) apiManager.TriggerEmpStormEvent();
                    yield return new WaitForSeconds(5.0f);
                    IsEmpStunActive = false;
                    break;

                case 4: // 5. SUPERNOVA (Puntos dobles, +4 segundos por luna y velocidad x1.3)
                    IsSupernovaActive = true;
                    CurrentSpeedMultiplier = 1.3f;
                    if (apiManager != null) apiManager.TriggerSupernovaEvent();
                    yield return new WaitForSeconds(5.5f);
                    IsSupernovaActive = false;
                    CurrentSpeedMultiplier = 1.0f;
                    break;
            }

            // Intervalo de 5 segundos entre eventos
            yield return new WaitForSeconds(5.0f);
        }
    }

    #endregion
}