using System;
using System.Windows.Forms;

public partial class ChatForm : Form
{
    private TextBox txtIP, txtPort, txtMessage;
    private Button btnStartServer, btnConnect, btnSend;
    private ListBox lstMessages;
    private ChatServer server;
    private ChatClient client;

    public ChatForm()
    {
        this.Text = "Чат";
        this.Size = new System.Drawing.Size(400, 400);

        txtIP = new TextBox { Left = 10, Top = 10, Width = 150, Text = "127.0.0.1" };
        txtPort = new TextBox { Left = 170, Top = 10, Width = 50, Text = "5000" };
        btnStartServer = new Button { Left = 230, Top = 10, Width = 150, Text = "Запустить сервер" };
        btnConnect = new Button { Left = 230, Top = 40, Width = 150, Text = "Подключиться" };
        lstMessages = new ListBox { Left = 10, Top = 70, Width = 370, Height = 200 };
        txtMessage = new TextBox { Left = 10, Top = 280, Width = 270 };
        btnSend = new Button { Left = 290, Top = 280, Width = 90, Text = "Отправить" };

        btnStartServer.Click += (s, e) => StartServer();
        btnConnect.Click += (s, e) => ConnectToServer();
        btnSend.Click += (s, e) => SendMessage();

        this.Controls.Add(txtIP);
        this.Controls.Add(txtPort);
        this.Controls.Add(btnStartServer);
        this.Controls.Add(btnConnect);
        this.Controls.Add(lstMessages);
        this.Controls.Add(txtMessage);
        this.Controls.Add(btnSend);
        // Обработчик закрытия формы
        this.FormClosing += (s, e) => OnFormClosing();
    }

    private void OnFormClosing()
    {
        if (server != null)
        {
            // Останавливаем сервер
            server.StopServer();
        }

        if (client != null)
        {
            // Закрываем клиентское соединение
            client.Disconnect();
        }
    }
    private void StartServer()
    {
        server = new ChatServer();
        server.MessageReceived += (msg) => Invoke(new Action(() => OnMessageReceived(msg)));
        server.StartServer(txtIP.Text, int.Parse(txtPort.Text));
    }

    private void ConnectToServer()
    {
        client = new ChatClient();
        client.MessageReceived += (msg) => Invoke(new Action(() => OnMessageReceived(msg)));
        client.Connect(txtIP.Text, int.Parse(txtPort.Text));
    }

    private void SendMessage()
    {
        string message = txtMessage.Text;

        if (client != null)
        {
            // Клиент отправляет сообщение
            client.SendMessage(message);
            // Не добавляем сообщение в список сразу, чтобы оно не отображалось дважды

        }
        else if (server != null)
        {
            // Сервер отправляет сообщение
            server.SendMessage(message);
            lstMessages.Items.Add("Вы (Сервер): " + message); // Отображаем в списке сообщений как "Вы (Сервер)"
        }

        txtMessage.Clear();
    }


    private void OnMessageReceived(string message)
    {
        // Просто добавляем полученное сообщение в список
        lstMessages.Items.Add(message);
    }
}
