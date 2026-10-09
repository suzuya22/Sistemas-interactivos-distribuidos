using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// Gestor del Menú Lateral Desplegable Social y Notificaciones Toast en Esquina.
/// Controla la lógica de:
/// - Presencia en línea (Actividad 2)
/// - Amigos y Solicitudes de amistad (Actividad 3)
/// - Notificaciones Toast en la esquina (Actividad 4)
/// </summary>
public class SocialSidebarManager : MonoBehaviour
{
    public static SocialSidebarManager Instance { get; private set; }

    private const string DatabaseUrl = "https://sistemas-distribuidos-jslm-default-rtdb.firebaseio.com";

    [Header("=== 1. MENÚ LATERAL (DRAWER) ===")]
    [Tooltip("El RectTransform del panel lateral que se va a deslizar.")]
    [SerializeField] private RectTransform sidebarPanel;

    [Tooltip("El botón que tiene la flecha para abrir o cerrar el menú.")]
    [SerializeField] private Button toggleDrawerButton;

    [Tooltip("Imagen o flecha para rotar o cambiar (opcional).")]
    [SerializeField] private RectTransform toggleArrowTransform;

    [Header("=== POSICIONES DEL MENÚ (COPIA Y PEGA O USA CLIC DERECHO) ===")]
    [Tooltip("Posición (X, Y) de anchoredPosition cuando el menú está ABIERTO / DESPLEGADO.")]
    [SerializeField] private Vector2 openedPosition = new Vector2(0f, 0f);

    [Tooltip("Posición (X, Y) de anchoredPosition cuando el menú está CERRADO / OCULTO.")]
    [SerializeField] private Vector2 closedPosition = new Vector2(-480f, 0f);

    [Tooltip("¿El menú empieza cerrado al iniciar el juego? Si marcas false, se queda donde lo colocaste en la escena.")]
    [SerializeField] private bool startClosed = true;

    [Tooltip("Tiempo en segundos que tarda la animación de deslizar.")]
    [SerializeField] private float drawerAnimDuration = 0.22f;

    [Header("=== 2. ENCABEZADO Y PERFIL ===")]
    [Tooltip("Texto donde se muestra tu estado actual (ej: 'MiNombre (En linea)').")]
    [SerializeField] private TMP_Text myStatusText;

    [Header("=== 3. BOTONES DE PESTAÑAS ===")]
    [Tooltip("Botón para ver usuarios en línea.")]
    [SerializeField] private Button tabOnlineButton;

    [Tooltip("Botón para ver tu lista de amigos.")]
    [SerializeField] private Button tabFriendsButton;

    [Header("=== 4. LISTA CON SCROLL ===")]
    [Tooltip("El objeto 'Content' dentro del ScrollRect donde se instanciarán los usuarios/amigos.")]
    [SerializeField] private Transform listContentContainer;

    [Tooltip("Plantilla de la tarjeta de un usuario o amigo (objeto con componente UserCardItem).")]
    [SerializeField] private UserCardItem userCardTemplate;

    [Tooltip("Texto de mensaje vacío cuando no hay nadie en la lista (opcional).")]
    [SerializeField] private TMP_Text emptyListMessageText;

    [Header("=== 5. SECCIÓN INFERIOR DE SOLICITUDES ===")]
    [Tooltip("El panel o contenedor inferior que dice 'Solicitud de amistad'.")]
    [SerializeField] private GameObject requestBoxPanel;

    [Tooltip("Texto para mostrar quién te mandó la solicitud (ej: 'Juan te envió solicitud').")]
    [SerializeField] private TMP_Text requestSenderText;

    [Tooltip("Botón Aceptar solicitud.")]
    [SerializeField] private Button acceptRequestButton;

    [Tooltip("Botón Rechazar solicitud.")]
    [SerializeField] private Button rejectRequestButton;

    [Header("=== 6. NOTIFICACIONES EN LA ESQUINA (TOASTS) ===")]
    [Tooltip("El contenedor en la esquina de la pantalla con un VerticalLayoutGroup.")]
    [SerializeField] private Transform toastContainer;

    [Tooltip("Plantilla de la card pequeña de notificación (objeto con ToastCardItem).")]
    [SerializeField] private ToastCardItem toastCardTemplate;

    // Modelos de datos
    [Serializable]
    public class UserData
    {
        public string uid;
        public string username;
        public string status; // "online" / "offline"
        public long lastSeen;
    }

    [Serializable]
    public class FriendRequest
    {
        public string fromUid;
        public string fromUsername;
        public long timestamp;
    }

    public enum NotificationType
    {
        FriendRequest,
        BattleChallenge
    }

    [Serializable]
    public class NotificationItem
    {
        public string id;
        public NotificationType type;
        public string fromUid;
        public string fromUsername;
        public long timestamp;
    }

