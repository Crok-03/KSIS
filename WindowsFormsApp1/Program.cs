﻿using System;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ChatForm()); // Здесь запускается главная форма
    }
}
}