using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class NetworkManagerLobby : MonoBehaviour
{
    public static NetworkManagerLobby Instance;

    [Header("UI Panels")]
    [SerializeField] private GameObject initialPanel;
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject joinPanel;
    [SerializeField] private GameObject lobbyInfoPanel;

    [Header("Join Lobby UI")]
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private TMP_Text joinStatusText;
    [SerializeField] private Button connectButton;

    [Header("Lobby Info UI")]
    [SerializeField] private TMP_Text ipText;
    [SerializeField] private TMP_Text playersText;
    [SerializeField] private Button startGameButton;
    [SerializeField] private TMP_Text startGameButtonText;
    [SerializeField] private Button lobbyBackButton; // Кнопка "Назад" в лобби

    public int port = 7777;
    private TcpListener listener;
    private TcpClient client;
    private Thread listenThread, receiveThread;
    private bool isHost = false;
    private int connectedPlayers = 0;
    private bool isRunning = true;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        // Всегда начинаем с начальной панели
        initialPanel.SetActive(true);
        mainMenuPanel.SetActive(false);
        joinPanel.SetActive(false);
        lobbyInfoPanel.SetActive(false);

        // Очищаем статусы
        joinStatusText.text = "";
        startGameButtonText.text = "Начать игру";

        // Находим и подписываем кнопку "Double"
        Button doubleButton = initialPanel.GetComponentInChildren<Button>();
        if (doubleButton != null)
        {
            doubleButton.onClick.RemoveAllListeners();
            doubleButton.onClick.AddListener(OnDoubleClick);
        }

        // Подписываем другие кнопки
        startGameButton.onClick.AddListener(StartGame);
        lobbyBackButton.onClick.AddListener(ReturnToMainMenuFromLobby);
        connectButton.onClick.AddListener(JoinLobby);
    }

    public void OnDoubleClick()
    {
        initialPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    public void ShowJoinPanel()
    {
        mainMenuPanel.SetActive(false);
        joinPanel.SetActive(true);
        ipInputField.text = "";
        joinStatusText.text = "Введите IP хоста";
    }

    public void ReturnToMainMenuFromJoin()
    {
        CloseNetworkConnection();
        joinPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    public void ReturnToMainMenuFromLobby()
    {
        CloseNetworkConnection();
        lobbyInfoPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    public void ShowLobbyInfo()
    {
        mainMenuPanel.SetActive(false);
        joinPanel.SetActive(false);
        lobbyInfoPanel.SetActive(true);
        UpdateLobbyUI();
    }

    private void UpdateLobbyUI()
    {
        ipText.text = isHost ? $"IP лобби: {GetLocalIPAddress()}" : $"Подключено к: {ipInputField.text}";
        playersText.text = $"Игроков: {connectedPlayers}";
        startGameButton.interactable = isHost && connectedPlayers >= 1;
        startGameButtonText.text = isHost && connectedPlayers >= 1 ? "Начать игру" : "Ожидание игроков...";
    }

    public void CreateLobby()
    {
        try
        {
            isHost = true;
            connectedPlayers = 1;

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            isRunning = true;
            listenThread = new Thread(AcceptClients);
            listenThread.IsBackground = true;
            listenThread.Start();

            ShowLobbyInfo();
        }
        catch (Exception e)
        {
            Debug.LogError($"CreateLobby error: {e.Message}");
            playersText.text = $"Ошибка: {e.Message}";
        }
    }

    private void AcceptClients()
    {
        try
        {
            while (isRunning)
            {
                TcpClient newClient = listener.AcceptTcpClient();
                connectedPlayers++;

                if (connectedPlayers == 2)
                {
                    client = newClient;
                    StartReceiving();

                    UnityMainThreadDispatcher.Instance.Enqueue(() =>
                    {
                        UpdateLobbyUI();
                        SendNetworkMessage(new NetworkMessage { type = "HOST_READY" });
                    });
                }
            }
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted)
        {
            // Нормальное завершение
        }
        catch (Exception e)
        {
            Debug.LogError($"AcceptClients error: {e.Message}");
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
                playersText.text = $"Ошибка: {e.Message}");
        }
    }

    public void JoinLobby()
    {
        if (string.IsNullOrEmpty(ipInputField.text.Trim()))
        {
            joinStatusText.text = "<color=red>Введите IP-адрес!</color>";
            return;
        }

        isHost = false;
        joinStatusText.text = "Подключение...";

        try
        {
            client = new TcpClient();
            IAsyncResult result = client.BeginConnect(ipInputField.text, port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));

            if (!success || !client.Connected)
            {
                throw new TimeoutException("Превышено время ожидания");
            }

            client.EndConnect(result);

            isRunning = true;
            StartReceiving();
            ShowLobbyInfo();
            SendNetworkMessage(new NetworkMessage { type = "CLIENT_READY" });
        }
        catch (Exception e)
        {
            Debug.LogError($"JoinLobby error: {e.Message}");
            joinStatusText.text = $"<color=red>Ошибка подключения: {e.Message}</color>";
            client?.Close();
            client = null;
        }
    }

    private void StartReceiving()
    {
        if (receiveThread != null && receiveThread.IsAlive)
            return;

        receiveThread = new Thread(ReceiveMessages);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    private void ReceiveMessages()
    {
        NetworkStream stream = null;
        try
        {
            stream = client.GetStream();
            byte[] buffer = new byte[1024];

            while (isRunning && client != null && client.Connected)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                UnityMainThreadDispatcher.Instance.Enqueue(() => ProcessNetworkMessage(message));
            }
        }
        catch (Exception)
        {
            // Соединение закрыто
        }
        finally
        {
            stream?.Close();
        }
    }

    private void ProcessNetworkMessage(string jsonMessage)
    {
        try
        {
            NetworkMessage message = JsonUtility.FromJson<NetworkMessage>(jsonMessage);

            switch (message.type)
            {
                case "HOST_READY":
                    connectedPlayers = 2;
                    UpdateLobbyUI();
                    playersText.text = "Подключено к лобби!";
                    break;

                case "START_GAME":
                    SceneManager.LoadScene("Game");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Process error: {e.Message}");
        }
    }

    public void SendNetworkMessage(NetworkMessage message)
    {
        if (client == null || !client.Connected) return;

        try
        {
            string json = JsonUtility.ToJson(message);
            byte[] data = Encoding.UTF8.GetBytes(json);

            NetworkStream stream = client.GetStream();
            stream.Write(data, 0, data.Length);
        }
        catch (Exception e)
        {
            Debug.LogError($"Send error: {e.Message}");
            CloseNetworkConnection();
        }
    }

    public void StartGame()
    {
        if (!isHost || connectedPlayers < 1) return;

        SendNetworkMessage(new NetworkMessage { type = "START_GAME" });
        SceneManager.LoadScene("Game");
    }

    private void CloseNetworkConnection()
    {
        isRunning = false;

        try
        {
            if (isHost)
            {
                listener?.Stop();
                listenThread?.Join(100);
            }
            else
            {
                receiveThread?.Join(100);
            }

            client?.Close();
        }
        catch (Exception e)
        {
            Debug.LogError($"Close error: {e.Message}");
        }
        finally
        {
            client = null;
            listener = null;
            connectedPlayers = 0;
            isHost = false;
        }
    }

    public void CloseLobby()
    {
        CloseNetworkConnection();

        // Возвращаемся к начальному экрану
        initialPanel.SetActive(true);
        mainMenuPanel.SetActive(false);
        joinPanel.SetActive(false);
        lobbyInfoPanel.SetActive(false);
    }

    private string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip.ToString();
            }
        }
        catch (Exception)
        {
            // Не удалось получить IP
        }
        return "127.0.0.1";
    }

    void OnApplicationQuit()
    {
        CloseNetworkConnection();
    }

    void OnDestroy()
    {
        CloseNetworkConnection();
    }
}

[System.Serializable]
public class NetworkMessage
{
    public string type;
}