using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class NetworkManagerTCP : MonoBehaviour
{
    public static NetworkManagerTCP Instance;
    public int port = 7777;

    private TcpListener listener;
    private TcpClient client;
    private Thread listenThread, receiveThread;
    private volatile bool clientConnectedFlag = false;

    public bool IsOffline = false;

    public bool IsHost { get; private set; } = false;

    void Awake()
    {
        if (Instance == null) Instance = this; else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        // Проверяем, поднят ли флаг подключения
        if (clientConnectedFlag)
        {
            clientConnectedFlag = false;
            // Уведомляем UI — хост готов
            OnlineMenuManager.Instance.OnNetworkReady(true);
        }
    }

    // --- Host ---
    public void StartHost()
    {
        IsHost = true;    // помечаем, что мы хост
        Debug.Log("[Net] Хост: StartHost() called, listening on loopback:" + port);
        listener = new TcpListener(IPAddress.Loopback, port); // вместо Any
        listener.Start();
        Debug.Log("[Net] Хост: Listener started!");
        listenThread = new Thread(AcceptClient) { IsBackground = true };
        listenThread.Start();
    }



    // В AcceptClient()
    void AcceptClient()
    {
        client = listener.AcceptTcpClient();
        Debug.Log("[Net] Хост: Client connected");
        // Отправляем HOST_READY
        SendMessage(new NetMessage { type = "HOST_READY" });
        clientConnectedFlag = true;
        StartReceiving();
    }

    // В ReceiveLoop()
    void ReceiveLoop()
    {
        var stream = client.GetStream();
        byte[] buf = new byte[1024];
        while (true)
        {
            int len = stream.Read(buf, 0, buf.Length);
            if (len <= 0) { Debug.Log("[Net] ReceiveLoop: Stream closed"); break; }
            var msg = Encoding.UTF8.GetString(buf, 0, len);
            Debug.Log("[Net] Получено → " + msg);
            GameManager.Instance.OnNetworkMessage(msg);
        }
    }

    public void ConnectToHost(string ip)
    {
        IsHost = false;
        client = new TcpClient();
        client.Connect(ip, port);
        Debug.Log($"[Net] Клиент: Connected to host {ip}:{port}");
        StartReceiving();
        SendMessage(new NetMessage { type = "READY" });
    }

    void StartReceiving()
    {
        receiveThread = new Thread(ReceiveLoop);
        receiveThread.Start();
    }

    // вместо SendMessage(string), делаем перегрузку:
    public void SendMessage(NetMessage msg)
    {
        string json = JsonUtility.ToJson(msg);
        var data = Encoding.UTF8.GetBytes(json);
        client.GetStream().Write(data, 0, data.Length);
        Debug.Log($"[Net] Отправлено → {json}");
    }



    void OnApplicationQuit()
    {
        listener?.Stop();
        client?.Close();
        listenThread?.Abort();
        receiveThread?.Abort();
    }

    public void Disconnect()
    {
        Debug.Log("[Net] Disconnect() called.");

        // Останавливаем слушатель, если он был запущен
        if (listener != null)
        {
            try
            {
                listener.Stop();
                Debug.Log("[Net] Listener stopped.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Net] Ошибка при остановке listener: {e.Message}");
            }
            listener = null;
        }

        // Закрываем клиентское соединение
        if (client != null)
        {
            try
            {
                client.Close();
                Debug.Log("[Net] Client closed.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Net] Ошибка при закрытии client: {e.Message}");
            }
            client = null;
        }

        // Завершаем поток, который принимает входящие подключения (для хоста)
        if (listenThread != null && listenThread.IsAlive)
        {
            try
            {
                listenThread.Abort();
                Debug.Log("[Net] listenThread aborted.");
            }
            catch { }
            listenThread = null;
        }

        // Завершаем поток получения данных
        if (receiveThread != null && receiveThread.IsAlive)
        {
            try
            {
                receiveThread.Abort();
                Debug.Log("[Net] receiveThread aborted.");
            }
            catch { }
            receiveThread = null;
        }

        // Сбрасываем флаги и флаг хоста
        clientConnectedFlag = false;
        IsHost = false;

        // Удаляем саму синглтон-ссылку, чтобы при повторном входе в сцену создавался новый экземпляр
        Instance = null;

        // Опционально: уничтожаем GameObject (если нужен полный пересоздаваемый NetworkManager)
        Destroy(gameObject);
    }

}
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    private static GameManager _instance;
    public static GameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("GameManager");
                _instance = go.AddComponent<GameManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private bool hostReadyFlag = false;
    private bool clientReadyFlag = false;
    private bool startGameFlag = false;

    Controller localCtrl, remoteCtrl;
    private bool deathInProgress = false;

    // Флаги, подошли ли Fire/Water к своим дверям
    private bool fireReachedDoor = false;
    private bool waterReachedDoor = false;

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    public void RegisterPlayers(Controller local, Controller remote)
    {
        localCtrl = local;
        remoteCtrl = remote;

        var nm = NetworkManagerTCP.Instance;
        bool isOffline = (nm == null) || nm.IsOffline;
        if (nm != null && !isOffline)
        {
            InvokeRepeating(nameof(SendInput), 0f, 0.05f);
        }
    }

    void SendInput()
    {
        if (localCtrl == null || NetworkManagerTCP.Instance.IsOffline)
            return;

        float x = localCtrl.GetMoveInputX();
        bool jump = localCtrl.ConsumeJump();
        NetMessage msg = new NetMessage
        {
            type = "INPUT",
            left = x < 0,
            right = x > 0,
            jump = jump
        };
        NetworkManagerTCP.Instance.SendMessage(msg);
    }

    public void OnNetworkMessage(string json)
    {
        var msg = JsonUtility.FromJson<NetMessage>(json);
        switch (msg.type)
        {
            case "INPUT":
                if (remoteCtrl != null)
                    remoteCtrl.ApplyRemoteInput(msg.left, msg.right, msg.jump);
                break;
            case "READY":
                clientReadyFlag = true;
                break;
            case "HOST_READY":
                hostReadyFlag = true;
                break;
            case "START":
                startGameFlag = true;
                break;
        }
    }

    void Update()
    {
        if (clientReadyFlag)
        {
            clientReadyFlag = false;
            OnlineMenuManager.Instance.OnNetworkReady(true);
        }
        if (hostReadyFlag)
        {
            hostReadyFlag = false;
            OnlineMenuManager.Instance.OnNetworkReady(true);
        }
        if (startGameFlag)
        {
            startGameFlag = false;
            if (SceneManager.GetActiveScene().name == "OnlineMenu")
                SceneManager.LoadScene("Game");
        }
    }

    public void HandleDeath(Controller deadCtrl)
    {
        if (deathInProgress) return;
        deathInProgress = true;

        Destroy(deadCtrl.gameObject);
        CancelInvoke(nameof(SendInput));
        StartCoroutine(RestartAfterDelay());
    }

    private System.Collections.IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(1f);
        string current = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(current);
        deathInProgress = false;
    }

    /// <summary>
    /// Вызывается из DoorController, когда герой подошёл к своей двери.
    /// </summary>
    public void PlayerReachedDoor(Controller.ElementType element)
    {
        Debug.Log($"[GameManager] PlayerReachedDoor: {element}");
        if (element == Controller.ElementType.Fire)
            fireReachedDoor = true;
        else if (element == Controller.ElementType.Water)
            waterReachedDoor = true;

        // Как только оба подошли – запускаем исчезновение дверей и появление лестниц
        if (fireReachedDoor && waterReachedDoor)
        {
            Debug.Log("[GameManager] Оба героя у дверей – запускаем FadeOut");
            StartCoroutine(OnBothPlayersAtDoorsCoroutine());
        }
    }

    private System.Collections.IEnumerator OnBothPlayersAtDoorsCoroutine()
    {
        // Мгновенная задержка, чтобы оба значка уже точно зажглись
        yield return null;

        // Находим все DoorController в сцене
        DoorController[] doors = FindObjectsOfType<DoorController>();
        foreach (var d in doors)
        {
            d.FadeOutDoorAndShowLadder();
        }
    }
}
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Controller : MonoBehaviour
{
    [Header("Настройки")]
    public float moveSpeed = 3f;
    public float jumpForce = 7f;
    public LayerMask groundLayer;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.1f;

    private Rigidbody2D rb;
    private bool isGrounded;
    private bool jumpPressed;
    private float moveInputX;

    [Header("Клавиши")]
    public KeyCode leftKey;
    public KeyCode rightKey;
    public KeyCode jumpKey;

    [Header("Animation")]
    public Animator bodyAnimator;
    public Animator headAnimator;
    public SpriteRenderer bodySprite;
    public SpriteRenderer headSprite;

    [Header("Сетевая метка")]
    public bool isLocal = false;

    [HideInInspector]
    public int crystalsCollected = 0;
    public enum ElementType { Fire, Water }
    public ElementType element; // назначим в инспекторе

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void AddCrystal()
    {
        crystalsCollected++;
        // Здесь можно добавить звук сборки, UI-обновление и т.п.
        Debug.Log($"{element} собрал кристалл. Всего: {crystalsCollected}");
    }
    // Inside Controller.cs
    public void SetAsLocal(KeyCode left, KeyCode right, KeyCode jump)
    {
        isLocal = true;
        leftKey = left;
        rightKey = right;
        jumpKey = jump;
    }

    void Update()
    {
        if (!isLocal) return;

        // 1) Горизонтальное состояние
        float x = 0f;
        if (Input.GetKey(leftKey)) x = -1f;
        if (Input.GetKey(rightKey)) x = 1f;
        moveInputX = x;

        // 2) Состояние прыжка
        //   Если игрок нажал кнопку jumpKey **и** он сейчас на земле — мы сообщаем «я хочу прыгнуть»
        //   Но сохраняем этот флаг до тех пор, пока удалённая сторона не сбросит его.
        if (Input.GetKey(jumpKey) && isGrounded)
        {
            // Передаём: «я всё ещё нажимаю прыжок» до момента, пока удалённая сторона
            //   не сбросит jumpPressed (например, после первого реагирования)
            jumpPressed = true;
        }
        else if (!Input.GetKey(jumpKey))
        {
            // Если нажатие отпущено — сбрасываем флаг, т.к. пользователь больше не хочет прыгать
            jumpPressed = false;
        }
    }


    void FixedUpdate()
    {
        // 1) Проверим, на земле ли мы
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // 2) Прыжок (пока isGrounded был true)
        if (jumpPressed)
        {
            // Сброс текущей вертикальной скорости, чтобы прыжок был ровным
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
            jumpPressed = false;
        }

        // 3) Горизонтальное движение
        Vector2 vel = rb.linearVelocity;
        vel.x = moveInputX * moveSpeed;
        rb.linearVelocity = vel;

        // 4) Анимация
        float absSpeed = Mathf.Abs(vel.x);
        bodyAnimator.SetFloat("Speed", absSpeed);
        headAnimator.SetFloat("Speed", absSpeed);

        // 5) Отражение спрайтов
        if (vel.x > 0.1f)
        {
            bodySprite.flipX = false;
            headSprite.flipX = false;
        }
        else if (vel.x < -0.1f)
        {
            bodySprite.flipX = true;
            headSprite.flipX = true;
        }
    }
    void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }

    // Вызывается из GameManager при получении net INPUT
    public void ApplyRemoteInput(bool left, bool right, bool jump)
    {
        if (isLocal) return;
        float x = 0;
        if (left) x = -1f;
        if (right) x = 1f;
        moveInputX = x;
        if (jump && isGrounded)
            jumpPressed = true;
    }

    public bool ConsumeJump()
    {
        bool j = jumpPressed;
        jumpPressed = false;
        return j;
    }
    public float GetMoveInputX() => moveInputX;

}
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    public void PlayOffline()
    {
        // Если объект NetworkManagerTCP уже есть на сцене — ставим IsOffline = true
        if (NetworkManagerTCP.Instance != null)
        {
            NetworkManagerTCP.Instance.IsOffline = true;
        }
        else
        {
            Debug.LogWarning("[MenuManager] NetworkManagerTCP.Instance == null, пропускаем IsOffline.");
        }

        // Загружаем сцену Game
        SceneManager.LoadScene("Game");
    }

    public void PlayOnline()
    {
        // То же самое: убедимся, что IsOffline = false
        if (NetworkManagerTCP.Instance != null)
        {
            NetworkManagerTCP.Instance.IsOffline = false;
        }
        else
        {
            Debug.LogWarning("[MenuManager] NetworkManagerTCP.Instance == null, пропускаем IsOffline = false.");
        }

        StartCoroutine(LoadOnlineWithEmpty());
    }

    private IEnumerator LoadOnlineWithEmpty()
    {
        // 1) Загружаем «пустую» сцену
        SceneManager.LoadScene("EmptyScene");

        // 2) Ждём один кадр (yield return null)
        yield return null;

        // 3) Теперь загружаем нужную OnlineMenu
        SceneManager.LoadScene("OnlineMenu");
    }
    public void ReturnToMainMenu()
    {
        if (NetworkManagerTCP.Instance != null)
        {
            NetworkManagerTCP.Instance.Disconnect();
        }

        // 2) Уничтожаем GameManager, чтобы все данные предыдущего раунда сбросились
        if (GameManager.Instance != null)
        {
            Destroy(GameManager.Instance.gameObject);
        }

        if (OnlineMenuManager.Instance != null)
        {
            Destroy(OnlineMenuManager.Instance.gameObject);
            OnlineMenuManager.Instance = null;
        }

        // 3) Переходим в главное меню
        SceneManager.LoadScene("Menu");
    }
}
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Net.Sockets;
using System.Net;

