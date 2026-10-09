using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using Random = UnityEngine.Random;

/// <summary>
/// Gestor Serverless de Firebase y Lógica del Minijuego (Moon Keeper).
/// Maneja:
/// 1. Autenticación (Email/Password, Registro, Login, Recuperación de contraseña).
/// 2. Realtime Database (Perfiles, Leaderboard en tiempo real y Guardado automático de High Scores).
/// 3. Mecánicas avanzadas compatibles con fuentes Retro/Pixel (Pixelta SDF):
///    - 3 Vidas espaciales (VIDAS: 3 / 3 con colores dinámicos).
///    - Contador de tiempo (TIEMPO: 35s, +2s por cada acierto).
///    - Combos flotantes: Salen en la posición de cada luna al destruirla con rebote,
///      color escalonado dinámico y fade out.
///    - Anuncios cinematográficos de Eventos Espaciales (Viento Solar y Cero Gravedad)
///      completamente dinámicos y sin emojis rotos.
///    - Modal / Texto de Game Over con reinicio al hacer clic o presionar Espacio.
/// </summary>
public class CodigoAPI : MonoBehaviour
{
    private const string DatabaseUrl = "https://sistemas-distribuidos-jslm-default-rtdb.firebaseio.com";

    [Header("Paneles de la UI")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject registerPanel;
    [SerializeField] private GameObject profilePanel; // Lobby
    [SerializeField] private GameObject forgotPasswordPanel;
    [SerializeField] private GameObject gamePanel; // Pantalla de Juego activa

    [Header("Navegación del Lobby y Modos de Juego")]
    [SerializeField] private Button practiceModeButton;
    [SerializeField] private Button findMatchButton;
    [SerializeField] private TMP_Text findMatchButtonText;
    [SerializeField] private Button backToLobbyButton;
    [SerializeField] private Button logoutButton;

    [Header("Configuración de Sesión")]
    [Tooltip("Si está en false, al arrancar el juego siempre empezará en el panel de Login limpio, sin auto-login.")]
    [SerializeField] private bool autoLoginOnStartup = false;

    [Tooltip("Panel o Texto de Game Over. Si se asigna aquí o en gameOverScoreText, se activará al perder.")]
    [SerializeField] private GameObject gameOverPanel;

    [Header("Campos de Entrada (Login)")]
    [SerializeField] private TMP_InputField loginEmailInput;
    [SerializeField] private TMP_InputField loginUsernameInput; // Fallback por compatibilidad
    [SerializeField] private TMP_InputField loginPasswordInput;

    [Header("Campos de Entrada (Registro)")]
    [SerializeField] private TMP_InputField registerUsernameInput;
    [SerializeField] private TMP_InputField registerEmailInput;
    [SerializeField] private TMP_InputField registerPasswordInput;

    [Header("Campos de Entrada (Recuperar Contraseña)")]
    [SerializeField] private TMP_InputField forgotPasswordEmailInput;

    [Header("UI del Minijuego")]
    [SerializeField] private TMP_Text currentScoreText;
    [SerializeField] private TMP_Text livesText;
    [SerializeField] private TMP_Text timerText;

    [Tooltip("Texto en la pantalla de juego para mostrar tu nombre (ej: 'Sebas123').")]
    [SerializeField] private TMP_Text inGamePlayerNameText;

    [Tooltip("Texto en la pantalla de juego para mostrar el rival contra el que vas (ej: 'Rival: jugador2').")]
    [SerializeField] private TMP_Text inGameRivalNameText;

    [Tooltip("Texto para mostrar el puntaje del rival en partida 1v1 (ej: 'Rival: 120 pts').")]
    [SerializeField] private TMP_Text rivalScoreText;

    [Tooltip("Opcional: Si se deja vacío (None), el aviso de combo aparece automáticamente flotando sobre la luna al destruirla y desaparece con fade.")]
    [SerializeField] private TMP_Text comboText;

    [Tooltip("Opcional: Si se deja vacío (None), el anuncio del evento se crea y centra automáticamente en pantalla solo al dispararse.")]
    [SerializeField] private TMP_Text eventAnnouncementText;

    [Tooltip("Texto donde se muestra el resumen de Game Over. Si gameOverPanel está vacío, se usará este directamente.")]
    [SerializeField] private TMP_Text gameOverScoreText;

    [SerializeField] private Button playAgainButton;

    [Header("Configuración del Minijuego")]
    [SerializeField] private int maxLives = 3;
    [SerializeField] private float initialGameTime = 35f;
    [SerializeField] private float maxGameTime = 75f;
    [SerializeField] private float timeBonusPerMoon = 2.0f;

    [Header("Textos de Información y Perfil")]
    [SerializeField] private TMP_Text profileUsernameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text leaderboardText;

    // Estado de Matchmaking 1v1
    private bool isSearchingMatch = false;
    private bool is1v1Match = false;
    private string currentMatchRoomId = "";
    private string rivalUid = "";
    private string rivalUsername = "";
    private int rivalScore = 0;
    private DatabaseReference currentMatchScoreRef;
    private DatabaseReference roomListenerRef;
    private DatabaseReference queueListenerRef;
    private bool isCreatingMatchRoom = false;

    // Estado del Juego
    public int currentScore = 0;
    private int currentLives = 3;
    private float gameTimer = 35f;
    private int currentCombo = 0;
    private int maxComboAchieved = 0;
    private int personalHighScore = 0;
    private bool isGameActive = false;

    // Control de Game Over y reinicio rápido
    private bool isGameOverState = false;
    private float gameOverTimestamp = 0f;
    private TMP_Text dynamicGameOverText;

    // Corrutinas activas de animación
    private Coroutine comboAnimRoutine;
    private Coroutine eventTextRoutine;

    // Instancias de Firebase
    private FirebaseAuth auth;
    private FirebaseDatabase database;
    private DatabaseReference databaseRef;
    private bool isFirebaseInitialized = false;

    private string currentUserId = "";
    private string currentUsername = "";

    public static CodigoAPI Instance { get; private set; }
    public string CurrentUserId => currentUserId;
    public string CurrentUsername => currentUsername;
    public DatabaseReference DatabaseRef => databaseRef;
    public bool IsFirebaseInitialized => isFirebaseInitialized;
    public TMP_FontAsset SharedFont => currentScoreText != null ? currentScoreText.font : null;
    public Material SharedFontMaterial => currentScoreText != null ? currentScoreText.fontSharedMaterial : null;

    public event Action<string, string> OnUserLoggedIn;
    public event Action OnUserLoggedOut;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        currentLives = maxLives;
        gameTimer = initialGameTime;

        if (playAgainButton != null)
        {
            playAgainButton.onClick.AddListener(OnPlayAgainClick);
        }

        if (practiceModeButton != null)
        {
            practiceModeButton.onClick.AddListener(OnPracticeModeClicked);
        }

        if (findMatchButton != null)
        {
            findMatchButton.onClick.AddListener(OnFindMatchClicked);
        }

        // Si backToLobbyButton fue asignado por error al botón de Log out del Perfil
        if (backToLobbyButton != null && profilePanel != null && backToLobbyButton.transform.IsChildOf(profilePanel.transform))
        {
            if (logoutButton == null)
            {
                logoutButton = backToLobbyButton;
            }
            backToLobbyButton = null;
        }

        // Si logoutButton no está asignado, buscarlo automáticamente dentro de profilePanel
        if (logoutButton == null && profilePanel != null)
        {
            Button[] profileButtons = profilePanel.GetComponentsInChildren<Button>(true);
            foreach (Button btn in profileButtons)
            {
                if (btn.gameObject.name.ToLower().Contains("log") || btn.gameObject.name.ToLower().Contains("cerrar"))
                {
                    logoutButton = btn;
                    break;
                }
            }
        }

        if (logoutButton != null)
        {
            logoutButton.onClick.RemoveListener(OnLogoutClick);
            logoutButton.onClick.AddListener(OnLogoutClick);
        }

        // Si backToLobbyButton no está asignado, buscar el botón dentro de gamePanel
        if (backToLobbyButton == null && gamePanel != null)
        {
            Button[] gameButtons = gamePanel.GetComponentsInChildren<Button>(true);
            foreach (Button btn in gameButtons)
            {
                if (btn != playAgainButton)
                {
                    backToLobbyButton = btn;
                    break;
                }
            }
        }

        if (backToLobbyButton != null)
        {
            backToLobbyButton.onClick.RemoveListener(ReturnToLobby);
            backToLobbyButton.onClick.AddListener(ReturnToLobby);
        }

        if (gamePanel != null)
        {
            gamePanel.SetActive(false);
        }

        HideGameOverUI();

        if (comboText != null)
            comboText.gameObject.SetActive(false);

        if (eventAnnouncementText != null)
            eventAnnouncementText.gameObject.SetActive(false);

        InitializeFirebase();
    }