    public enum ToastType
    {
        FriendOnline,
        FriendOffline,
        FriendRequestReceived,
        General
    }

    // Datos en memoria
    private readonly Dictionary<string, UserData> allUsers = new Dictionary<string, UserData>();
    private readonly HashSet<string> friendUids = new HashSet<string>();
    private readonly Dictionary<string, FriendRequest> incomingRequests = new Dictionary<string, FriendRequest>();
    private readonly HashSet<string> sentRequestUids = new HashSet<string>();
    private readonly Dictionary<string, string> lastKnownFriendStatus = new Dictionary<string, string>();
    private readonly List<NotificationItem> notificationQueue = new List<NotificationItem>();

    // Estado del Drawer
    private bool isDrawerOpen = false;
    private int currentTab = 0; // 0: En Línea, 1: Amigos
    private Coroutine drawerAnimRoutine;

    // Firebase
    private DatabaseReference dbRef;
    private DatabaseReference connectedRef;
    private DatabaseReference myStatusRef;
    private DatabaseReference myLastSeenRef;

    private string currentUserId = "";
    private string currentUsername = "";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (sidebarPanel == null)
        {
            sidebarPanel = GetComponent<RectTransform>();
        }

        if (listContentContainer == null)
        {
            ScrollRect scroll = GetComponentInChildren<ScrollRect>(true);
            if (scroll != null && scroll.content != null)
            {
                listContentContainer = scroll.content;
            }
        }

        if (userCardTemplate == null)
        {
            userCardTemplate = GetComponentInChildren<UserCardItem>(true);
        }