public class OnlineMenuManager : MonoBehaviour
{
    public static OnlineMenuManager Instance;

    public Toggle hostToggle;
    public Toggle clientToggle;
    public InputField ipInput;
    public Button hostButton;
    public Button connectButton;
    public Button startGameButton;
    public Text statusText;
    public Text hostIPText;

    private bool isHost;
    public bool IsHost => isHost;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            //DontDestroyOnLoad(gameObject);
        }
        else Destroy(gameObject);
    }

    void Start()
    {
        // Подключаем события переключателей
        hostToggle.onValueChanged.AddListener((val) => OnModeChanged());
        clientToggle.onValueChanged.AddListener((val) => OnModeChanged());

        // Изначально — хост
        hostToggle.isOn = true;
        OnModeChanged();

        startGameButton.interactable = false;
        statusText.text = "";
    }

    public void OnModeChanged()
    {
        // Диагностический лог
        Debug.Log($"[OnlineMenu] OnModeChanged: hostToggle.isOn = {hostToggle.isOn}");

        isHost = hostToggle.isOn;

        // Переключаем UI
        ipInput.gameObject.SetActive(!isHost);
        hostButton.gameObject.SetActive(isHost);
        connectButton.gameObject.SetActive(!isHost);

        statusText.text = "";
    }

    public void OnHostButton()
    {
        statusText.text = "Ожидание подключения…";
        NetworkManagerTCP.Instance.StartHost();

    
        string localIP = GetLocalIPAddress();
        hostIPText.text = "Ваш IP: " + localIP;
    }

    public void OnConnectButton()
{
    string ip = ipInput.text.Trim();
    if (string.IsNullOrEmpty(ip)) { statusText.text = "Введите IP"; return; }
    statusText.text = "Подключаюсь…";
    // если вы вводите 127.0.0.1, оно точно пойдёт локальному слушателю
    NetworkManagerTCP.Instance.ConnectToHost(ip);
}


    public void OnNetworkReady(bool remoteIsReady)
    {
        Debug.Log($"[OnlineMenu] OnNetworkReady(isHost={isHost}, ready={remoteIsReady})");
        if (isHost)
        {
            statusText.text = "Клиент подключён!";
            startGameButton.interactable = true;
        }
        else
        {
            statusText.text = "Вы подключены!";
            connectButton.interactable = false;
            ipInput.interactable = false;
        }
    }




    public void OnStartGameButton()
    {
        Debug.Log("[OnlineMenu] OnStartGameButton — шлём START");
        NetworkManagerTCP.Instance.SendMessage(new NetMessage { type = "START" });
        // Вместо немедленного LoadScene поставим Invoke:
        Invoke(nameof(LoadGameScene), 0.1f);
    }

    void LoadGameScene()
    {
        Debug.Log("[OnlineMenu] LoadGameScene() — загружаем Game");
        SceneManager.LoadScene("Game");
    }

    public void OnCopyIPButton()
    {
        string ip = hostIPText.text.Replace("Ваш IP: ", "").Trim();
        GUIUtility.systemCopyBuffer = ip;

        Debug.Log("[OnlineMenu] IP скопирован в буфер: " + ip);
        statusText.text = "IP скопирован!";
    }


    private string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();
        }
        return "127.0.0.1";
    }
}// Assets/Scripts/PlayerSpawner.cs
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    public GameObject firePrefab;
    public GameObject waterPrefab;

    void Start()
    {
        if (firePrefab == null || waterPrefab == null)
        {
            Debug.LogError("PlayerSpawner: префабы не назначены в инспекторе!");
            return;
        }
        // Безопасно получаем синглтон
        var nm = NetworkManagerTCP.Instance;

        // Если синглтон не создался или мы в офлайн-режиме → офлайн-спавн
        bool isOffline = (nm == null) || nm.IsOffline;
        bool amHost = (nm != null) && nm.IsHost;

        Controller local, remote;

        if (isOffline)
        {
            // Офлайн: создаём обоих локальных
            var f = Instantiate(firePrefab, new Vector3(-6, -1, 0), Quaternion.identity);
            var w = Instantiate(waterPrefab, new Vector3(-7.6f, 0.5f, 0), Quaternion.identity);

            // Настраиваем управление
            f.GetComponent<Controller>().SetAsLocal(KeyCode.A, KeyCode.D, KeyCode.W);
            w.GetComponent<Controller>().SetAsLocal(KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow);

            // Регистрируем обоих сразу
            GameManager.Instance.RegisterPlayers(
                f.GetComponent<Controller>(),
                w.GetComponent<Controller>()
            );

            return;
        }

        if (amHost)
        {
            var f = Instantiate(firePrefab, new Vector3(-6, -1, 0), Quaternion.identity);
            var w = Instantiate(waterPrefab, new Vector3(-7.6f, 0.5f, 0), Quaternion.identity);

            // Огонь — локальный у хоста
            var ctrlF = f.GetComponent<Controller>();
            ctrlF.isLocal = true;
            ctrlF.leftKey = KeyCode.A;
            ctrlF.rightKey = KeyCode.D;
            ctrlF.jumpKey = KeyCode.W;

            // Вода — удалённый
            var ctrlW = w.GetComponent<Controller>();
            ctrlW.isLocal = false;

            local = ctrlF;
            remote = ctrlW;
        }
        else
        {
            var f = Instantiate(firePrefab, new Vector3(-6, -1, 0), Quaternion.identity);
            var w = Instantiate(waterPrefab, new Vector3(-7.6f, 0.5f, 0), Quaternion.identity);

            // Огонь — удалённый
            var ctrlF = f.GetComponent<Controller>();
            ctrlF.isLocal = false;

            // Вода — локальная у клиента
            var ctrlW = w.GetComponent<Controller>();
            ctrlW.isLocal = true;
            ctrlW.leftKey = KeyCode.LeftArrow;
            ctrlW.rightKey = KeyCode.RightArrow;
            ctrlW.jumpKey = KeyCode.UpArrow;

            local = ctrlW;
            remote = ctrlF;
        }

        GameManager.Instance.RegisterPlayers(local, remote);
    }

}
using UnityEngine;

