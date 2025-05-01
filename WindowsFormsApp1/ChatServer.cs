using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class ChatServer
{
    private TcpListener server;
    private List<TcpClient> clients = new List<TcpClient>();
    private Dictionary<TcpClient, string> clientNames = new Dictionary<TcpClient, string>();
    private int clientCounter = 0; // Счетчик клиентов
    public event Action<string> MessageReceived;

    public void StartServer(string ip, int port)
    {
        server = new TcpListener(IPAddress.Parse(ip), port);
        server.Start();
        new Thread(AcceptClients).Start();
    }

    public void StopServer()
    {
        server?.Stop();
        foreach (var client in clients)
        {
            client.Close();
        }
        clients.Clear();
        clientNames.Clear();
    }

    public void SendMessage(string message)
    {
        BroadcastMessage($"Сервер: {message}");
    }

    private void AcceptClients()
    {
        while (true)
        {
            TcpClient client = server.AcceptTcpClient();
            lock (clients) clients.Add(client);

            // Присваиваем клиенту номер
            clientCounter++;
            string clientName = $"Клиент{clientCounter-1}";
            lock (clientNames) clientNames[client] = clientName;

            // Отправляем клиенту его имя
            NetworkStream stream = client.GetStream();
            byte[] nameData = Encoding.UTF8.GetBytes(clientName);
            stream.Write(nameData, 0, nameData.Length);

            new Thread(() => HandleClient(client)).Start();
        }
    }

    private void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];

        while (true)
        {
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;

            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // Получаем имя клиента
            string clientName;
            lock (clientNames)
            {
                clientName = clientNames.ContainsKey(client) ? clientNames[client] : "Неизвестный клиент";
            }

            string fullMessage = $"{clientName}: {message}";

            // Отправляем сообщение всем клиентам
            MessageReceived?.Invoke(fullMessage);
            BroadcastMessage(fullMessage);
        }

        lock (clients) clients.Remove(client);
        lock (clientNames) clientNames.Remove(client);
        client.Close();
    }

    private void BroadcastMessage(string message)
    {
        lock (clients)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            foreach (var client in clients)
            {
                try
                {
                    client.GetStream().Write(data, 0, data.Length);
                }
                catch
                {
                    // Игнорируем ошибки (например, если клиент отключился)
                }
            }
        }
    }
}
