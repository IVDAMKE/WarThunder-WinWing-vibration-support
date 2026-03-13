using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IL2WinWing
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ApplicationConfiguration.Initialize();
            Application.Run(new CustomAppContext());
        }
    }

    public class CustomAppContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private DebugWindow? debugWindow;
        private WWAPI wwAPI = new WWAPI();
        private WarThunderProtocol wtProtocol;

        public CustomAppContext()
        {
            trayIcon = new NotifyIcon();
            trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            trayIcon.Text = "WarThunderWinWing";
            trayIcon.ContextMenuStrip = new ContextMenuStrip();
            trayIcon.ContextMenuStrip.Items.Add("Debug", null, ShowDebugWindow);
            trayIcon.ContextMenuStrip.Items.Add("-");
            trayIcon.ContextMenuStrip.Items.Add("Exit", null, Exit);
            trayIcon.Visible = true;

            // Initialize the separated War Thunder Protocol
            wtProtocol = new WarThunderProtocol(wwAPI, LogToDebugWindow);
            wtProtocol.Start();
            
            wwAPI.WWMessageReceived += OnWWMessage;
            wwAPI.StartListen();
        }

        ~CustomAppContext()
        {
            wtProtocol.Stop();
            wwAPI.TearDown();
        }

        private void OnWWMessage(object? sender, WWMessageEventArgs e)
        {
            LogToDebugWindow("FROM WW: " + e.msg);
            if (e.msg.Contains("addCommon"))
            {
                wwAPI.wwInit = true;
                wtProtocol.SetWaitingForWWInit(false);
            }
            else if (e.msg.Contains("clearOutput"))
            {
                wwAPI.wwInit = false;
            }
        }

        private void LogToDebugWindow(string message)
        {
            debugWindow?.AddText(message);
        }

        private void ShowDebugWindow(object? sender, EventArgs e)
        {
            if (debugWindow == null || debugWindow.IsDisposed)
            {
                debugWindow = new DebugWindow();
            }
            debugWindow.Show();
        }

        private void Exit(object? sender, EventArgs e)
        {
            debugWindow?.Close();
            wtProtocol.Stop();
            wwAPI.TearDown();
            trayIcon.Visible = false;
            Application.Exit();
        }
    }
}