public class Crystal : MonoBehaviour
{
    public Controller.ElementType requiredElement;

    void OnTriggerEnter2D(Collider2D other)
    {
        // Ищем Controller у родителя/предка
        Controller ctrl = other.GetComponentInParent<Controller>();
        if (ctrl == null) return;

        // Собираем только свой элемент
        if (ctrl.element == requiredElement)
        {
            ctrl.AddCrystal();
            Destroy(gameObject);
        }
    }
}
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class DeathZone : MonoBehaviour
{
    public enum ZoneType { Lava, Water, Acid }
    public ZoneType zoneType;

    void Awake()
    {
        // Убедимся, что коллайдер настроен как триггер
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // Пытаемся найти Controller в родителях (если коллайдер у дочернего объекта)
        Controller ctrl = other.GetComponentInParent<Controller>();
        if (ctrl == null) return;

        bool shouldKill = false;

        switch (zoneType)
        {
            case ZoneType.Lava:
                // Лава убивает Воду
                if (ctrl.element == Controller.ElementType.Water)
                    shouldKill = true;
                break;
            case ZoneType.Water:
                // Вода убивает Огонь
                if (ctrl.element == Controller.ElementType.Fire)
                    shouldKill = true;
                break;
            case ZoneType.Acid:
                // Кислота убивает обоих
                shouldKill = true;
                break;
        }

        if (shouldKill)
        {
            GameManager.Instance.HandleDeath(ctrl);
        }
    }
}
using UnityEngine;
using UnityEngine.UI;

