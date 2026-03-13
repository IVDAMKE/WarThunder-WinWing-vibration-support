using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        // Ground Calibration
        private float _fieldElevation = 0.0f;
        private bool _calibrated = false;
        private bool _gearIsDown = true; 

        // Network
        private readonly HttpClient httpClient = new HttpClient();
        private const string WT_STATE_URL = "http://127.0.0.1:8111/state";
        private const string WT_INDICATORS_URL = "http://127.0.0.1:8111/indicators";

        // Logic variables
        private int gunShells = 1000;
        private float lastAoA = 0.0F;
        private bool run = true;
        private bool waitingForWWInit = false;

        private readonly NotifyIcon trayIcon;
        private DebugWindow? debugWindow;
        private WWAPI wwAPI = new WWAPI();

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

            new Task(WarThunderPoller).Start();
            
            wwAPI.WWMessageReceived += OnWWMessage;
            wwAPI.StartListen();
        }

        ~CustomAppContext()
        {
            wwAPI.TearDown();
            run = false;
            httpClient.Dispose();
        }

        private void OnWWMessage(object? sender, WWMessageEventArgs e)
        {
            debugWindow?.AddText("FROM WW: " + e.msg);
            if (e.msg.Contains("addCommon"))
            {
                wwAPI.wwInit = true;
                waitingForWWInit = false;
            }
            else if (e.msg.Contains("clearOutput"))
            {
                wwAPI.wwInit = false;
            }
        }

        private void ShowDebugWindow(object? sender, EventArgs e)
        {
            debugWindow = new DebugWindow();
            debugWindow.Show();
        }

        private async void WarThunderPoller()
        {
            if (!wwAPI.wwInit && !waitingForWWInit)
            {
                waitingForWWInit = true;
                wwAPI.Send(WWAPI.WWMessage.START);
            }

            while (run)
            {
                try
                {
                    if (wwAPI.wwInit)
                    {
                        await ParseWarThunderData();
                    }
                    else if (!waitingForWWInit)
                    {
                         waitingForWWInit = true;
                         wwAPI.Send(WWAPI.WWMessage.START);
                    }
                }
                catch (HttpRequestException) { /* WT not running */ }
                catch (Exception ex) { debugWindow?.AddText("Error: " + ex.Message); }

                await Task.Delay(1); 
            }
        }

       private async Task ParseWarThunderData()
{
    string stateJson, indicatorsJson;
    try 
    {
        stateJson = await httpClient.GetStringAsync(WT_STATE_URL);
        indicatorsJson = await httpClient.GetStringAsync(WT_INDICATORS_URL);
    }
    catch { return; }

    JsonNode? stateNode = JsonNode.Parse(stateJson);
    JsonNode? indicatorsNode = JsonNode.Parse(indicatorsJson);
    
    if (stateNode == null || indicatorsNode == null) return;
    
    bool stateValid = (bool?)stateNode["valid"] ?? false;
    bool indValid = (bool?)indicatorsNode["valid"] ?? false;
    if (!stateValid || !indValid) return;

    WWAPI.WWTelemetryMsg wwTelemetry = new WWAPI.WWTelemetryMsg();
    Random rnd = new Random();

    // --- GEAR & RADAR ALT ---
    // float gearInd = (float?)indicatorsNode["gears"] ?? 0.5f;
    // if (gearInd == 0.0f) _gearIsDown = false;
    // else if (gearInd == 1.0f) _gearIsDown = true;
    // wwTelemetry.args.gearValue = gearInd;
    // wwTelemetry.args.isGearDown = _gearIsDown;
    // float radarAlt = GetSimulatedRadarAlt(stateNode, indicatorsNode);
    // wwTelemetry.args.isGearTouchGround = (_gearIsDown && radarAlt < 5.0f);

    // --- ENGINE & FUEL DATA ---
    wwTelemetry.args.engine1Rpm = (float?)stateNode["RPM 1"] ?? 0.0f;
    wwTelemetry.args.engine2Rpm = (float?)stateNode["RPM 2"] ?? 0.0f;
    wwTelemetry.args.engine1Temp = (float?)stateNode["oil temp 1, C"] ?? 0.0f;
    wwTelemetry.args.engine2Temp = (float?)stateNode["oil temp 2, C"] ?? 0.0f;
    wwTelemetry.args.fuelQuantity = (float?)stateNode["Mfuel, kg"] ?? 0.0f;

    // --- FLIGHT PHYSICS ---
    float mach = (float?)stateNode["M"] ?? 0.0f;
    wwTelemetry.args.machNumber = mach;
    wwTelemetry.args.gForce = (float?)stateNode["Ny"] ?? 1.0f;
    wwTelemetry.args.sideSlip = (float?)stateNode["AoS, deg"] ?? 0.0f;
    wwTelemetry.args.trueAirSpeed = (float?)stateNode["TAS, km/h"] ?? 0.0f;
    wwTelemetry.args.angleOfAttack = (float?)stateNode["AoA, deg"] ?? 0.0f;
    wwTelemetry.args.verticalVelocity = (float?)stateNode["Vy, m/s"] ?? 0.0f;
    wwTelemetry.args.speedbrakesValue = (float?)stateNode["airbrake, %"] ?? 0.0f;
    // --- VIBRATION MIXER (RAW) ---
    float jitter = (float)(rnd.NextDouble() * 2.0 - 1.0);
    float rpmRatio = (wwTelemetry.args.engine1Rpm / 15000.0f); 
    float engineVibe = (rpmRatio + ((float?)stateNode["throttle 1, %"] ?? 0.0f / 100.0f)) * jitter;
    
    // Add transonic buffet: Shake stick when near Mach 1.0
    float machBuffet = (mach > 0.95f && mach < 1.05f) ? (jitter * 0.5f) : 0.0f;

    wwTelemetry.args.accelerationX = engineVibe + machBuffet;
    wwTelemetry.args.accelerationZ = engineVibe + machBuffet;
    wwTelemetry.args.accelerationY = engineVibe + (wwTelemetry.args.gForce - 1.0f);

    // --- WEAPONS ---
    float w1 = (float?)indicatorsNode["weapon1"] ?? 0.0f;
    float w2 = (float?)indicatorsNode["weapon2"] ?? 0.0f;
    if (w1 > 0 || w2 > 0) {
        gunShells = (gunShells <= 0) ? 1000 : gunShells - 1;
        wwTelemetry.args.cannonShellsCount = gunShells;
        wwTelemetry.args.isFireCannonShells = true;
    } else { wwTelemetry.args.isFireCannonShells = false; }

    wwAPI.Send(WWAPI.WWMessage.UPDATE, wwTelemetry);
}

        private float GetSimulatedRadarAlt(JsonNode stateNode, JsonNode indicatorsNode)
        {
            float baroAlt = (float?)stateNode["H, m"] ?? 0.0f;
            float gearStatus = (float?)indicatorsNode["gears"] ?? 0.0f;
            float speed = (float?)stateNode["TAS, km/h"] ?? 0.0f;

            if (gearStatus > 0.4f && speed < 50.0f && !_calibrated) {
                _fieldElevation = baroAlt;
                _calibrated = true; 
            }
            return Math.Max(0, baroAlt - _fieldElevation);
        }

        private void Exit(object? sender, EventArgs e)
        {
            debugWindow?.Close();
            wwAPI.TearDown();
            run = false;
            trayIcon.Visible = false;
            Application.Exit();
        }
    }
}