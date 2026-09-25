using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public enum MoonType
{
    Normal,         // Blanco (+1 pt, +2 seg)
    Evasive,        // Amarillo (+4 pts, +2 seg, corre del mouse)
    MagneticHazard, // Modo Furia Roja (-1 vida, atraído al mouse durante 2s)
    Orbital,        // Cian (+2 pts, +2 seg, movimiento zigzag/espiral)
    Twin            // Púrpura (+2 pts, +2 seg, órbita mutua, furia si la otra muere)
}

public enum HazardPhase
{
    None,           // Estado normal pacífico
    Warning,        // 1 segundo de aviso (se hace roja y tiembla, pero no persigue todavía)
    Pursuit         // 2 segundos de embestida (persigue activamente al cursor y daña al tocarlo)
}

[RequireComponent(typeof(Button))]
public class BubbleBehavior : MonoBehaviour
{
    [Header("Tipo de Luna")]
    public MoonType moonType = MoonType.Normal;

    [Header("Movimiento")]
    public float minSpeed = 90f;
    public float maxSpeed = 210f;
    public float rotationSpeed = 18f;

    [Header("Audio")]
    public AudioClip goodPopSound;
    public AudioClip badPopSound;
    public float volume = 0.8f;

    private float speed;
    private CodigoAPI apiManager;
    private Image bubbleImage;
    private float baseRotation;
    private float blinkPhase;
    private AudioSource audioSource;
    private bool isPopped = false;

    // Físicas específicas
    private BubbleBehavior twinCompanion;
    private float orbitAngle = 0f;
    private bool isTwinEnraged = false;

    // Contador global de lunas en persecución activa para ralentizar el ratón
    public static int ActivePursuitCount = 0;

    // Fases de Mutación Roja
    public HazardPhase currentHazardPhase = HazardPhase.None;
    private float warningDuration = 1.0f;  // 1 segundo de aviso previo
    private float pursuitDuration = 2.0f;  // 2 segundos de embestida
    private float phaseTimer = 0f;
    private float corruptionTriggerTime;
    private float lifeTimer = 0f;

    // Colores Neón característicos
    private static readonly Color ColorNormal = Color.white;
    private static readonly Color ColorEvasive = new Color(1f, 0.92f, 0.016f, 1f);      // Amarillo Neón #FFEA00
    private static readonly Color ColorHazard = new Color(1f, 0.09f, 0.27f, 1f);        // Rojo Carmesí #FF1744
    private static readonly Color ColorOrbital = new Color(0f, 0.9f, 1f, 1f);           // Cian Cósmico #00E5FF
    private static readonly Color ColorTwin = new Color(0.835f, 0f, 0.976f, 1f);        // Púrpura Neón #D500F9

    private void Awake()
    {
        bubbleImage = GetComponent<Image>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.volume = volume;
        audioSource.spatialBlend = 0f;
    }

    private void Start()
    {
        apiManager = FindObjectOfType<CodigoAPI>();
        speed = Random.Range(minSpeed, maxSpeed);
        baseRotation = Random.Range(-15f, 15f);
        blinkPhase = Random.Range(0f, Mathf.PI * 2f);

        // Cada luna muta rápidamente en un tiempo aleatorio entre 0.6 y 2.5 segundos
        corruptionTriggerTime = Random.Range(0.6f, 2.5f);

        ApplyTypeVisuals();

        GetComponent<Button>().onClick.AddListener(Pop);
        Destroy(gameObject, 10f);
    }

    private void OnDestroy()
    {
        if (currentHazardPhase == HazardPhase.Pursuit)
        {
            ActivePursuitCount = Mathf.Max(0, ActivePursuitCount - 1);
        }
    }

    /// <summary>
    /// Configura el tipo de luna y parámetros especiales (usado por el Spawner).
    /// </summary>
    public void Setup(MoonType type, BubbleBehavior companion = null, float angle = 0f)
    {
        moonType = type;
        twinCompanion = companion;
        orbitAngle = angle;
        ApplyTypeVisuals();
    }