        // Ocultar plantillas si están en la escena
        if (userCardTemplate != null) userCardTemplate.gameObject.SetActive(false);
        if (toastCardTemplate != null) toastCardTemplate.gameObject.SetActive(false);
        if (requestBoxPanel != null) requestBoxPanel.SetActive(false);
    }

    private void Start()
    {
        // Configurar botones de UI
        if (toggleDrawerButton != null)
        {
            toggleDrawerButton.onClick.AddListener(ToggleDrawer);
        }

        if (tabOnlineButton != null)
        {
            tabOnlineButton.onClick.AddListener(() => SelectTab(0));
        }

        if (tabFriendsButton != null)
        {
            tabFriendsButton.onClick.AddListener(() => SelectTab(1));
        }

        // Posición inicial del drawer
        if (sidebarPanel != null)
        {
            isDrawerOpen = !startClosed;
            Vector2 target = isDrawerOpen ? openedPosition : closedPosition;
            sidebarPanel.anchoredPosition = target;
        }

        UpdateTabVisuals();
        UpdateRequestBoxUI();

        // Suscribirse a CodigoAPI
        if (CodigoAPI.Instance != null)
        {
            CodigoAPI.Instance.OnUserLoggedIn += HandleUserLoggedIn;
            CodigoAPI.Instance.OnUserLoggedOut += HandleUserLoggedOut;

            if (!string.IsNullOrEmpty(CodigoAPI.Instance.CurrentUserId))
            {
                HandleUserLoggedIn(CodigoAPI.Instance.CurrentUserId, CodigoAPI.Instance.CurrentUsername);
            }
        }
    }

    private void OnDestroy()
    {
        if (CodigoAPI.Instance != null)
        {
            CodigoAPI.Instance.OnUserLoggedIn -= HandleUserLoggedIn;
            CodigoAPI.Instance.OnUserLoggedOut -= HandleUserLoggedOut;
        }

        CleanupPresence();
    }

    #region 1. Presencia y Conexión con Firebase

    private void HandleUserLoggedIn(string uid, string username)
    {
        currentUserId = uid;
        currentUsername = !string.IsNullOrEmpty(username) ? username : "Jugador";

        if (myStatusText != null)
        {
            myStatusText.text = $"<color=#00E676>●</color> <b>{currentUsername}</b> (En linea)";
        }

        InitializeFirebaseSocial();
    }

    private void HandleUserLoggedOut()
    {
        CleanupPresence();

        currentUserId = "";
        currentUsername = "";
        allUsers.Clear();
        friendUids.Clear();
        incomingRequests.Clear();
        sentRequestUids.Clear();
        lastKnownFriendStatus.Clear();
        notificationQueue.Clear();

        if (myStatusText != null)
        {
            myStatusText.text = "Inicia sesion para ver amigos";
        }

        // Si el drawer está abierto al cerrar sesión, cerrarlo
        if (isDrawerOpen && sidebarPanel != null)
        {
            isDrawerOpen = false;
            sidebarPanel.anchoredPosition = closedPosition;
            if (toggleArrowTransform != null)
                toggleArrowTransform.localEulerAngles = Vector3.zero;
        }

        UpdateRequestBoxUI();
        RefreshCurrentTabUI();
    }

    private void InitializeFirebaseSocial()
    {
        if (string.IsNullOrEmpty(currentUserId)) return;

        try
        {
            dbRef = FirebaseDatabase.GetInstance(DatabaseUrl).RootReference;
            connectedRef = FirebaseDatabase.GetInstance(DatabaseUrl).GetReference(".info/connected");

            myStatusRef = dbRef.Child("users").Child(currentUserId).Child("status");
            myLastSeenRef = dbRef.Child("users").Child(currentUserId).Child("lastSeen");

            connectedRef.ValueChanged += HandleConnectedChanged;

            ListenAllUsers();
            ListenMyFriends();
            ListenIncomingRequests();
            ListenSentRequests();
            ListenBattleInvites();
            ListenBattleResponses();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SocialSidebarManager] Error inicializando Firebase: {ex.Message}");
        }
    }

    private void AddNotification(string id, NotificationType type, string fromUid, string fromUsername, long ts)
    {
        int existingIndex = notificationQueue.FindIndex(n => n.id == id && n.type == type);
        if (existingIndex >= 0)
        {
            notificationQueue[existingIndex].fromUsername = fromUsername;
            notificationQueue[existingIndex].timestamp = ts;
        }
        else
        {
            notificationQueue.Add(new NotificationItem
            {
                id = id,
                type = type,
                fromUid = fromUid,
                fromUsername = fromUsername,
                timestamp = ts
            });
        }
        UpdateRequestBoxUI();
    }

    private void RemoveNotification(string id, NotificationType type)
    {
        int removedCount = notificationQueue.RemoveAll(n => n.id == id && n.type == type);
        if (removedCount > 0)
        {
            UpdateRequestBoxUI();
        }
    }

    private void HandleConnectedChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null) return;

        bool isConnected = args.Snapshot.Value != null && (bool)args.Snapshot.Value;
        if (isConnected && !string.IsNullOrEmpty(currentUserId))
        {
            myStatusRef.OnDisconnect().SetValue("offline");
            myLastSeenRef.OnDisconnect().SetValue(ServerValue.Timestamp);

            myStatusRef.SetValueAsync("online");
            myLastSeenRef.SetValueAsync(ServerValue.Timestamp);
        }
    }

    private void CleanupPresence()
    {
        if (!string.IsNullOrEmpty(currentUserId) && myStatusRef != null)
        {
            myStatusRef.SetValueAsync("offline");
            myLastSeenRef.SetValueAsync(ServerValue.Timestamp);
        }

        if (connectedRef != null)
        {
            connectedRef.ValueChanged -= HandleConnectedChanged;
        }
    }

    private void OnApplicationQuit()
    {
        CleanupPresence();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (string.IsNullOrEmpty(currentUserId) || myStatusRef == null) return;

        if (pauseStatus)
        {
            myStatusRef.SetValueAsync("offline");
            myLastSeenRef.SetValueAsync(ServerValue.Timestamp);
        }
        else
        {
            myStatusRef.SetValueAsync("online");
            myLastSeenRef.SetValueAsync(ServerValue.Timestamp);
        }
    }

    #endregion

    #region 2. Listeners de Datos en Firebase

    private void ListenAllUsers()
    {
        dbRef.Child("users").ChildAdded += HandleUserAddedOrChanged;
        dbRef.Child("users").ChildChanged += HandleUserAddedOrChanged;
        dbRef.Child("users").ChildRemoved += HandleUserRemoved;
    }

    private void HandleUserAddedOrChanged(object sender, ChildChangedEventArgs args)
    {
        if (args.Snapshot == null || !args.Snapshot.Exists) return;

        string uid = args.Snapshot.Key;
        string username = args.Snapshot.Child("username").Value?.ToString() ?? "Desconocido";
        string status = args.Snapshot.Child("status").Value?.ToString() ?? "offline";
        long lastSeen = 0;
        if (long.TryParse(args.Snapshot.Child("lastSeen").Value?.ToString(), out long ls)) lastSeen = ls;

        bool wasKnownFriend = friendUids.Contains(uid);
        string oldStatus = lastKnownFriendStatus.ContainsKey(uid) ? lastKnownFriendStatus[uid] : "";

        UserData user = new UserData
        {
            uid = uid,
            username = username,
            status = status,
            lastSeen = lastSeen
        };
        allUsers[uid] = user;

        if (wasKnownFriend && uid != currentUserId && !string.IsNullOrEmpty(oldStatus) && oldStatus != status)
        {
            if (status == "online")
            {
                ShowToastNotification("¡Amigo Conectado!", $"{username} se ha conectado.", ToastType.FriendOnline);
            }
            else if (status == "offline")
            {
                ShowToastNotification("Amigo Desconectado", $"{username} se ha desconectado.", ToastType.FriendOffline);
            }
        }
        if (wasKnownFriend)
        {
            lastKnownFriendStatus[uid] = status;
        }

        RefreshCurrentTabUI();
    }

    private void HandleUserRemoved(object sender, ChildChangedEventArgs args)
    {
        if (args.Snapshot == null) return;
        string uid = args.Snapshot.Key;
        allUsers.Remove(uid);
        lastKnownFriendStatus.Remove(uid);
        RefreshCurrentTabUI();
    }

    private void ListenMyFriends()
    {
        dbRef.Child("friends").Child(currentUserId).ChildAdded += (s, e) =>
        {
            if (e.Snapshot != null && e.Snapshot.Exists)
            {
                string friendUid = e.Snapshot.Key;
                friendUids.Add(friendUid);
                if (allUsers.TryGetValue(friendUid, out UserData u))
                {
                    lastKnownFriendStatus[friendUid] = u.status;
                }
                RefreshCurrentTabUI();
            }
        };

        dbRef.Child("friends").Child(currentUserId).ChildRemoved += (s, e) =>
        {
            if (e.Snapshot != null)
            {
                string friendUid = e.Snapshot.Key;
                friendUids.Remove(friendUid);
                lastKnownFriendStatus.Remove(friendUid);
                RefreshCurrentTabUI();
            }
        };
    }

    private void ListenIncomingRequests()
    {
        dbRef.Child("friendRequests").Child(currentUserId).ChildAdded += (s, e) =>
        {
            if (e.Snapshot != null && e.Snapshot.Exists)
            {
                string fromUid = e.Snapshot.Key;
                string fromUsername = e.Snapshot.Child("fromUsername").Value?.ToString() ?? "Un jugador";
                long ts = 0;
                if (long.TryParse(e.Snapshot.Child("timestamp").Value?.ToString(), out long t)) ts = t;

                incomingRequests[fromUid] = new FriendRequest
                {
                    fromUid = fromUid,
                    fromUsername = fromUsername,
                    timestamp = ts
                };

                AddNotification(fromUid, NotificationType.FriendRequest, fromUid, fromUsername, ts);
                ShowToastNotification("¡Solicitud Recibida!", $"{fromUsername} quiere ser tu amigo.", ToastType.FriendRequestReceived);
            }
        };

        dbRef.Child("friendRequests").Child(currentUserId).ChildRemoved += (s, e) =>
        {
            if (e.Snapshot != null)
            {
                string fromUid = e.Snapshot.Key;
                incomingRequests.Remove(fromUid);
                RemoveNotification(fromUid, NotificationType.FriendRequest);
            }
        };
    }

    private void ListenBattleInvites()
    {
        dbRef.Child("battleInvites").Child(currentUserId).ChildAdded += (s, e) =>
        {
            if (e.Snapshot != null && e.Snapshot.Exists)
            {
                string fromUid = e.Snapshot.Key;
                string fromUsername = e.Snapshot.Child("fromUsername").Value?.ToString() ?? "Un amigo";
                long ts = 0;
                if (long.TryParse(e.Snapshot.Child("timestamp").Value?.ToString(), out long t)) ts = t;

                AddNotification(fromUid, NotificationType.BattleChallenge, fromUid, fromUsername, ts);
                ShowToastNotification("¡Reto de Batalla!", $"{fromUsername} te invitó a un duelo 1v1.", ToastType.General);
            }
        };

        dbRef.Child("battleInvites").Child(currentUserId).ChildRemoved += (s, e) =>
        {
            if (e.Snapshot != null)
            {
                string fromUid = e.Snapshot.Key;
                RemoveNotification(fromUid, NotificationType.BattleChallenge);
            }
        };
    }

    private void ListenBattleResponses()
    {
        dbRef.Child("battleResponses").Child(currentUserId).ValueChanged += (s, e) =>
        {
            if (e.Snapshot != null && e.Snapshot.Exists && e.Snapshot.Value != null)
            {
                bool accepted = e.Snapshot.Child("accepted").Value != null && (bool)e.Snapshot.Child("accepted").Value;
                string roomId = e.Snapshot.Child("roomId").Value?.ToString() ?? "";
                string friendName = e.Snapshot.Child("friendName").Value?.ToString() ?? "Tu amigo";

                dbRef.Child("battleResponses").Child(currentUserId).RemoveValueAsync();

                if (accepted && !string.IsNullOrEmpty(roomId))
                {
                    ShowToastNotification("¡Duelo Aceptado!", $"{friendName} aceptó tu reto. ¡Iniciando!", ToastType.General);
                    if (CodigoAPI.Instance != null)
                    {
                        CodigoAPI.Instance.ConnectToMatchRoom(roomId);
                    }
                }
                else
                {
                    ShowToastNotification("Duelo Rechazado", $"{friendName} rechazó el duelo.", ToastType.General);
                }
            }
        };
    }

    private void ListenSentRequests()
    {
        dbRef.Child("sentFriendRequests").Child(currentUserId).ChildAdded += (s, e) =>
        {
            if (e.Snapshot != null && e.Snapshot.Exists)
            {
                sentRequestUids.Add(e.Snapshot.Key);
                RefreshCurrentTabUI();
            }
        };

        dbRef.Child("sentFriendRequests").Child(currentUserId).ChildRemoved += (s, e) =>
        {
            if (e.Snapshot != null)
            {
                sentRequestUids.Remove(e.Snapshot.Key);
                RefreshCurrentTabUI();
            }
        };
    }

    #endregion

    #region 3. Operaciones de Amistad y Retos

    public void SendFriendRequest(string targetUid, string targetUsername)
    {
        if (string.IsNullOrEmpty(currentUserId) || targetUid == currentUserId) return;

        var reqData = new Dictionary<string, object>
        {
            { "fromUid", currentUserId },
            { "fromUsername", currentUsername },
            { "timestamp", ServerValue.Timestamp }
        };

        dbRef.Child("friendRequests").Child(targetUid).Child(currentUserId).SetValueAsync(reqData)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    sentRequestUids.Add(targetUid);
                    dbRef.Child("sentFriendRequests").Child(currentUserId).Child(targetUid).SetValueAsync(true);
                    ShowToastNotification("Solicitud Enviada", $"Se envio solicitud a {targetUsername}.", ToastType.General);
                    RefreshCurrentTabUI();
                }
                else
                {
                    ShowToastNotification("Error", "No se pudo enviar la solicitud.", ToastType.General);
                }
            });
    }

    public void SendBattleChallenge(string friendUid, string friendUsername)
    {
        if (string.IsNullOrEmpty(currentUserId) || friendUid == currentUserId) return;

        var inviteData = new Dictionary<string, object>
        {
            { "fromUid", currentUserId },
            { "fromUsername", currentUsername },
            { "timestamp", ServerValue.Timestamp }
        };

        dbRef.Child("battleInvites").Child(friendUid).Child(currentUserId).SetValueAsync(inviteData)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    dbRef.Child("battleInvites").Child(friendUid).Child(currentUserId).OnDisconnect().RemoveValue();
                    ShowToastNotification("Duelo Enviado", $"Invitaste a {friendUsername} a una batalla.", ToastType.General);
                }
                else
                {
                    ShowToastNotification("Error", "No se pudo enviar el reto.", ToastType.General);
                }
            });
    }

    /// <summary>
    /// Procesa de inmediato la notificación seleccionada en la cola (FIFO),
    /// descartándola de la UI al instante para mostrar la siguiente o cerrar el panel.
    /// </summary>
    private void ProcessCurrentNotification(bool accepted, NotificationItem item)
    {
        if (item == null) return;

        // 1. Descartar de la cola inmediatamente para que la UI responda sin latencia
        notificationQueue.Remove(item);
        UpdateRequestBoxUI();

        // 2. Ejecutar la acción en Firebase en segundo plano
        if (item.type == NotificationType.FriendRequest)
        {
            if (accepted)
            {
                ExecuteAcceptFriendRequest(item.fromUid, item.fromUsername);
            }
            else
            {
                ExecuteRejectFriendRequest(item.fromUid);
            }
        }
        else if (item.type == NotificationType.BattleChallenge)
        {
            if (accepted)
            {
                ExecuteAcceptBattleChallenge(item.fromUid, item.fromUsername);
            }
            else
            {
                ExecuteRejectBattleChallenge(item.fromUid);
            }
        }
    }

    private void ExecuteAcceptFriendRequest(string fromUid, string fromUsername)
    {
        if (string.IsNullOrEmpty(currentUserId) || string.IsNullOrEmpty(fromUid)) return;

        friendUids.Add(fromUid);
        incomingRequests.Remove(fromUid);
        RefreshCurrentTabUI();
        ShowToastNotification("Amigo Agregado", $"Ahora tu y {fromUsername} son amigos.", ToastType.General);

        var updates = new Dictionary<string, object>
        {
            { $"friends/{currentUserId}/{fromUid}", true },
            { $"friends/{fromUid}/{currentUserId}", true },
            { $"friendRequests/{currentUserId}/{fromUid}", null },
            { $"sentFriendRequests/{fromUid}/{currentUserId}", null }
        };

        dbRef.UpdateChildrenAsync(updates).ContinueWithOnMainThread(task =>
        {
            if (!task.IsCompletedSuccessfully)
            {
                ShowToastNotification("Error", "No se pudo sincronizar la amistad en el servidor.", ToastType.General);
            }
        });
    }

    private void ExecuteRejectFriendRequest(string fromUid)
    {
        if (string.IsNullOrEmpty(currentUserId) || string.IsNullOrEmpty(fromUid)) return;

        incomingRequests.Remove(fromUid);
        ShowToastNotification("Solicitud Rechazada", "Se rechazo la solicitud de amistad.", ToastType.General);

        var updates = new Dictionary<string, object>
        {
            { $"friendRequests/{currentUserId}/{fromUid}", null },
            { $"sentFriendRequests/{fromUid}/{currentUserId}", null }
        };

        dbRef.UpdateChildrenAsync(updates);
    }

    private void ExecuteAcceptBattleChallenge(string fromUid, string fromUsername)
    {
        if (string.IsNullOrEmpty(currentUserId) || string.IsNullOrEmpty(fromUid)) return;

        string roomId = "duel_" + Guid.NewGuid().ToString().Substring(0, 8);

        var scores = new Dictionary<string, object>
        {
            { fromUid, 0 },
            { currentUserId, 0 }
        };

        var roomData = new Dictionary<string, object>
        {
            { "roomId", roomId },
            { "player1", fromUid },
            { "player1Name", fromUsername },
            { "player2", currentUserId },
            { "player2Name", currentUsername },
            { "status", "active" },
            { "startTime", ServerValue.Timestamp },
            { "scores", scores }
        };

        var responseData = new Dictionary<string, object>
        {
            { "accepted", true },
            { "roomId", roomId },
            { "friendName", currentUsername }
        };

        var updates = new Dictionary<string, object>
        {
            { $"matchmaking/rooms/{roomId}", roomData },
            { $"battleResponses/{fromUid}", responseData },
            { $"battleInvites/{currentUserId}/{fromUid}", null }
        };

        dbRef.UpdateChildrenAsync(updates).ContinueWithOnMainThread(task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                if (CodigoAPI.Instance != null)
                {
                    CodigoAPI.Instance.ConnectToMatchRoom(roomId);
                }
            }
            else
            {
                Debug.LogError($"[SocialSidebarManager] Error al iniciar duelo: {task.Exception}");
                ShowToastNotification("Error", "No se pudo iniciar el duelo.", ToastType.General);
            }
        });
    }

    private void ExecuteRejectBattleChallenge(string fromUid)
    {
        if (string.IsNullOrEmpty(currentUserId) || string.IsNullOrEmpty(fromUid)) return;

        dbRef.Child("battleInvites").Child(currentUserId).Child(fromUid).RemoveValueAsync();

        var responseData = new Dictionary<string, object>
        {
            { "accepted", false },
            { "friendName", currentUsername }
        };
        dbRef.Child("battleResponses").Child(fromUid).SetValueAsync(responseData);

        ShowToastNotification("Duelo Rechazado", "Rechazaste la batalla.", ToastType.General);
    }

    public void AcceptFriendRequest(string fromUid, string fromUsername)
    {
        NotificationItem item = notificationQueue.Find(n => n.fromUid == fromUid && n.type == NotificationType.FriendRequest);
        if (item != null)
        {
            ProcessCurrentNotification(true, item);
        }
        else
        {
            ExecuteAcceptFriendRequest(fromUid, fromUsername);
        }
    }

    public void RejectFriendRequest(string fromUid)
    {
        NotificationItem item = notificationQueue.Find(n => n.fromUid == fromUid && n.type == NotificationType.FriendRequest);
        if (item != null)
        {
            ProcessCurrentNotification(false, item);
        }
        else
        {
            ExecuteRejectFriendRequest(fromUid);
        }
    }

    public void AcceptBattleChallenge(string fromUid, string fromUsername)
    {
        NotificationItem item = notificationQueue.Find(n => n.fromUid == fromUid && n.type == NotificationType.BattleChallenge);
        if (item != null)
        {
            ProcessCurrentNotification(true, item);
        }
        else
        {
            ExecuteAcceptBattleChallenge(fromUid, fromUsername);
        }
    }

    public void RejectBattleChallenge(string fromUid)
    {
        NotificationItem item = notificationQueue.Find(n => n.fromUid == fromUid && n.type == NotificationType.BattleChallenge);
        if (item != null)
        {
            ProcessCurrentNotification(false, item);
        }
        else
        {
            ExecuteRejectBattleChallenge(fromUid);
        }
    }

    #endregion

    #region 4. Interfaz Gráfica y Pestañas

    public void ToggleDrawer()
    {
        isDrawerOpen = !isDrawerOpen;

        if (drawerAnimRoutine != null) StopCoroutine(drawerAnimRoutine);
        Vector2 targetPos = isDrawerOpen ? openedPosition : closedPosition;
        drawerAnimRoutine = StartCoroutine(AnimateDrawer(targetPos));

        if (toggleArrowTransform != null)
        {
            // Rotar 180 grados según si está abierto o cerrado
            toggleArrowTransform.localEulerAngles = new Vector3(0, 0, isDrawerOpen ? 180f : 0f);
        }
    }

    private IEnumerator AnimateDrawer(Vector2 targetPos)
    {
        if (sidebarPanel == null) yield break;

        Vector2 startPos = sidebarPanel.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < drawerAnimDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / drawerAnimDuration);
            sidebarPanel.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }

        sidebarPanel.anchoredPosition = targetPos;
    }

