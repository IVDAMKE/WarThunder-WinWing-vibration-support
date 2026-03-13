using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IL2WinWing
{
    public class WWMessageEventArgs : EventArgs
    {
        public string msg { get; set; } = string.Empty;
    }

    internal class WWAPI
    {
        private UdpClient wwClient = new UdpClient();
        private IPEndPoint wwEP = new IPEndPoint(IPAddress.Parse("127.0.0.1"), Properties.Settings.Default.WWPort);
        private bool listen = false;

        private const string NET_READY = "{\"func\": \"net\", \"msg\": \"ready\"}";
        private const string MSN_READY = "{\"func\": \"mission\", \"msg\": \"ready\"}";
        private const string MSN_START = "{\"func\": \"mission\", \"msg\": \"start\"}";
        private const string MOD       = "{\"func\": \"mod\", \"msg\": \"FA-18C_hornet\"}"; // Updated for modern jet feedback
        private const string MSN_STOP  = "{\"func\": \"mission\", \"msg\": \"stop\"}";

        public bool wwInit { get; set; } = false;

        public class Args
        {
            public float angleOfAttack { get; set; }
            public float rateOfAngleOfAttack { get; set; }
            public float trueAirSpeed { get; set; }
            public float gearValue { get; set; }
            public bool isGearDown { get; set; }
            public bool isGearTouchGround { get; set; }
            public int cannonShellsCount { get; set; } = 1000;
            public bool isFireCannonShells { get; set; } = false;
            public float speedbrakesValue { get; set; }
            public float verticalVelocity { get; set; }
            public float accelerationX { get; set; }
            public float accelerationY { get; set; }
            public float accelerationZ { get; set; }
            
            // --- NEW FUNCTIONING ARGS ADDED ---
            public float machNumber { get; set; }
            public float fuelQuantity { get; set; }
            public float engine1Rpm { get; set; }
            public float engine2Rpm { get; set; }
            public float engine1Temp { get; set; }
            public float engine2Temp { get; set; }
            public float gForce { get; set; }
            public float sideSlip { get; set; }
            
            public bool hasPayload { get; set; } = true;
            public bool hasNoPayload { get; set; } = false;
            public List<object> payloadStations { get; set; } = new List<object>();
        }

        public class WWTelemetryMsg
        {
            public string func { get; } = "addCommon";
            public Args args { get; set; } = new Args();

            public override string ToString()
            {
                return JsonSerializer.Serialize(this);
            }
        }

        public WWAPI()
        {
            wwClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            wwClient.Client.Connect(wwEP);
        }

        ~WWAPI()
        {
            wwClient.Close();
        }
        
        public void StartListen()
        {
            listen = true;
            new Task(Receiver).Start();
        }

        public void TearDown()
        {
            Send(WWMessage.STOP);
            listen = false;
            wwInit = false;
            wwClient.Close();
        }

        public enum WWMessage
        {
            START,
            UPDATE,
            STOP,
        }

        public bool Send(WWMessage msg, WWTelemetryMsg? telemetry = null)
        {
            if (msg == WWMessage.START)
            {
                string[] initMsgs = { MSN_STOP, NET_READY, MSN_READY, MSN_START, MOD };
                foreach (var m in initMsgs)
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(m);
                    try { wwClient.Send(bytes, bytes.Length, wwEP); }
                    catch { return false; }
                }
            }
            else if (msg == WWMessage.UPDATE && telemetry != null && wwInit)
            {
                string json = JsonSerializer.Serialize(telemetry);
                byte[] bytes = Encoding.ASCII.GetBytes(json);
                try { wwClient.SendAsync(bytes, bytes.Length, wwEP); }
                catch { return false; }
            }
            else if (msg == WWMessage.STOP && wwInit)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(MSN_STOP);
                try { wwClient.SendAsync(bytes, bytes.Length, wwEP); wwInit = false; }
                catch { return false; }
            }
            return true;
        }

        private void Receiver()
        {
            while (listen)
            {
                try
                {
                    byte[] data = wwClient.Receive(ref wwEP);
                    if (data.Length > 0)
                    {
                        WWMessageEventArgs msg = new WWMessageEventArgs { msg = Encoding.ASCII.GetString(data) };
                        WWMessageReceived?.Invoke(this, msg);
                    }
                }
                catch { listen = false; }
                Thread.Sleep(25);
            }
        }

        public event EventHandler<WWMessageEventArgs>? WWMessageReceived;
    }
}