public class DebugConsole : MonoBehaviour
{
    public Text debugText;

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }
    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        debugText.text += logString + "\n";
    }

    public void OnCopyLogButton()
    {
        string ip = debugText.text;
        GUIUtility.systemCopyBuffer = ip;
    }
}
using System.Collections;
using UnityEngine;

public class DoorController : MonoBehaviour
{
    // Теперь используем сразу ElementType из Controller
    public Controller.ElementType element;

    [Header("Дочерние объекты (привяжите в инспекторе)")]
    public GameObject door;    // спрайт двери (активен изначально)
    public GameObject icon;    // значок двери (Initially inactive)
    public GameObject ladder;  // лестница (Initially inactive)

    bool iconActive = false;
    bool playerAtDoor = false;

    void Start()
    {
        if (door != null) door.SetActive(true);
        if (icon != null) icon.SetActive(false);
        if (ladder != null) ladder.SetActive(false);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // Логируем, какой объект зашёл в зону:
        Debug.Log($"[DoorController:{element}] OnTriggerEnter2D: {other.name}");

        Controller ctrl = other.GetComponentInParent<Controller>();
        if (ctrl == null)
        {
            Debug.Log($"[DoorController:{element}] Вошёл объект без Controller, пропускаем");
            return;
        }

        Debug.Log($"[DoorController:{element}] Обнаружен Controller с элементом {ctrl.element} и кристаллов собрано {ctrl.crystalsCollected}");

        // Если это «наш» герой и он собрал ≥ 3 кристалла
        if (ctrl.element == element && ctrl.crystalsCollected >= 3)
        {
            if (!iconActive)
            {
                iconActive = true;
                if (icon != null)
                {
                    icon.SetActive(true);
                    Debug.Log($"[DoorController:{element}] Значок двери зажжён");
                }
            }

            if (!playerAtDoor)
            {
                playerAtDoor = true;
                Debug.Log($"[DoorController:{element}] Герой подошёл к двери, сообщаем GameManager");
                GameManager.Instance.PlayerReachedDoor(element);
            }
        }
    }