    private void ApplyTypeVisuals()
    {
        if (bubbleImage == null) bubbleImage = GetComponent<Image>();
        if (bubbleImage == null) return;

        switch (moonType)
        {
            case MoonType.Normal:
                bubbleImage.color = ColorNormal;
                break;
            case MoonType.Evasive:
                bubbleImage.color = ColorEvasive;
                break;
            case MoonType.MagneticHazard:
                bubbleImage.color = ColorHazard;
                break;
            case MoonType.Orbital:
                bubbleImage.color = ColorOrbital;
                break;
            case MoonType.Twin:
                bubbleImage.color = ColorTwin;
                break;
        }
    }

    private void Update()
    {
        if (isPopped) return;

        float speedMultiplier = BubbleSpawner.CurrentSpeedMultiplier;
        float effectiveSpeed = speed * speedMultiplier;

        if (isTwinEnraged)
        {
            effectiveSpeed *= 2.5f;
        }

        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, transform.position);
        Vector2 mousePos = Input.mousePosition;
        float distanceToMouse = Vector2.Distance(screenPos, mousePos);

        // Control de Fases: Normal -> Aviso (1s) -> Persecución (2s) -> Calma
        if (BubbleSpawner.IsEmpStunActive)
        {
            if (currentHazardPhase != HazardPhase.None)
            {
                ExitHazardPhases();
            }
        }
        else
        {
            switch (currentHazardPhase)
            {
                case HazardPhase.None:
                    lifeTimer += Time.deltaTime;
                    if (lifeTimer >= corruptionTriggerTime)
                    {
                        EnterWarningPhase();
                    }
                    break;

                case HazardPhase.Warning:
                    phaseTimer += Time.deltaTime;

                    // Visual de aviso: se vuelve roja y tiembla durante 1 segundo antes de atacar
                    if (bubbleImage != null)
                    {
                        float warnPulse = 0.5f + Mathf.Sin(Time.time * 26f) * 0.5f;
                        bubbleImage.color = Color.Lerp(ColorHazard, Color.white, warnPulse * 0.4f);
                    }

                    // Temblor de advertencia
                    float warnShake = Mathf.Sin(Time.time * 35f) * 14f;
                    transform.rotation = Quaternion.Euler(0f, 0f, baseRotation + warnShake);

                    if (phaseTimer >= warningDuration)
                    {
                        EnterPursuitPhase();
                    }
                    break;

                case HazardPhase.Pursuit:
                    phaseTimer += Time.deltaTime;

                    if (bubbleImage != null)
                    {
                        float pulse = 0.5f + Mathf.Sin(Time.time * 20f) * 0.5f;
                        bubbleImage.color = Color.Lerp(ColorHazard, new Color(0.6f, 0f, 0.1f, 1f), pulse * 0.35f);
                    }

                    // Daño por contacto: Si la luna roja toca el cursor del mouse, daña al jugador inmediatamente
                    if (distanceToMouse < 60f)
                    {
                        Pop();
                        return;
                    }

                    if (phaseTimer >= pursuitDuration)
                    {
                        ExitHazardPhases();
                    }
                    break;
            }
        }

        Vector3 movement = Vector3.zero;

