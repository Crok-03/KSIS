using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public class ChatClient
{
    private TcpClient client;
    private NetworkStream stream;
    private string clientName;
    public event Action<string> MessageReceived;

    public void Connect(string ip, int port)
    {
        try
        {
            client = new TcpClient(ip, port);
            stream = client.GetStream();

            // Получаем имя клиента от сервера
            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            clientName = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // Показываем сообщение о подключении
            MessageBox.Show("Вы успешно подключились к серверу!", "Подключение", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Запускаем поток для получения сообщений
            new Thread(ReceiveMessages).Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка подключения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void SendMessage(string message)
    {
        if (client == null || !client.Connected) return;

        byte[] data = Encoding.UTF8.GetBytes(message);
        stream.Write(data, 0, data.Length);
    }

    private void ReceiveMessages()
    {
        try
        {
            byte[] buffer = new byte[1024];

            while (true)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                MessageReceived?.Invoke(message);
            }
        }
        catch
        {
            // Игнорируем ошибки
        }
    }

    public void Disconnect()
    {
        client?.Close();
    }
}