    // Вызывается, когда оба героя подошли
    public void FadeOutDoorAndShowLadder()
    {
        StartCoroutine(FadeCoroutine());
    }

    IEnumerator FadeCoroutine()
    {
        // Ждём 1 секунду
        yield return new WaitForSeconds(1f);

        // Получаем Renderer’ы
        SpriteRenderer doorSR = (door != null) ? door.GetComponent<SpriteRenderer>() : null;
        SpriteRenderer iconSR = (icon != null) ? icon.GetComponent<SpriteRenderer>() : null;
        float duration = 1f;
        float elapsed = 0f;

        Color doorColor = (doorSR != null) ? doorSR.color : Color.white;
        Color iconColor = (iconSR != null) ? iconSR.color : Color.white;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float alpha = Mathf.Lerp(1f, 0f, t);

            if (doorSR != null)
                doorSR.color = new Color(doorColor.r, doorColor.g, doorColor.b, alpha);
            if (iconSR != null)
                iconSR.color = new Color(iconColor.r, iconColor.g, iconColor.b, alpha);

            yield return null;
        }

        if (door != null) door.SetActive(false);
        if (icon != null) icon.SetActive(false);
        if (ladder != null) ladder.SetActive(true);

        Debug.Log($"[DoorController:{element}] Дверь и значок исчезли, показана лестница");
    }
}
// Assets/Scripts/NetMessage.cs
using System;

[Serializable]
public class NetMessage
{
    public string type;    // "READY", "HOST_READY", "START" или "INPUT"

    // только для INPUT:
    public bool left;
    public bool right;
    public bool jump;
}