        // FÍSICAS DE MOVIMIENTO
        if (currentHazardPhase == HazardPhase.Pursuit)
        {
            // Embestida directa hacia el cursor del mouse
            Vector2 pullDir = (mousePos - screenPos).normalized;
            movement = (Vector3)pullDir * (effectiveSpeed * 2.6f) * Time.deltaTime;
            movement += (Vector3)BubbleSpawner.CurrentWindVelocity * 0.6f * Time.deltaTime;

            float shake = Mathf.Sin(Time.time * 30f) * 15f;
            transform.rotation = Quaternion.Euler(0f, 0f, baseRotation + shake);
        }
        else
        {
            // Movimiento normal hacia arriba (o ligeramente más lento durante el segundo de aviso)
            float verticalSpeed = (currentHazardPhase == HazardPhase.Warning) ? effectiveSpeed * 0.6f : effectiveSpeed;
            movement = Vector3.up * verticalSpeed * Time.deltaTime;
            movement += (Vector3)BubbleSpawner.CurrentWindVelocity * Time.deltaTime;

            switch (moonType)
            {
                case MoonType.Evasive:
                    if (distanceToMouse < 130f && distanceToMouse > 0.01f)
                    {
                        Vector2 fleeDir = (screenPos - mousePos).normalized;
                        movement += (Vector3)fleeDir * (effectiveSpeed * 1.8f) * Time.deltaTime;
                        transform.rotation = Quaternion.Euler(0f, 0f, baseRotation + Mathf.Sin(Time.time * 20f) * 25f);
                    }
                    break;

                case MoonType.Orbital:
                    float wave = Mathf.Sin(Time.time * 4f + blinkPhase) * 110f;
                    movement += Vector3.right * wave * Time.deltaTime;
                    transform.rotation = Quaternion.Euler(0f, 0f, baseRotation + wave * 0.15f);
                    break;

                case MoonType.Twin:
                    if (twinCompanion != null)
                    {
                        orbitAngle += Time.deltaTime * 3.5f;
                        Vector3 orbitOffset = new Vector3(Mathf.Cos(orbitAngle), Mathf.Sin(orbitAngle) * 0.4f, 0) * 35f * Time.deltaTime;
                        movement += orbitOffset;
                    }
                    else if (isTwinEnraged && bubbleImage != null)
                    {
                        float enragePulse = 0.5f + Mathf.Sin(Time.time * 15f) * 0.5f;
                        bubbleImage.color = Color.Lerp(ColorTwin, Color.white, enragePulse);
                    }
                    break;

                case MoonType.Normal:
                default:
                    float drift = Mathf.Sin(Time.time * 1.5f + blinkPhase) * 4f;
                    transform.rotation = Quaternion.Euler(0f, 0f, baseRotation + drift);
                    break;
            }
        }

        transform.position += movement;
    }

    private void EnterWarningPhase()
    {
        if (BubbleSpawner.IsEmpStunActive) return;
        currentHazardPhase = HazardPhase.Warning;
        phaseTimer = 0f;
        if (bubbleImage != null)
        {
            bubbleImage.color = ColorHazard;
        }
    }

    private void EnterPursuitPhase()
    {
        currentHazardPhase = HazardPhase.Pursuit;
        phaseTimer = 0f;
        ActivePursuitCount++;
    }

    private void ExitHazardPhases()
    {
        if (currentHazardPhase == HazardPhase.Pursuit)
        {
            ActivePursuitCount = Mathf.Max(0, ActivePursuitCount - 1);
        }

        currentHazardPhase = HazardPhase.None;
        lifeTimer = 0f;
        corruptionTriggerTime = Random.Range(1.8f, 3.8f);
        ApplyTypeVisuals();
    }

    /// <summary>
    /// Notifica a esta luna que su gemela fue destruida, activando el modo furia.
    /// </summary>
    public void OnCompanionPopped()
    {
        twinCompanion = null;
        isTwinEnraged = true;
    }

    private void Pop()
    {
        if (isPopped) return;
        isPopped = true;

        if (currentHazardPhase == HazardPhase.Pursuit)
        {
            ActivePursuitCount = Mathf.Max(0, ActivePursuitCount - 1);
        }

        bool isHazard = (currentHazardPhase == HazardPhase.Warning || currentHazardPhase == HazardPhase.Pursuit || moonType == MoonType.MagneticHazard);
        AudioClip clipToPlay = isHazard ? badPopSound : goodPopSound;

        if (audioSource != null && clipToPlay != null)
        {
            audioSource.PlayOneShot(clipToPlay, volume);
            StartCoroutine(DestroyAfterAudio(clipToPlay.length));
        }
        else
        {
            Destroy(gameObject);
        }

        if (bubbleImage != null) bubbleImage.enabled = false;

        // Notificar al gestor de juego
        if (apiManager != null)
        {
            if (isHazard)
            {
                apiManager.OnHazardHit(transform.position);
            }
            else
            {
                int points = moonType switch
                {
                    MoonType.Evasive => 4,
                    MoonType.Orbital => 2,
                    MoonType.Twin => 2,
                    _ => 1
                };
                apiManager.OnMoonPopped(points, transform.position);
            }
        }

        // Si era gemela, enfurecer a la sobreviviente
        if (twinCompanion != null)
        {
            twinCompanion.OnCompanionPopped();
        }
    }

    private IEnumerator DestroyAfterAudio(float delay)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, delay));
        Destroy(gameObject);
    }
}