#if UNITY_EDITOR
    [ContextMenu("Guardar posición actual como DESPLEGADO (Abierto)")]
    public void SaveCurrentAsOpened()
    {
        if (sidebarPanel != null)
        {
            openedPosition = sidebarPanel.anchoredPosition;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[SocialSidebarManager] Posición ABIERTO guardada: {openedPosition}");
        }
    }

    [ContextMenu("Guardar posición actual como OCULTO (Cerrado)")]
    public void SaveCurrentAsClosed()
    {
        if (sidebarPanel != null)
        {
            closedPosition = sidebarPanel.anchoredPosition;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[SocialSidebarManager] Posición CERRADO guardada: {closedPosition}");
        }
    }
#endif

    public void SelectTab(int tabIndex)
    {
        currentTab = tabIndex;
        UpdateTabVisuals();
        RefreshCurrentTabUI();
    }

    private void UpdateTabVisuals()
    {
        Color activeColor = new Color(0.18f, 0.45f, 0.9f, 1f);
        Color inactiveColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        if (tabOnlineButton != null)
        {
            Image img = tabOnlineButton.GetComponent<Image>();
            if (img != null) img.color = (currentTab == 0) ? activeColor : inactiveColor;
        }

        if (tabFriendsButton != null)
        {
            Image img = tabFriendsButton.GetComponent<Image>();
            if (img != null) img.color = (currentTab == 1) ? activeColor : inactiveColor;
        }
    }

    private void RefreshCurrentTabUI()
    {
        if (listContentContainer == null) return;

        SetupContentLayout();

        // Limpiar elementos anteriores (excepto plantillas)
        for (int i = listContentContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = listContentContainer.GetChild(i);
            if (userCardTemplate != null && child == userCardTemplate.transform) continue;
            Destroy(child.gameObject);
        }

        if (currentTab == 0)
        {
            RenderOnlineUsersTab();
        }
        else
        {
            RenderFriendsTab();
        }
    }

    private void SetupContentLayout()
    {
        if (listContentContainer == null) return;

        RectTransform contentRt = listContentContainer.GetComponent<RectTransform>();
        if (contentRt != null)
        {
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, contentRt.sizeDelta.y);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.localScale = Vector3.one;
        }

        ContentSizeFitter csf = listContentContainer.GetComponent<ContentSizeFitter>();
        if (csf != null)
        {
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        VerticalLayoutGroup vlg = listContentContainer.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
        }
    }

    private void FormatCardItemRectTransform(UserCardItem item)
    {
        if (item == null) return;

        RectTransform rt = item.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 50f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
        }

        LayoutElement le = item.GetComponent<LayoutElement>();
        if (le == null) le = item.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 50f;
        le.preferredHeight = 50f;
        le.flexibleHeight = 0f;
    }

    private void RenderOnlineUsersTab()
    {
        int count = 0;

        foreach (var kvp in allUsers)
        {
            if (kvp.Key == currentUserId) continue; // No mostrarme a mí mismo

            UserData u = kvp.Value;
            bool isOnline = (u.status == "online");
            bool isFriend = friendUids.Contains(u.uid);
            bool isPending = sentRequestUids.Contains(u.uid);

            count++;

            if (userCardTemplate != null)
            {
                UserCardItem item = Instantiate(userCardTemplate, listContentContainer);
                FormatCardItemRectTransform(item);
                item.gameObject.SetActive(true);
                item.Setup(u.uid, u.username, isOnline, isFriend, isPending, () =>
                {
                    if (isFriend)
                    {
                        SendBattleChallenge(u.uid, u.username);
                    }
                    else
                    {
                        SendFriendRequest(u.uid, u.username);
                    }
                });
            }
        }

        SetEmptyMessage(count == 0, "No hay otros usuarios registrados aún.");
    }

    private void RenderFriendsTab()
    {
        int count = 0;

        foreach (string fUid in friendUids)
        {
            count++;
            string fName = allUsers.TryGetValue(fUid, out UserData u) ? u.username : "Amigo";
            bool isOnline = (u != null && u.status == "online");

            if (userCardTemplate != null)
            {
                UserCardItem item = Instantiate(userCardTemplate, listContentContainer);
                FormatCardItemRectTransform(item);
                item.gameObject.SetActive(true);
                item.Setup(fUid, fName, isOnline, true, false, () => SendBattleChallenge(fUid, fName));
            }
        }

        SetEmptyMessage(count == 0, "Aún no tienes amigos agregados.\n¡Encuéntralos en 'EN LÍNEA'!");
    }

    private void UpdateRequestBoxUI()
    {
        if (requestBoxPanel == null) return;

        if (notificationQueue.Count == 0)
        {
            if (acceptRequestButton != null) acceptRequestButton.onClick.RemoveAllListeners();
            if (rejectRequestButton != null) rejectRequestButton.onClick.RemoveAllListeners();
            requestBoxPanel.SetActive(false);
            return;
        }

        requestBoxPanel.SetActive(true);

        NotificationItem current = notificationQueue[0];

        if (acceptRequestButton != null) acceptRequestButton.onClick.RemoveAllListeners();
        if (rejectRequestButton != null) rejectRequestButton.onClick.RemoveAllListeners();

        int pendingCount = notificationQueue.Count;
        string extraBadge = pendingCount > 1 ? $" (+{pendingCount - 1} en cola)" : "";

        if (current.type == NotificationType.FriendRequest)
        {
            if (requestSenderText != null)
            {
                requestSenderText.text = $"{current.fromUsername} te envio solicitud{extraBadge}";
            }

            if (acceptRequestButton != null)
            {
                acceptRequestButton.onClick.AddListener(() =>
                {
                    ProcessCurrentNotification(true, current);
                });
            }

            if (rejectRequestButton != null)
            {
                rejectRequestButton.onClick.AddListener(() =>
                {
                    ProcessCurrentNotification(false, current);
                });
            }
        }
        else if (current.type == NotificationType.BattleChallenge)
        {
            if (requestSenderText != null)
            {
                requestSenderText.text = $"¡{current.fromUsername} te reto a un duelo VS!{extraBadge}";
            }

            if (acceptRequestButton != null)
            {
                acceptRequestButton.onClick.AddListener(() =>
                {
                    ProcessCurrentNotification(true, current);
                });
            }

            if (rejectRequestButton != null)
            {
                rejectRequestButton.onClick.AddListener(() =>
                {
                    ProcessCurrentNotification(false, current);
                });
            }
        }
    }

    private void SetEmptyMessage(bool show, string message)
    {
        if (emptyListMessageText != null)
        {
            emptyListMessageText.gameObject.SetActive(show);
            if (show) emptyListMessageText.text = message;
        }
    }

    #endregion

    #region 5. Notificaciones Toast en la Esquina

    public void ShowToastNotification(string title, string message, ToastType type)
    {
        if (CodigoAPI.Instance != null)
        {
            CodigoAPI.Instance.SetStatus($"[{title}] {message}");
        }

        if (toastContainer == null || toastCardTemplate == null) return;

        StartCoroutine(SpawnToastRoutine(title, message, type));
    }

    private IEnumerator SpawnToastRoutine(string title, string message, ToastType type)
    {
        ToastCardItem toast = Instantiate(toastCardTemplate, toastContainer);
        toast.gameObject.SetActive(true);
        toast.transform.SetAsFirstSibling();

        Color stripeColor = type switch
        {
            ToastType.FriendOnline => new Color(0f, 0.9f, 0.45f, 1f),
            ToastType.FriendOffline => new Color(0.5f, 0.55f, 0.65f, 1f),
            ToastType.FriendRequestReceived => new Color(0f, 0.85f, 1f, 1f),
            _ => new Color(1f, 0.88f, 0.1f, 1f)
        };

        toast.Setup(title, message, stripeColor);

        CanvasGroup cg = toast.GetComponent<CanvasGroup>();
        if (cg == null) cg = toast.gameObject.AddComponent<CanvasGroup>();

        // Animación de entrada
        cg.alpha = 0f;
        Vector3 initialScale = new Vector3(0.85f, 0.85f, 1f);
        toast.transform.localScale = initialScale;

        float elapsed = 0f;
        float inDuration = 0.2f;
        while (elapsed < inDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / inDuration;
            cg.alpha = t;
            toast.transform.localScale = Vector3.Lerp(initialScale, Vector3.one, t);
            yield return null;
        }

        cg.alpha = 1f;
        toast.transform.localScale = Vector3.one;

        yield return new WaitForSecondsRealtime(3.5f);

        // Desvanecimiento
        elapsed = 0f;
        float outDuration = 0.25f;
        while (elapsed < outDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / outDuration;
            cg.alpha = 1f - t;
            yield return null;
        }

        Destroy(toast.gameObject);
    }

    #endregion
}