    private void Update()
    {
        // 1. Si la partida está activa, correr temporizador
        bool isGamePanelActive = (gamePanel != null) ? gamePanel.activeInHierarchy : (profilePanel != null && profilePanel.activeInHierarchy);
        if (isGameActive && isGamePanelActive)
        {
            gameTimer -= Time.deltaTime;

            if (gameTimer <= 0f)
            {
                gameTimer = 0f;
                UpdateGameUI();
                TriggerGameOver("SE AGOTO EL TIEMPO!");
                return;
            }

            UpdateGameUI();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            ApplyMouseSlowdownEffect();
#endif
        }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        else
        {
            hadPreviousPoint = false;
        }
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private POINT lastPoint;
    private bool hadPreviousPoint = false;

    private void ApplyMouseSlowdownEffect()
    {
        if (!Application.isFocused)
        {
            hadPreviousPoint = false;
            return;
        }

        float mouseSpeedMultiplier = GetCurrentMouseSpeedMultiplier();

        if (mouseSpeedMultiplier < 0.99f)
        {
            POINT currentPoint;
            if (GetCursorPos(out currentPoint))
            {
                if (hadPreviousPoint)
                {
                    int dx = currentPoint.X - lastPoint.X;
                    int dy = currentPoint.Y - lastPoint.Y;

                    if (dx != 0 || dy != 0)
                    {
                        Vector2 mousePos = Input.mousePosition;
                        if (mousePos.x >= 0 && mousePos.x <= Screen.width && mousePos.y >= 0 && mousePos.y <= Screen.height)
                        {
                            int targetX = Mathf.RoundToInt(lastPoint.X + dx * mouseSpeedMultiplier);
                            int targetY = Mathf.RoundToInt(lastPoint.Y + dy * mouseSpeedMultiplier);

                            SetCursorPos(targetX, targetY);
                            lastPoint.X = targetX;
                            lastPoint.Y = targetY;
                            return;
                        }
                    }
                }
                lastPoint = currentPoint;
                hadPreviousPoint = true;
            }
        }
        else
        {
            hadPreviousPoint = false;
        }
    }
#endif

    /// <summary>
    /// Calcula el multiplicador de velocidad del ratón según los eventos y lunas activas.
    /// </summary>
    public float GetCurrentMouseSpeedMultiplier()
    {
        float mult = 1.0f;

        // 1. Gravedad Cero: Ralentiza el ratón al 45% (efecto de inercia y resistencia espacial)
        if (BubbleSpawner.CurrentSpeedMultiplier < 0.9f)
        {
            mult *= 0.45f;
        }

        // 2. Lunas Rojas en persecución activa: Ralentizan el ratón por atracción gravitacional magnética
        if (BubbleBehavior.ActivePursuitCount > 0)
        {
            mult *= 0.60f;
        }

        return Mathf.Clamp(mult, 0.32f, 1.0f);
    }

    #region 1. Inicialización de Firebase

    private void InitializeFirebase()
    {
        SetStatus("Conectando con Firebase...");

        try
        {
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogWarning($"[Firebase] CheckAndFixDependenciesAsync con fallo: {task.Exception}");
                    TryFallbackFirebaseInit();
                    return;
                }

                try
                {
                    DependencyStatus dependencyStatus = task.Result;
                    if (dependencyStatus == DependencyStatus.Available)
                    {
                        InitFirebaseSuccess();
                    }
                    else
                    {
                        Debug.LogWarning($"[Firebase] Dependencias: {dependencyStatus}. Intentando fallback directo...");
                        TryFallbackFirebaseInit();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Firebase] Excepción leyendo resultado: {ex.Message}");
                    TryFallbackFirebaseInit();
                }
            });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Firebase] Error al invocar CheckAndFixDependenciesAsync: {ex.Message}");
            TryFallbackFirebaseInit();
        }
    }

    private void InitFirebaseSuccess()
    {
        try
        {
            auth = FirebaseAuth.DefaultInstance;
            database = FirebaseDatabase.GetInstance(DatabaseUrl);
            databaseRef = database.RootReference;
            isFirebaseInitialized = true;

            Debug.Log("[Firebase] Inicializado correctamente.");

            if (auth.CurrentUser != null && autoLoginOnStartup)
            {
                currentUserId = auth.CurrentUser.UserId;
                CargarPerfilDeUsuario(currentUserId);
            }
            else
            {
                ShowLoginPanel();
                SetStatus("Listo para iniciar sesión.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Firebase] Error en InitFirebaseSuccess: {ex.Message}");
            TryFallbackFirebaseInit();
        }
    }

    private void TryFallbackFirebaseInit()
    {
        try
        {
            auth = FirebaseAuth.DefaultInstance;
            database = FirebaseDatabase.GetInstance(DatabaseUrl);
            databaseRef = database.RootReference;
            isFirebaseInitialized = true;

            Debug.Log("[Firebase] Inicializado vía fallback directo.");

            if (auth.CurrentUser != null && autoLoginOnStartup)
            {
                currentUserId = auth.CurrentUser.UserId;
                CargarPerfilDeUsuario(currentUserId);
            }
            else
            {
                ShowLoginPanel();
                SetStatus("Listo para iniciar sesión.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Firebase] Fallback directo falló: {ex.Message}");
            SetStatus("Error al conectar con Firebase.");
            ShowLoginPanel();
        }
    }

    private bool EnsureFirebaseInitialized()
    {
        if (isFirebaseInitialized && auth != null && databaseRef != null)
            return true;

        try
        {
            auth = FirebaseAuth.DefaultInstance;
            database = FirebaseDatabase.GetInstance(DatabaseUrl);
            databaseRef = database.RootReference;
            isFirebaseInitialized = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region 2. Navegación entre Paneles de la UI

    public void ShowLoginPanel()
    {
        isGameActive = false;
        isGameOverState = false;
        HideGameOverUI();

        if (loginPanel != null) loginPanel.SetActive(true);
        if (registerPanel != null) registerPanel.SetActive(false);
        if (profilePanel != null) profilePanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        if (gamePanel != null) gamePanel.SetActive(false);
        SetStatus("");
    }

    public void ShowRegisterPanel()
    {
        isGameActive = false;
        isGameOverState = false;
        HideGameOverUI();

        if (loginPanel != null) loginPanel.SetActive(false);
        if (registerPanel != null) registerPanel.SetActive(true);
        if (profilePanel != null) profilePanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        if (gamePanel != null) gamePanel.SetActive(false);
        SetStatus("Ingresa nombre de usuario, correo y contraseña.");
    }

    public void ShowForgotPasswordPanel()
    {
        isGameActive = false;
        isGameOverState = false;
        HideGameOverUI();

        if (loginPanel != null) loginPanel.SetActive(false);
        if (registerPanel != null) registerPanel.SetActive(false);
        if (profilePanel != null) profilePanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(true);
        if (gamePanel != null) gamePanel.SetActive(false);
        SetStatus("Ingresa tu correo para enviarte el enlace de recuperación.");
    }

    private void ShowProfilePanel(string displayName)
    {
        if (loginPanel != null) loginPanel.SetActive(false);
        if (registerPanel != null) registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        if (gamePanel != null) gamePanel.SetActive(false);
        if (profilePanel != null) profilePanel.SetActive(true);

        HideGameOverUI();

        if (profileUsernameText != null)
            profileUsernameText.text = "Bienvenido, " + displayName;

        OnUserLoggedIn?.Invoke(currentUserId, currentUsername);

        // En el lobby el juego permanece en espera de que el jugador elija modo
        isGameActive = false;
    }

    public void OnPracticeModeClicked()
    {
        is1v1Match = false;
        currentMatchRoomId = "";
        rivalUid = "";
        rivalUsername = "";
        rivalScore = 0;

        if (rivalScoreText != null)
            rivalScoreText.gameObject.SetActive(false);

        if (profilePanel != null) profilePanel.SetActive(false);
        if (gamePanel != null) gamePanel.SetActive(true);

        StartGame();
    }

    public void ReturnToLobby()
    {
        isGameActive = false;
        isGameOverState = false;

        BubbleSpawner[] spawners = FindObjectsOfType<BubbleSpawner>();
        foreach (BubbleSpawner spawner in spawners)
        {
            spawner.ClearBubbles();
            spawner.enabled = false;
        }

        if (is1v1Match)
        {
            LeaveMatchmaking();
        }

        HideGameOverUI();

        if (gamePanel != null) gamePanel.SetActive(false);
        if (profilePanel != null) profilePanel.SetActive(true);

        if (!string.IsNullOrEmpty(currentUserId))
        {
            if (profileUsernameText != null)
                profileUsernameText.text = "Bienvenido, " + currentUsername;

            GetLeaderboard();
        }
    }

    #region Matchmaking 1v1 en Tiempo Real (Actividad 5)

    public void OnFindMatchClicked()
    {
        if (!EnsureFirebaseInitialized())
        {
            SetStatus("Firebase conectando... Espera un momento.");
            return;
        }

        if (isSearchingMatch)
        {
            CancelMatchmakingSearch();
        }
        else
        {
            StartMatchmakingSearch();
        }
    }

    private void StartMatchmakingSearch()
    {
        if (string.IsNullOrEmpty(currentUserId)) return;

        isSearchingMatch = true;
        isCreatingMatchRoom = false;

        if (findMatchButtonText != null)
            findMatchButtonText.text = "Cancelar busqueda";

        SetStatus("Buscando oponente 1v1...");

        // 1. Escuchar asignación de sala
        if (roomListenerRef != null)
        {
            roomListenerRef.ValueChanged -= HandlePlayerRoomAssigned;
        }
        roomListenerRef = databaseRef.Child("matchmaking").Child("playerRooms").Child(currentUserId);
        roomListenerRef.ValueChanged += HandlePlayerRoomAssigned;
        roomListenerRef.OnDisconnect().RemoveValue();

        // 2. Registrar presencia en la cola
        var queueData = new Dictionary<string, object>
        {
            { "uid", currentUserId },
            { "username", currentUsername },
            { "timestamp", ServerValue.Timestamp }
        };

        var myQueueRef = databaseRef.Child("matchmaking").Child("queue").Child(currentUserId);
        myQueueRef.SetValueAsync(queueData);
        myQueueRef.OnDisconnect().RemoveValue();

        // 3. Escuchar la cola en tiempo real (si alguien nuevo entra mientras esperamos)
        if (queueListenerRef != null)
        {
            queueListenerRef.ChildAdded -= HandleQueueChildAdded;
        }
        queueListenerRef = databaseRef.Child("matchmaking").Child("queue");
        queueListenerRef.ChildAdded += HandleQueueChildAdded;

        // 4. Verificación inmediata de jugadores que ya estén esperando en la cola
        databaseRef.Child("matchmaking").Child("queue").GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (!isSearchingMatch || isCreatingMatchRoom) return;

            if (task.IsCompletedSuccessfully && task.Result.Exists)
            {
                foreach (DataSnapshot child in task.Result.Children)
                {
                    CheckQueueChildForMatch(child);
                    if (isCreatingMatchRoom) break;
                }
            }
        });
    }

    private void HandleQueueChildAdded(object sender, ChildChangedEventArgs args)
    {
        if (!isSearchingMatch || isCreatingMatchRoom) return;
        if (args.Snapshot == null || !args.Snapshot.Exists) return;

        CheckQueueChildForMatch(args.Snapshot);
    }

    private void CheckQueueChildForMatch(DataSnapshot child)
    {
        if (!isSearchingMatch || isCreatingMatchRoom) return;

        string otherUid = child.Key;
        if (string.IsNullOrEmpty(otherUid) || otherUid == currentUserId) return;

        string otherUsername = child.Child("username").Value?.ToString() ?? "Rival";

        // Regla determinística: El jugador con UID menor alfabéticamente crea la sala.
        // Esto garantiza que solo UN cliente cree la sala y no haya duplicados ni bloqueos.
        bool isHost = string.Compare(currentUserId, otherUid, StringComparison.Ordinal) < 0;

        if (isHost)
        {
            isCreatingMatchRoom = true;
            CreateMatchRoom(currentUserId, currentUsername, otherUid, otherUsername);
        }
    }

    private void HandlePlayerRoomAssigned(object sender, ValueChangedEventArgs args)
    {
        if (!isSearchingMatch) return;
        if (args.Snapshot != null && args.Snapshot.Exists && args.Snapshot.Value != null)
        {
            string roomId = args.Snapshot.Value.ToString();
            if (!string.IsNullOrEmpty(roomId))
            {
                CleanupMatchmakingListeners();
                ConnectToMatchRoom(roomId);
            }
        }
    }

    private void CreateMatchRoom(string p1Uid, string p1Name, string p2Uid, string p2Name)
    {
        string roomId = "room_" + Guid.NewGuid().ToString().Substring(0, 8);

        var scores = new Dictionary<string, object>
        {
            { p1Uid, 0 },
            { p2Uid, 0 }
        };

        var roomData = new Dictionary<string, object>
        {
            { "roomId", roomId },
            { "player1", p1Uid },
            { "player1Name", p1Name },
            { "player2", p2Uid },
            { "player2Name", p2Name },
            { "status", "active" },
            { "startTime", ServerValue.Timestamp },
            { "scores", scores }
        };

        var updates = new Dictionary<string, object>
        {
            { $"matchmaking/rooms/{roomId}", roomData },
            { $"matchmaking/playerRooms/{p1Uid}", roomId },
            { $"matchmaking/playerRooms/{p2Uid}", roomId },
            { $"matchmaking/queue/{p1Uid}", null },
            { $"matchmaking/queue/{p2Uid}", null }
        };

        databaseRef.UpdateChildrenAsync(updates).ContinueWithOnMainThread(task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                CleanupMatchmakingListeners();
                ConnectToMatchRoom(roomId);
            }
            else
            {
                isCreatingMatchRoom = false;
                Debug.LogError($"[CodigoAPI] Error al crear partida en matchmaking: {task.Exception}");
                CancelMatchmakingSearch();
                SetStatus("Error al crear partida.");
            }
        });
    }

    private void CleanupMatchmakingListeners()
    {
        if (roomListenerRef != null)
        {
            roomListenerRef.ValueChanged -= HandlePlayerRoomAssigned;
            roomListenerRef = null;
        }

        if (queueListenerRef != null)
        {
            queueListenerRef.ChildAdded -= HandleQueueChildAdded;
            queueListenerRef = null;
        }

        if (!string.IsNullOrEmpty(currentUserId) && databaseRef != null)
        {
            databaseRef.Child("matchmaking").Child("playerRooms").Child(currentUserId).RemoveValueAsync();
            databaseRef.Child("matchmaking").Child("queue").Child(currentUserId).RemoveValueAsync();
        }
    }

    public void ConnectToMatchRoom(string roomId)
    {
        isSearchingMatch = false;
        isCreatingMatchRoom = false;

        if (findMatchButtonText != null)
            findMatchButtonText.text = "Buscar partida online";

        CleanupMatchmakingListeners();

        currentMatchRoomId = roomId;
        is1v1Match = true;

        if (currentMatchScoreRef != null)
        {
            currentMatchScoreRef.ValueChanged -= HandleRivalScoreChanged;
            currentMatchScoreRef = null;
        }

        databaseRef.Child("matchmaking").Child("rooms").Child(roomId).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompletedSuccessfully && task.Result.Exists)
            {
                DataSnapshot snap = task.Result;
                string p1 = snap.Child("player1").Value?.ToString() ?? "";
                string p2 = snap.Child("player2").Value?.ToString() ?? "";
                string p1Name = snap.Child("player1Name").Value?.ToString() ?? "Jugador 1";
                string p2Name = snap.Child("player2Name").Value?.ToString() ?? "Jugador 2";

                if (currentUserId == p1)
                {
                    rivalUid = p2;
                    rivalUsername = p2Name;
                }
                else
                {
                    rivalUid = p1;
                    rivalUsername = p1Name;
                }

                rivalScore = 0;

                currentMatchScoreRef = databaseRef.Child("matchmaking").Child("rooms").Child(roomId).Child("scores").Child(rivalUid);
                currentMatchScoreRef.ValueChanged += HandleRivalScoreChanged;

                if (profilePanel != null) profilePanel.SetActive(false);
                if (gamePanel != null) gamePanel.SetActive(true);

                if (rivalScoreText != null)
                {
                    rivalScoreText.gameObject.SetActive(true);
                    rivalScoreText.text = $"Rival ({rivalUsername}): 0 pts";
                }

                SetStatus($"¡Partida 1v1 contra {rivalUsername}!");
                StartGame();
            }
        });
    }

    private void HandleRivalScoreChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.Snapshot != null && args.Snapshot.Exists && args.Snapshot.Value != null)
        {
            if (int.TryParse(args.Snapshot.Value.ToString(), out int rScore))
            {
                rivalScore = rScore;
                if (rivalScoreText != null)
                {
                    rivalScoreText.text = $"Rival ({rivalUsername}): {rivalScore} pts";
                }

                if (inGameRivalNameText != null)
                {
                    inGameRivalNameText.text = $"Rival: {rivalUsername} ({rivalScore} pts)";
                }
            }
        }
    }

    public void CancelMatchmakingSearch()
    {
        isSearchingMatch = false;
        isCreatingMatchRoom = false;

        if (findMatchButtonText != null)
            findMatchButtonText.text = "Buscar partida online";

        SetStatus("Busqueda de partida cancelada.");

        CleanupMatchmakingListeners();
    }

    private void LeaveMatchmaking()
    {
        if (currentMatchScoreRef != null)
        {
            currentMatchScoreRef.ValueChanged -= HandleRivalScoreChanged;
            currentMatchScoreRef = null;
        }

        if (!string.IsNullOrEmpty(currentMatchRoomId) && databaseRef != null)
        {
            databaseRef.Child("matchmaking").Child("rooms").Child(currentMatchRoomId).RemoveValueAsync();
        }

        currentMatchRoomId = "";
        is1v1Match = false;
        rivalUid = "";
        rivalUsername = "";
        rivalScore = 0;

        if (rivalScoreText != null)
            rivalScoreText.gameObject.SetActive(false);
    }

    #endregion

    private void HideGameOverUI()
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (gameOverScoreText != null)
            gameOverScoreText.gameObject.SetActive(false);

        if (playAgainButton != null)
            playAgainButton.gameObject.SetActive(false);

        if (dynamicGameOverText != null)
            dynamicGameOverText.gameObject.SetActive(false);
    }

    #endregion

    #region 3. Registro de Usuario y Datos Adicionales

    public void OnRegisterClick()
    {
        if (!EnsureFirebaseInitialized())
        {
            SetStatus("Conectando con Firebase... Espera un momento.");
            return;
        }

        string username = registerUsernameInput != null ? registerUsernameInput.text.Trim() : "";
        string email = registerEmailInput != null ? registerEmailInput.text.Trim() : "";
        string password = registerPasswordInput != null ? registerPasswordInput.text : "";

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            SetStatus("Por favor completa todos los campos para registrarte.");
            return;
        }

        SetStatus("Registrando usuario en Firebase...");

        auth.CreateUserWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled)
            {
                SetStatus("Registro cancelado.");
                return;
            }
            if (task.IsFaulted)
            {
                string errorMsg = ObtenerMensajeDeError(task.Exception);
                Debug.LogError($"[Firebase] Error en registro: {errorMsg}");
                SetStatus(errorMsg);
                return;
            }

            AuthResult result = task.Result;
            FirebaseUser newUser = result.User;
            string userId = newUser.UserId;

            UserProfile profileUpdate = new UserProfile { DisplayName = username };
            newUser.UpdateUserProfileAsync(profileUpdate);

            UserProfileData extraData = new UserProfileData
            {
                userId = userId,
                username = username,
                email = email,
                score = 0,
                fechaRegistro = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                rol = "Jugador"
            };

            string json = JsonUtility.ToJson(extraData);
            databaseRef.Child("users").Child(userId).SetRawJsonValueAsync(json).ContinueWithOnMainThread(dbTask =>
            {
                if (dbTask.IsFaulted)
                {
                    Debug.LogError("[Firebase] Error al guardar datos adicionales en la base de datos.");
                }

                SetStatus("¡Usuario registrado con éxito! Ya puedes iniciar sesión.");
                if (registerUsernameInput != null) registerUsernameInput.text = "";
                if (registerEmailInput != null) registerEmailInput.text = "";
                if (registerPasswordInput != null) registerPasswordInput.text = "";
                ShowLoginPanel();
            });
        });
    }

    #endregion

    #region 4. Inicio de Sesión (Login)

    public void OnLoginClick()
    {
        if (!EnsureFirebaseInitialized())
        {
            SetStatus("Conectando con Firebase... Espera un momento.");
            return;
        }

        string email = "";
        if (loginEmailInput != null && !string.IsNullOrEmpty(loginEmailInput.text))
            email = loginEmailInput.text.Trim();
        else if (loginUsernameInput != null && !string.IsNullOrEmpty(loginUsernameInput.text))
            email = loginUsernameInput.text.Trim();

        string password = loginPasswordInput != null ? loginPasswordInput.text : "";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            SetStatus("Por favor ingresa tu correo y contraseña.");
            return;
        }

        SetStatus("Iniciando sesión...");

        auth.SignInWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled)
            {
                SetStatus("Inicio de sesión cancelado.");
                return;
            }
            if (task.IsFaulted)
            {
                string errorMsg = ObtenerMensajeDeError(task.Exception);
                Debug.LogError($"[Firebase] Error en login: {errorMsg}");
                SetStatus(errorMsg);
                return;
            }

            AuthResult result = task.Result;
            FirebaseUser user = result.User;
            currentUserId = user.UserId;

            SetStatus("Sesión iniciada. Cargando perfil...");
            CargarPerfilDeUsuario(currentUserId);
        });
    }

    private void CargarPerfilDeUsuario(string userId)
    {
        databaseRef.Child("users").Child(userId).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || !task.Result.Exists)
            {
                string fallbackName = auth.CurrentUser?.DisplayName ?? "Jugador";
                currentUsername = fallbackName;
                currentScore = 0;
                personalHighScore = 0;
                ShowProfilePanel(currentUsername);
                GetLeaderboard();
                return;
            }

            string json = task.Result.GetRawJsonValue();
            UserProfileData profile = JsonUtility.FromJson<UserProfileData>(json);

            currentUsername = !string.IsNullOrEmpty(profile.username) ? profile.username : "Jugador";
            personalHighScore = profile.score;
            currentScore = 0;

            ShowProfilePanel(currentUsername);
            SetStatus("¡Bienvenido!");
            GetLeaderboard();
        });
    }

    #endregion

    #region 5. Recuperación de Contraseña

    public void OnForgotPasswordClick()
    {
        if (!EnsureFirebaseInitialized())
        {
            SetStatus("Conectando con Firebase... Espera un momento.");
            return;
        }

        string email = forgotPasswordEmailInput != null ? forgotPasswordEmailInput.text.Trim() : "";

        if (string.IsNullOrEmpty(email))
        {
            if (loginEmailInput != null && !string.IsNullOrEmpty(loginEmailInput.text))
                email = loginEmailInput.text.Trim();
            else if (loginUsernameInput != null && !string.IsNullOrEmpty(loginUsernameInput.text))
                email = loginUsernameInput.text.Trim();
        }

        if (string.IsNullOrEmpty(email))
        {
            SetStatus("Por favor ingresa tu correo para enviarte el enlace de recuperación.");
            return;
        }

        SetStatus("Enviando correo de recuperación...");

        auth.SendPasswordResetEmailAsync(email).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled)
            {
                SetStatus("Operación cancelada.");
                return;
            }
            if (task.IsFaulted)
            {
                string errorMsg = ObtenerMensajeDeError(task.Exception);
                Debug.LogError($"[Firebase] Error al enviar recuperación: {errorMsg}");
                SetStatus(errorMsg);
                return;
            }

            SetStatus("¡Correo enviado! Revisa tu bandeja de entrada o spam para restablecer tu clave.");
            if (forgotPasswordEmailInput != null) forgotPasswordEmailInput.text = "";
        });
    }

    #endregion

    #region 6. Ciclo de Juego, Mecánicas y Combos Dinámicos

    public void StartGame()
    {
        currentScore = 0;
        currentLives = maxLives;
        gameTimer = initialGameTime;
        currentCombo = 0;
        maxComboAchieved = 0;
        isGameActive = true;
        isGameOverState = false;

        HideGameOverUI();

        if (comboText != null)
            comboText.gameObject.SetActive(false);

        // Reactivar y limpiar spawners
        BubbleSpawner[] spawners = FindObjectsOfType<BubbleSpawner>();
        foreach (BubbleSpawner spawner in spawners)
        {
            spawner.enabled = true;
            spawner.ClearBubbles();
        }

        if (inGamePlayerNameText != null)
        {
            inGamePlayerNameText.text = !string.IsNullOrEmpty(currentUsername) ? currentUsername : "Jugador";
        }

        if (inGameRivalNameText != null)
        {
            if (is1v1Match)
            {
                inGameRivalNameText.gameObject.SetActive(true);
                inGameRivalNameText.text = $"Rival: {rivalUsername}";
            }
            else
            {
                inGameRivalNameText.text = "Modo practica";
            }
        }

        UpdateGameUI();
    }

    public void OnMoonPopped(int basePoints)
    {
        OnMoonPopped(basePoints, null);
    }

    public void OnMoonPopped(int basePoints, Vector3 worldPos)
    {
        OnMoonPopped(basePoints, (Vector3?)worldPos);
    }

    public void OnMoonPopped(int basePoints, Vector3? worldPos)
    {
        if (!isGameActive) return;

        // 1. Manejo del combo
        currentCombo++;
        if (currentCombo > maxComboAchieved)
        {
            maxComboAchieved = currentCombo;
        }

        // 2. Suma de puntos con multiplicador de racha (duplicado si hay Supernova)
        int comboMultiplier = Mathf.Max(1, currentCombo);
        if (BubbleSpawner.IsSupernovaActive)
        {
            comboMultiplier *= 2;
        }
        int earnedPoints = basePoints * comboMultiplier;
        currentScore += earnedPoints;

        if (is1v1Match && !string.IsNullOrEmpty(currentMatchRoomId) && databaseRef != null)
        {
            databaseRef.Child("matchmaking").Child("rooms").Child(currentMatchRoomId).Child("scores").Child(currentUserId).SetValueAsync(currentScore);
        }

        // 3. Recompensa de tiempo (+2 segundos normal, +4 segundos en Supernova)
        float bonusTime = BubbleSpawner.IsSupernovaActive ? (timeBonusPerMoon * 2f) : timeBonusPerMoon;
        gameTimer = Mathf.Min(gameTimer + bonusTime, maxGameTime);

        // 4. Pop-up flotante con fade out en la posición de la luna destruida (sin emojis, tamaño compacto)
        if (worldPos.HasValue)
        {
            string popup;
            Color popupColor;
            if (BubbleSpawner.IsSupernovaActive)
            {
                popup = currentCombo > 1 
                    ? $"+{earnedPoints}\n<size=75%>SUPERNOVA x{currentCombo}!</size>" 
                    : $"+{earnedPoints}\n<size=75%>SUPERNOVA!</size>";
                popupColor = new Color(1f, 0.45f, 0f, 1f);
            }
            else
            {
                popup = currentCombo > 1 
                    ? $"+{earnedPoints}\n<size=75%>x{currentCombo}!</size>" 
                    : $"+{earnedPoints}";
                popupColor = GetComboColor(currentCombo);
            }
            SpawnFloatingPopup(worldPos.Value, popup, popupColor, currentCombo > 1 ? 22f : 18f);
        }

        // 5. Si el usuario asignó un texto estático de combo en el Inspector, actualizarlo también
        if (comboText != null)
        {
            comboText.gameObject.SetActive(true);
            comboText.text = BubbleSpawner.IsSupernovaActive 
                ? $"SUPERNOVA x{currentCombo}!" 
                : $"COMBO x{currentCombo}!";
            comboText.color = BubbleSpawner.IsSupernovaActive 
                ? new Color(1f, 0.45f, 0f, 1f) 
                : GetComboColor(currentCombo);

            if (comboAnimRoutine != null) StopCoroutine(comboAnimRoutine);
            comboAnimRoutine = StartCoroutine(PunchScaleRoutine(comboText.transform, 1.45f, 0.22f));
        }

        UpdateGameUI();
    }

    public void OnHazardHit()
    {
        OnHazardHit(null);
    }

    public void OnHazardHit(Vector3 worldPos)
    {
        OnHazardHit((Vector3?)worldPos);
    }

    public void OnHazardHit(Vector3? worldPos)
    {
        if (!isGameActive) return;

        currentLives--;
        currentCombo = 0;

        // Pop-up flotante rojo de peligro (sin emojis, tamaño compacto)
        if (worldPos.HasValue)
        {
            SpawnFloatingPopup(worldPos.Value, "-1 VIDA\n<size=70%>COMBO ROTO!</size>", new Color(1f, 0.12f, 0.28f, 1f), 20f);
        }

        if (comboText != null)
        {
            comboText.gameObject.SetActive(true);
            comboText.text = "COMBO ROTO!";
            comboText.color = new Color(1f, 0.09f, 0.27f, 1f);

            if (comboAnimRoutine != null) StopCoroutine(comboAnimRoutine);
            comboAnimRoutine = StartCoroutine(PunchScaleRoutine(comboText.transform, 1.3f, 0.35f));
        }

        UpdateGameUI();

        if (currentLives <= 0)
        {
            currentLives = 0;
            TriggerGameOver("TE HAS QUEDADO SIN VIDAS!");
        }
    }

    public void AddPoint(int delta = 1)
    {
        if (delta > 0)
        {
            OnMoonPopped(delta);
        }
        else
        {
            OnHazardHit();
        }
    }

    public void SpawnFloatingPopup(Vector3 worldPos, string message, Color color, float fontSize = 20f)
    {
        Canvas targetCanvas = FindObjectOfType<Canvas>();
        if (targetCanvas == null) return;

        GameObject popupObj = new GameObject("FloatingComboPopup", typeof(RectTransform), typeof(TextMeshProUGUI));
        popupObj.transform.SetParent(targetCanvas.transform, false);
        popupObj.transform.position = worldPos;

        RectTransform rt = popupObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160f, 60f);
        rt.SetAsLastSibling();

        TextMeshProUGUI tmp = popupObj.GetComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;

        if (currentScoreText != null && currentScoreText.font != null)
        {
            tmp.font = currentScoreText.font;
            tmp.fontSharedMaterial = currentScoreText.fontSharedMaterial;
        }

        StartCoroutine(AnimateFloatingPopup(popupObj, tmp));
    }

    private IEnumerator AnimateFloatingPopup(GameObject obj, TextMeshProUGUI tmp)
    {
        if (obj == null || tmp == null) yield break;

        RectTransform rt = obj.GetComponent<RectTransform>();
        Vector3 startScale = Vector3.one * 0.5f;
        Vector3 peakScale = Vector3.one * 1.15f;
        Vector3 normalScale = Vector3.one;

        float duration = 0.75f;
        float elapsed = 0f;
        Color baseColor = tmp.color;

        while (elapsed < duration)
        {
            if (obj == null || tmp == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            rt.position += Vector3.up * (60f * Time.deltaTime);

            if (t < 0.22f)
            {
                float popT = t / 0.22f;
                rt.localScale = Vector3.Lerp(startScale, peakScale, popT);
            }
            else if (t < 0.45f)
            {
                float settleT = (t - 0.22f) / 0.23f;
                rt.localScale = Vector3.Lerp(peakScale, normalScale, settleT);
            }

            if (t > 0.45f)
            {
                float fadeT = (t - 0.45f) / 0.55f;
                tmp.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(1f, 0f, fadeT));
            }

            yield return null;
        }

        if (obj != null)
        {
            Destroy(obj);
        }
    }

    /// <summary>
    /// Finaliza la partida, detiene los asteroides, guarda récord en Firebase
    /// y muestra el cartel de Game Over sin emojis rotos.
    /// </summary>
    private void TriggerGameOver(string reason)
    {
        isGameActive = false;
        isGameOverState = true;
        gameOverTimestamp = Time.unscaledTime;

        // Detener y limpiar los asteroides en pantalla
        BubbleSpawner[] spawners = FindObjectsOfType<BubbleSpawner>();
        foreach (BubbleSpawner spawner in spawners)
        {
            spawner.ClearBubbles();
            spawner.enabled = false;
        }

        // Auto-guardado garantizado e inmediato en Firebase al perder
        bool isNewRecord = currentScore > personalHighScore;
        if (isNewRecord)
        {
            personalHighScore = currentScore;
        }
        SaveScoreOnGameOver(currentScore);

        // 1. Activar el texto/panel que el usuario asignó SIN alterar lo que escribió
        if (is1v1Match)
        {
            string outcome;
            if (currentScore > rivalScore)
                outcome = $"VICTORIA!\nTu: {currentScore} pts  -  Rival ({rivalUsername}): {rivalScore} pts";
            else if (currentScore < rivalScore)
                outcome = $"DERROTA\nTu: {currentScore} pts  -  Rival ({rivalUsername}): {rivalScore} pts";
            else
                outcome = $"EMPATE!\nAmbos: {currentScore} pts";

            if (gameOverScoreText != null)
            {
                gameOverScoreText.gameObject.SetActive(true);
                gameOverScoreText.text = outcome;
            }
            else
            {
                TMP_Text dynamicGO = GetOrCreateGameOverText();
                if (dynamicGO != null)
                {
                    dynamicGO.gameObject.SetActive(true);
                    dynamicGO.text = outcome;
                }
            }
        }
        else
        {
            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(true);
            }

            if (gameOverScoreText != null)
            {
                gameOverScoreText.gameObject.SetActive(true);
            }

            // Si no asignó absolutamente nada, solo como salvavidas activamos el cartel básico
            if (gameOverPanel == null && gameOverScoreText == null)
            {
                TMP_Text dynamicGO = GetOrCreateGameOverText();
                if (dynamicGO != null)
                {
                    dynamicGO.gameObject.SetActive(true);
                    dynamicGO.text = "GAME OVER!";
                }
            }
        }

        // 2. Activar el botón de Jugar de Nuevo únicamente al perder
        if (playAgainButton != null)
        {
            playAgainButton.gameObject.SetActive(true);
        }

        UpdateGameUI();
    }

    private TMP_Text GetOrCreateGameOverText()
    {
        if (dynamicGameOverText != null) return dynamicGameOverText;

        Canvas targetCanvas = FindObjectOfType<Canvas>();
        if (targetCanvas == null) return null;

        GameObject goObj = new GameObject("DynamicGameOverText", typeof(RectTransform), typeof(TextMeshProUGUI));
        goObj.transform.SetParent(targetCanvas.transform, false);

        RectTransform rt = goObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(850f, 350f);
        rt.SetAsLastSibling();

        TextMeshProUGUI tmp = goObj.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 42f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        if (currentScoreText != null && currentScoreText.font != null)
        {
            tmp.font = currentScoreText.font;
            tmp.fontSharedMaterial = currentScoreText.fontSharedMaterial;
        }

        dynamicGameOverText = tmp;
        dynamicGameOverText.gameObject.SetActive(false);
        return dynamicGameOverText;
    }

    public void OnPlayAgainClick()
    {
        StartGame();
    }

    public void RestartGame()
    {
        StartGame();
    }

    #endregion

    #region 7. Anuncios Dinámicos de Eventos Espaciales (Retro, sin emojis)

    private TMP_Text GetOrCreateEventText()
    {
        if (eventAnnouncementText != null) return eventAnnouncementText;

        Canvas targetCanvas = FindObjectOfType<Canvas>();
        if (targetCanvas == null) return null;

        GameObject eventObj = new GameObject("DynamicEventAnnouncement", typeof(RectTransform), typeof(TextMeshProUGUI));
        eventObj.transform.SetParent(targetCanvas.transform, false);

        RectTransform rt = eventObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(950f, 150f);
        rt.SetAsLastSibling();

        TextMeshProUGUI tmp = eventObj.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 50f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        if (currentScoreText != null && currentScoreText.font != null)
        {
            tmp.font = currentScoreText.font;
            tmp.fontSharedMaterial = currentScoreText.fontSharedMaterial;
        }

        eventAnnouncementText = tmp;
        eventAnnouncementText.gameObject.SetActive(false);
        return eventAnnouncementText;
    }

    public void TriggerSolarWindEvent(int direction)
    {
        if (eventTextRoutine != null) StopCoroutine(eventTextRoutine);
        eventTextRoutine = StartCoroutine(AnimateSolarWindRoutine(direction));
    }

    public void TriggerZeroGravityEvent()
    {
        if (eventTextRoutine != null) StopCoroutine(eventTextRoutine);
        eventTextRoutine = StartCoroutine(AnimateZeroGravityRoutine());
    }

    public void TriggerMoonRainEvent()
    {
        if (eventTextRoutine != null) StopCoroutine(eventTextRoutine);
        eventTextRoutine = StartCoroutine(AnimateMoonRainRoutine());
    }

    public void TriggerEmpStormEvent()
    {
        if (eventTextRoutine != null) StopCoroutine(eventTextRoutine);
        eventTextRoutine = StartCoroutine(AnimateEmpStormRoutine());
    }

    public void TriggerSupernovaEvent()
    {
        if (eventTextRoutine != null) StopCoroutine(eventTextRoutine);
        eventTextRoutine = StartCoroutine(AnimateSupernovaRoutine());
    }

    private IEnumerator AnimateSolarWindRoutine(int direction)
    {
        TMP_Text banner = GetOrCreateEventText();
        if (banner == null)
        {
            SetStatus(direction > 0 ? "!VIENTO SOLAR HACIA LA DERECHA >>!" : "!VIENTO SOLAR HACIA LA IZQUIERDA <<!");
            yield break;
        }

        RectTransform rt = banner.rectTransform;
        Vector2 originalPos = Vector2.zero;
        rt.anchoredPosition = originalPos;
        rt.localScale = Vector3.zero;
        rt.localRotation = Quaternion.identity;

        string arrow = direction > 0 ? ">>" : "<<";
        banner.text = $"!RAFAGA DE VIENTO SOLAR {arrow}!";
        banner.color = new Color(1f, 0.88f, 0.1f, 1f);
        banner.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.deltaTime;
            float s = Mathf.Lerp(0f, 1.25f, elapsed / 0.3f);
            rt.localScale = Vector3.one * s;
            yield return null;
        }
        rt.localScale = Vector3.one;

        yield return new WaitForSeconds(1.7f);

        elapsed = 0f;
        float speed = 2500f;
        while (elapsed < 0.55f)
        {
            elapsed += Time.deltaTime;
            rt.anchoredPosition += new Vector2(direction * speed * Time.deltaTime, 0f);
            Color c = banner.color;
            c.a = Mathf.Lerp(1f, 0f, elapsed / 0.55f);
            banner.color = c;
            yield return null;
        }

        banner.gameObject.SetActive(false);
        rt.anchoredPosition = originalPos;
        rt.localScale = Vector3.one;
    }

    private IEnumerator AnimateZeroGravityRoutine()
    {
        TMP_Text banner = GetOrCreateEventText();
        if (banner == null)
        {
            SetStatus("!CAMPO DE GRAVEDAD CERO! (Velocidad reducida al 50%)");
            yield break;
        }

        RectTransform rt = banner.rectTransform;
        Vector2 originalPos = Vector2.zero;
        rt.anchoredPosition = originalPos;
        rt.localScale = Vector3.zero;
        rt.localRotation = Quaternion.identity;

        banner.text = "!GRAVEDAD CERO!";
        banner.color = new Color(0f, 0.95f, 1f, 1f);
        banner.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < 0.35f)
        {
            elapsed += Time.deltaTime;
            rt.localScale = Vector3.one * Mathf.Lerp(0f, 1.2f, elapsed / 0.35f);
            yield return null;
        }

        float floatTime = 3.8f;
        elapsed = 0f;
        while (elapsed < floatTime)
        {
            elapsed += Time.deltaTime;
            rt.anchoredPosition += new Vector2(0f, 60f * Time.deltaTime);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(elapsed * 3.5f) * 12f);

            if (elapsed > floatTime - 0.8f)
            {
                Color c = banner.color;
                c.a = Mathf.Lerp(1f, 0f, (elapsed - (floatTime - 0.8f)) / 0.8f);
                banner.color = c;
            }

            yield return null;
        }

        banner.gameObject.SetActive(false);
        rt.anchoredPosition = originalPos;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;
    }

    private IEnumerator AnimateMoonRainRoutine()
    {
        TMP_Text banner = GetOrCreateEventText();
        if (banner == null)
        {
            SetStatus("!LLUVIA DE LUNAS! FRENESI (SPAWN X3)");
            yield break;
        }

        RectTransform rt = banner.rectTransform;
        Vector2 originalPos = Vector2.zero;
        rt.anchoredPosition = originalPos;
        rt.localScale = Vector3.zero;
        rt.localRotation = Quaternion.identity;

        banner.text = "!LLUVIA DE LUNAS!\n<size=60%>FRENESI (SPAWN X3)</size>";
        banner.color = new Color(0.46f, 1f, 0.01f, 1f); // Neón Lima #76FF03
        banner.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < 0.28f)
        {
            elapsed += Time.deltaTime;
            float s = Mathf.Lerp(0f, 1.3f, elapsed / 0.28f);
            rt.localScale = Vector3.one * s;
            yield return null;
        }
        rt.localScale = Vector3.one;

        float displayTime = 2.4f;
        elapsed = 0f;
        while (elapsed < displayTime)
        {
            elapsed += Time.deltaTime;
            float jitter = Mathf.Sin(elapsed * 32f) * 6f;
            rt.anchoredPosition = originalPos + new Vector2(jitter, 0f);

            if (elapsed > displayTime - 0.6f)
            {
                Color c = banner.color;
                c.a = Mathf.Lerp(1f, 0f, (elapsed - (displayTime - 0.6f)) / 0.6f);
                banner.color = c;
            }
            yield return null;
        }

        banner.gameObject.SetActive(false);
        rt.anchoredPosition = originalPos;
        rt.localScale = Vector3.one;
    }

    private IEnumerator AnimateEmpStormRoutine()
    {
        TMP_Text banner = GetOrCreateEventText();
        if (banner == null)
        {
            SetStatus("!TORMENTA ELECTROMAGNETICA! LUNAS ROJAS DESACTIVADAS");
            yield break;
        }

        RectTransform rt = banner.rectTransform;
        Vector2 originalPos = Vector2.zero;
        rt.anchoredPosition = originalPos;
        rt.localScale = Vector3.zero;
        rt.localRotation = Quaternion.identity;

        banner.text = "!TORMENTA ELECTROMAGNETICA!\n<size=55%>LUNAS ROJAS DESACTIVADAS</size>";
        banner.color = new Color(0.88f, 0.25f, 1f, 1f); // Violeta Eléctrico #E040FB
        banner.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.deltaTime;
            rt.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.25f, elapsed / 0.3f);
            yield return null;
        }
        rt.localScale = Vector3.one;

        float displayTime = 2.4f;
        elapsed = 0f;
        while (elapsed < displayTime)
        {
            elapsed += Time.deltaTime;
            float glitchX = (UnityEngine.Random.value - 0.5f) * 12f;
            rt.anchoredPosition = originalPos + new Vector2(glitchX, 0f);

            float flicker = 0.75f + Mathf.Sin(elapsed * 25f) * 0.25f;
            Color c = banner.color;
            if (elapsed > displayTime - 0.6f)
            {
                c.a = Mathf.Lerp(flicker, 0f, (elapsed - (displayTime - 0.6f)) / 0.6f);
            }
            else
            {
                c.a = flicker;
            }
            banner.color = c;

            yield return null;
        }

        banner.gameObject.SetActive(false);
        rt.anchoredPosition = originalPos;
        rt.localScale = Vector3.one;
    }

    private IEnumerator AnimateSupernovaRoutine()
    {
        TMP_Text banner = GetOrCreateEventText();
        if (banner == null)
        {
            SetStatus("!SUPERNOVA! PUNTOS Y TIEMPO X2");
            yield break;
        }

        RectTransform rt = banner.rectTransform;
        Vector2 originalPos = Vector2.zero;
        rt.anchoredPosition = originalPos;
        rt.localScale = Vector3.zero;
        rt.localRotation = Quaternion.identity;

        banner.text = "!SUPERNOVA!\n<size=55%>PUNTOS Y TIEMPO X2</size>";
        banner.color = new Color(1f, 0.43f, 0f, 1f); // Naranja Cósmico #FF6D00
        banner.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < 0.35f)
        {
            elapsed += Time.deltaTime;
            float s = Mathf.Lerp(0f, 1.4f, elapsed / 0.35f);
            rt.localScale = Vector3.one * s;
            yield return null;
        }

        float displayTime = 2.5f;
        elapsed = 0f;
        while (elapsed < displayTime)
        {
            elapsed += Time.deltaTime;
            float pulse = 1.0f + Mathf.Sin(elapsed * 8f) * 0.08f;
            rt.localScale = Vector3.one * pulse;

            if (elapsed > displayTime - 0.7f)
            {
                Color c = banner.color;
                c.a = Mathf.Lerp(1f, 0f, (elapsed - (displayTime - 0.7f)) / 0.7f);
                banner.color = c;
            }

            yield return null;
        }

        banner.gameObject.SetActive(false);
        rt.anchoredPosition = originalPos;
        rt.localScale = Vector3.one;
    }

    #endregion

    #region 8. Auto-Guardado y Leaderboard en Firebase

    private void SaveScoreOnGameOver(int finalScore)
    {
        if (string.IsNullOrEmpty(currentUserId) || !isFirebaseInitialized) return;

        bool isNewRecord = finalScore > personalHighScore;
        if (isNewRecord)
        {
            personalHighScore = finalScore;
        }

        // Siempre guardamos en Firebase el récord más alto alcanzado por el usuario
        int scoreToPersist = Mathf.Max(personalHighScore, finalScore);

        Debug.Log($"[Firebase] Auto-guardando récord en Firebase al perder: {scoreToPersist} pts (Nuevo récord: {isNewRecord})");

        databaseRef.Child("users").Child(currentUserId).Child("score").SetValueAsync(scoreToPersist).ContinueWithOnMainThread(task =>
        {
            if (!task.IsFaulted)
            {
                Debug.Log($"[Firebase] Puntaje ({scoreToPersist} pts) guardado automáticamente al perder.");
                SetStatus(isNewRecord ? "!NUEVO RECORD GUARDADO EN FIREBASE!" : "Puntaje guardado en Firebase.");
                GetLeaderboard();
            }
            else
            {
                Debug.LogError("[Firebase] Error al guardar puntaje automático al perder.");
            }
        });
    }

    /// <summary>
    /// Botón de compatibilidad: Como el puntaje se guarda automáticamente al perder,
    /// si el botón anterior 'Actualizar score' ahora se usa para 'Jugar de nuevo',
    /// este método reinicia la partida de inmediato sin tener que reconfigurar el OnClick en el Inspector.
    /// </summary>
    public void OnSaveScoreClick()
    {
        OnPlayAgainClick();
    }

    public void OnRefreshLeaderboardClick()
    {
        GetLeaderboard();
    }

    private void GetLeaderboard()
    {
        if (!isFirebaseInitialized) return;

        if (leaderboardText != null)
            leaderboardText.text = "<b>CARGANDO TABLA...</b>";

        databaseRef.Child("users").OrderByChild("score").LimitToLast(10).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || !task.Result.Exists)
            {
                if (leaderboardText != null)
                    leaderboardText.text = "<b>TABLA DE POSICIONES</b>\nNo hay puntajes registrados.";
                return;
            }

            List<UserProfileData> lista = new List<UserProfileData>();

            foreach (DataSnapshot child in task.Result.Children)
            {
                string json = child.GetRawJsonValue();
                if (!string.IsNullOrEmpty(json))
                {
                    UserProfileData user = JsonUtility.FromJson<UserProfileData>(json);
                    lista.Add(user);
                }
            }

            lista.Sort((a, b) => b.score.CompareTo(a.score));

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("<b>TABLA DE POSICIONES</b>");
            builder.AppendLine("----------------------------");

            int rank = 1;
            foreach (UserProfileData user in lista)
            {
                string nombre = !string.IsNullOrEmpty(user.username) ? user.username : "Anónimo";

                if (rank == 1)
                    builder.AppendLine($"{rank}. <color=#FFD700><b>{nombre}</b></color> - {user.score} pts");
                else
                    builder.AppendLine($"{rank}. <b>{nombre}</b> - {user.score} pts");

                rank++;
            }

            if (leaderboardText != null)
                leaderboardText.text = builder.ToString();
        });
    }

    #endregion

    #region 9. Formato y Utilidades de la UI (Sin emojis, fuente Retro Pixelta)

    private void UpdateGameUI()
    {
        // 1. Textos dedicados si están enlazados en el Inspector
        if (livesText != null)
        {
            livesText.text = GetLivesString();
        }

        if (timerText != null)
        {
            int sec = Mathf.Max(0, Mathf.CeilToInt(gameTimer));
            timerText.text = sec <= 5
                ? $"<color=#FF1744><b>TIEMPO: {sec}s</b></color>"
                : $"TIEMPO: {sec}s";
        }

        // 2. Score principal y fallback unificado
        if (currentScoreText != null)
        {
            if (livesText == null || timerText == null)
            {
                int sec = Mathf.Max(0, Mathf.CeilToInt(gameTimer));
                string comboStr = currentCombo > 1 ? $" (x{currentCombo})" : "";
                currentScoreText.text = $"PUNTOS: <b>{currentScore}</b>{comboStr}   |   {GetLivesString()}   |   TIEMPO: <b>{sec}s</b>";
            }
            else
            {
                currentScoreText.text = "PUNTOS: " + currentScore;
            }
        }
    }

    /// <summary>
    /// Formato retro para las vidas (sin emojis, usando colores dinámicos).
    /// </summary>
    private string GetLivesString()
    {
        string colorHex = currentLives switch
        {
            3 => "#00E5FF", // Cian Cósmico (Lleno)
            2 => "#FFEA00", // Amarillo Neón (Alerta)
            1 => "#FF1744", // Carmesí (Peligro)
            _ => "#888888"  // Gris (Sin vidas)
        };
        return $"<color={colorHex}>VIDAS: {currentLives} / {maxLives}</color>";
    }

    private Color GetComboColor(int combo)
    {
        if (combo <= 2) return Color.white;
        if (combo <= 5) return new Color(0f, 0.9f, 1f, 1f);       // Cian Neón (#00E5FF)
        if (combo <= 9) return new Color(1f, 0.92f, 0.016f, 1f);  // Amarillo Neón (#FFEA00)
        if (combo <= 14) return new Color(1f, 0.57f, 0f, 1f);     // Naranja Eléctrico (#FF9100)
        return new Color(1f, 0.09f, 0.27f, 1f);                  // Carmesí Intenso (#FF1744)
    }

    private IEnumerator PunchScaleRoutine(Transform targetTransform, float punchMagnitude, float duration)
    {
        if (targetTransform == null) yield break;
        Vector3 baseScale = Vector3.one;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (targetTransform == null) yield break;
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float scaleMultiplier = Mathf.Lerp(punchMagnitude, 1.0f, Mathf.Sin(t * Mathf.PI * 0.5f));
            targetTransform.localScale = baseScale * scaleMultiplier;
            yield return null;
        }

        if (targetTransform != null)
            targetTransform.localScale = baseScale;
    }

    public void OnLogoutClick()
    {
        isGameActive = false;
        isGameOverState = false;
        HideGameOverUI();

        if (isSearchingMatch)
        {
            CancelMatchmakingSearch();
        }
        if (is1v1Match)
        {
            LeaveMatchmaking();
        }

        OnUserLoggedOut?.Invoke();

        if (auth != null)
            auth.SignOut();

        currentUserId = "";
        currentUsername = "";
        currentScore = 0;
        personalHighScore = 0;

        BubbleSpawner[] spawners = FindObjectsOfType<BubbleSpawner>();
        foreach (BubbleSpawner spawner in spawners)
        {
            spawner.ClearBubbles();
        }

        if (loginPasswordInput != null)
            loginPasswordInput.text = "";

        ShowLoginPanel();

        // Garantizar de forma inmediata y explícita el apagado de perfil y juego
        if (profilePanel != null) profilePanel.SetActive(false);
        if (gamePanel != null) gamePanel.SetActive(false);
        if (loginPanel != null) loginPanel.SetActive(true);

        SetStatus("Sesión cerrada.");
    }

    public void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private string ObtenerMensajeDeError(AggregateException exception)
    {
        if (exception == null) return "Error desconocido.";

        foreach (var inner in exception.Flatten().InnerExceptions)
        {
            if (inner is FirebaseException fbEx)
            {
                AuthError errorCode = (AuthError)fbEx.ErrorCode;
                return errorCode switch
                {
                    AuthError.WrongPassword => "Contraseña incorrecta.",
                    AuthError.UserNotFound => "No existe una cuenta con este correo.",
                    AuthError.EmailAlreadyInUse => "Este correo electrónico ya está registrado.",
                    AuthError.InvalidEmail => "El correo electrónico no tiene un formato válido.",
                    AuthError.WeakPassword => "La contraseña es muy débil (mínimo 6 caracteres).",
                    AuthError.MissingEmail => "Por favor ingresa un correo electrónico.",
                    AuthError.MissingPassword => "Por favor ingresa una contraseña.",
                    _ => fbEx.Message
                };
            }
        }

        return exception.InnerException?.Message ?? exception.Message;
    }

    #endregion
}

#region Estructuras de Datos de Firebase

[Serializable]
public class UserProfileData
{
    public string userId;
    public string username;
    public string email;
    public int score;
    public string fechaRegistro;
    public string rol;
}

#endregion