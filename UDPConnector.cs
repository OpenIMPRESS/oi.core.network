using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Threading;
using System.Text;
using System.Linq;

#if !UNITY_EDITOR && UNITY_METRO
using System.Threading.Tasks;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Networking;
#else
using System.Net;
using System.Net.Sockets;
#endif

namespace oi.core.network {

    [Serializable]
    public class RegisterObject {
        public string packageType = "register";
        public string socketID;
        public bool isSender;
        public string localIP;
        public string UID;
    }

    [Serializable]
    public class AnswerObject {
        public string type;
        public string address;
        public int port;
    }

    public class UDPConnector : MonoBehaviour {
        public delegate void _DataIn(byte[] data);
        public event _DataIn OnDataIn;

        public delegate void _DataOut(byte[] data);
        public event _DataOut OnDataOut;

        // Public settings, applied in Start()
        public int debugLevel;
        public string SocketID;
        public bool UseMatchmakingServer = true;
        public string ManualHostName = "";
        public int ManualPort;
        public bool IsSender;
        // =======================================

        // Socket Description
        private string _socketID;
        private bool _isSender;

        // Socket Connection (From MM or manually set)
        public string _remoteAddress { get; private set; }
        public int _remotePort { get; private set; }
        private bool _useMatchmakingServer;

        // MM Server
        private string _serverHostname;
        private int _serverPort;

        private string UID;
        private string localIP = "";

        private bool _sendRunning = false;
        ManualResetEvent send_MRSTE = new ManualResetEvent(false);
        private bool _listenRunning = false;
        private Queue<byte[]> _sendQueue = new Queue<byte[]>();
        private Queue<byte[]> _receiveQueue = new Queue<byte[]>();
        Dictionary<UInt32, byte[][]> _dataParts = new Dictionary<UInt32, byte[][]>();

        private int headerLen = 13;
        private int cutoffLength;

        private float registerInterval = 2F;
        private float lastRegister = -1.5F;

        private float connectionTimeout = 5F;
        private float lastReceivedHB = 0F;

        private float HBInterval = 2F;
        private float lastSentHB = -1.5F;

        private float currentTime = 0;

        // Remote Client
        public bool connected;


        // TODO: THIS IS FOR DEBUGGING ONLY
        public string guidSuffix = "";

        // TODO: store and calculate outgoing data
        public float GetTrafficBytesOut() {
            return 1.0f; // return in Bytes/second
        }

        // TODO: store and calculate incomming data
        public float GetTrafficBytesIn() {
            return 1.0f; // return in Bytes/second
        }

        // TODO: store and calculate outgoing messages
        public float GetTrafficMessagesOut() {
            return 1.0f; // return in Messages/second
        }

        // TODO: store and calculate incomming messages
        public float GetTrafficMessagesIn() {
            return 1.0f; // return in Messages/second
        }

#if !UNITY_EDITOR && UNITY_METRO
        private DatagramSocket udpClient;
        private Task _sendTask;
        private Task _listenTask;
#else
        private UdpClient udpClient;
	    private Thread _sendThread;
	    private Thread _listenThread;
#endif

        private SessionManager sm;
        // Use this for initialization
#if !UNITY_EDITOR && UNITY_METRO
        async void Start() {
#else
        
        void Start() {
#endif
            sm = FindObjectOfType<SessionManager>();
            if (sm == null) {
                Debug.LogError("Please add and configure a SessionManager component to the scene.");
                return;
            }

            _serverHostname = sm.GetMMHostName();
            _serverPort = sm.GetMMPort();
            connected = false;

            _useMatchmakingServer = UseMatchmakingServer;
            if (!_useMatchmakingServer) {
                _remoteAddress = ManualHostName;
                _remotePort = ManualPort;
            }
            _isSender = IsSender;
            _socketID = SocketID;

            cutoffLength = 60000 - headerLen;
            localIP = GetLocalIPAddress();
            UID = sm.GetGUID();
            if (debugLevel > 0) {
                UID = UID+guidSuffix;
            }

#if !UNITY_EDITOR && UNITY_METRO
            _listenTask = Task.Run(() => DataListener());
            await Task.Delay(1000);
            _sendTask = Task.Run(() => DataSender());
#else
		_sendThread = new Thread(DataSender);
		_sendThread.Start();
        _listenThread = new Thread(DataListener);
		_listenThread.Start();
#endif
        }

        // Update is called once per frame
        void Update() {
            currentTime = Time.time;
            if (_useMatchmakingServer) {
                if (connected && Time.time > lastReceivedHB + connectionTimeout) {
                    connected = false;
                }

                if (connected) {
                    if (Time.time > lastSentHB + HBInterval) {
                        lastSentHB = Time.time;
                        Punch();
                    }
                }

                if (!connected) {
                    if (Time.time > lastRegister + registerInterval) {
                        lastRegister = Time.time;
                        Register();
                    }
                }
            } else if (!connected) {
                connected = true;
            }
        }

        UInt32 packageSequenceID = 0;
        public void SendData(byte[] nextPacket) {
            if (nextPacket.Length != 0 && OnDataOut != null)
                OnDataOut.Invoke(nextPacket);

            if (connected) {
                if (nextPacket.Length != 0) {
                    packageSequenceID++;
                    UInt32 partsAm = (UInt32)((nextPacket.Length + cutoffLength - 1) / cutoffLength); // Round Up The Result Of Integer Division
                    UInt32 currentPart = 0;

                    while (nextPacket.Length > 0) {
                        byte[] cutData = new byte[0];
                        if (nextPacket.Length > cutoffLength) {
                            cutData = new byte[cutoffLength];
                            Array.Copy(nextPacket, cutData, cutoffLength);

                            int remainingLen = nextPacket.Length - cutoffLength;
                            byte[] remainder = new byte[remainingLen];
                            Array.Copy(nextPacket, cutoffLength, remainder, 0, remainingLen);
                            nextPacket = remainder;
                        } else {
                            cutData = nextPacket;
                            nextPacket = new byte[0];
                        }

                        byte[] sendBytes;
                        using (MemoryStream fs = new MemoryStream())
                        using (BinaryWriter writer = new BinaryWriter(fs)) {
                            writer.Write((byte)20);
                            writer.Write(packageSequenceID);
                            writer.Write(partsAm);
                            writer.Write(currentPart++);
                            writer.Write(cutData);
                            sendBytes = fs.ToArray();
                        }
                        _BufferSendData(sendBytes);
                    }
                }
            }
        }

        public byte[] GetNewData() {
            byte[] returnBytes = null;
            lock (_receiveQueue) {
                if (_receiveQueue.Count > 0) {
                    returnBytes = _receiveQueue.Dequeue();
                }
            }
            return returnBytes;
        }

        public void Close() {
            _sendRunning = false;
            _listenRunning = false;
#if UNITY_EDITOR
            udpClient.Close();
#endif
        }

        private void OnApplicationQuit() {
            Close();
        }

        public static string GetLocalIPAddress() {
            string localIP = "";
#if !UNITY_EDITOR && UNITY_METRO
        foreach (HostName localHostName in NetworkInformation.GetHostNames()) {
            if (localHostName.IPInformation != null) {
                if (localHostName.Type == HostNameType.Ipv4) {
                    localIP = localHostName.ToString();
                    break;
                }
            }
        }
#else
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0)) {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                localIP = endPoint.Address.ToString();
            }
#endif
            return localIP;
        }

        //------------- LISTEN STUFF -----------------


#if !UNITY_EDITOR && UNITY_METRO
        private async Task DataListener() {
            udpClient = new DatagramSocket();
            udpClient.MessageReceived += Listener_MessageReceived;
            try {
                await udpClient.BindEndpointAsync(null, "0");
                if(debugLevel > 0) Debug.Log("Listening on port: " + udpClient.Information.LocalPort);
            } catch (Exception e) {
                if(debugLevel > 0) Debug.Log("DATA LISTENER START EXCEPTION: " + e.ToString());
                if(debugLevel > 0) Debug.Log(SocketError.GetStatus(e.HResult).ToString());
                return;
            }

#else
        private void DataListener() {
            IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
            udpClient = new UdpClient(anyIP);
            int listenPort = ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
            if (debugLevel > 0) Debug.Log("Client listening on " + listenPort);

            _listenRunning = true;
            while (_listenRunning) {
                //UdpReceiveResult receivedResults = await udpClient.ReceiveAsync();
                //byte[] receivedPackage = receivedResults.Buffer;
                try {
                    byte[] receivedPackage = udpClient.Receive(ref anyIP);
                    HandleReceivedData(receivedPackage);
                } catch (Exception e) {
                    if (_listenRunning) Debug.LogWarning("Exception in UDPConnector.DataListener: "+e);
                }
            }
            udpClient.Close();
            if (debugLevel > 0) Debug.Log("DataListener Stopped");
#endif
        }

#if !UNITY_EDITOR && UNITY_METRO
    private async void Listener_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args) {
        try {
            Stream streamIn = args.GetDataStream().AsStreamForRead();
            MemoryStream ms = ToMemoryStream(streamIn);
            byte[] receivedPackage = ms.ToArray();
            HandleReceivedData(receivedPackage);
        } catch (Exception e) {
            if(debugLevel > 0) Debug.Log("DATA LISTENER EXCEPTION: " + e.ToString());
            if(debugLevel > 0) Debug.Log(SocketError.GetStatus(e.HResult).ToString());
            return;
        }
    }
    static MemoryStream ToMemoryStream(Stream input) {
        try {                                         // Read and write in
            byte[] block = new byte[0x1000];       // blocks of 4K.
            MemoryStream ms = new MemoryStream();
            while (true) {
                int bytesRead = input.Read(block, 0, block.Length);
                if (bytesRead == 0) return ms;
                ms.Write(block, 0, bytesRead);
            }
        } finally { }
    }
#endif



        private void HandleReceivedData(byte[] inData) {
            if (debugLevel > 1) {
                string dString = Encoding.ASCII.GetString(inData);
                Debug.Log(dString.Length + "  " + (byte)inData[0] + "  " + dString);
            }

            byte magicByte = inData[0];
            if (magicByte == 100) {
                string json = Encoding.ASCII.GetString(inData, 1, inData.Length - 1);
                try {
                    AnswerObject obj = JsonUtility.FromJson<AnswerObject>(json);
                    if (obj.type == "answer") {
                        _remoteAddress = obj.address;
                        _remotePort = obj.port;
                        Punch();
                        Punch();
                    }
                    if (obj.type == "punch") {  // received punch packet from other client -> connection works
                        lastReceivedHB = currentTime;
                        connected = true;
                    }
                    return; // return if package was a json package
                } catch (Exception e) { Debug.Log(e.ToString()); }
            } else if (magicByte == 20) {
                lastReceivedHB = currentTime;
                byte packageType;
                UInt32 packageSequenceID;
                UInt32 partsAm;
                UInt32 currentPart;

                using (MemoryStream str = new MemoryStream(inData)) {
                    using (BinaryReader reader = new BinaryReader(str)) {
                        packageType = reader.ReadByte();
                        packageSequenceID = reader.ReadUInt32();
                        partsAm = reader.ReadUInt32();
                        currentPart = reader.ReadUInt32();
                    }
                }
                byte[] data = new byte[inData.Length - headerLen];
                Array.Copy(inData, headerLen, data, 0, inData.Length - headerLen);

                if (debugLevel > 1) Debug.Log("packageSequenceID:  " + packageSequenceID + ", partsAm: " + partsAm + ", currentPart: " + currentPart + ", size: " + inData.Length);
                if (partsAm == 1) {
                    lock (_receiveQueue)
                        _receiveQueue.Enqueue(data);
                    if (OnDataIn != null) OnDataIn.Invoke(data);
                } else if (partsAm > 1) {
                    if (!_dataParts.ContainsKey(packageSequenceID)) {
                        byte[][] parts = new byte[partsAm][];
                        parts[currentPart] = data;
                        _dataParts.Add(packageSequenceID, parts);
                    } else {
                        byte[][] parts = _dataParts[packageSequenceID];
                        parts[currentPart] = data;

                        bool dataComplete = true;
                        int concatDataSize = 0;
                        for (int i = 0; i < partsAm; i++) {
                            if (parts[i] == null) {
                                dataComplete = false;
                                break;
                            }
                            concatDataSize += parts[i].Length;
                        }
                        if (dataComplete) {
                            _dataParts.Remove(packageSequenceID);
                            byte[] concatData = new byte[concatDataSize];
                            int idx = 0;
                            for (int i = 0; i < partsAm; i++) {
                                Array.Copy(parts[i], 0, concatData, idx, parts[i].Length);
                                idx += parts[i].Length;
                            }

                            lock (_receiveQueue)
                                _receiveQueue.Enqueue(concatData);
                            if (OnDataIn != null) OnDataIn.Invoke(concatData);
                        }
                    }
                }
            } else {
                lastReceivedHB = currentTime;
                if (OnDataIn != null) OnDataIn.Invoke(inData);
                lock (_receiveQueue)
                    _receiveQueue.Enqueue(inData);
            }
        }


        //------------- SEND STUFF -----------------

        private void _BufferSendData(byte[] dataBufferToSend) {
            if (connected) {
                lock (_sendQueue) {
                    _sendQueue.Enqueue(dataBufferToSend);
                    send_MRSTE.Set();
                    send_MRSTE.Reset();
                }
            }
        }

#if !UNITY_EDITOR && UNITY_METRO
        private async void _sendData(byte[] data, string hostName, int port) {
            await udpClient.SendAsync(data, data.Length, hostName, port);

            using (var stream = await udpClient.GetOutputStreamAsync(new HostName(hostName), port.ToString())) {
                using (var writer = new DataWriter(stream)) {
                    writer.WriteBytes(data);
                    await writer.StoreAsync();
                }
            }
#else
        private void _sendData(byte[] data, string hostName, int port) {
            udpClient.Send(data, data.Length, hostName, port); 
#endif
        }

        private void Register() {
            RegisterObject regObj = new RegisterObject();
            regObj.socketID = _socketID;
            regObj.isSender = _isSender;
            regObj.localIP = localIP;
            regObj.UID = UID;
            string json = JsonUtility.ToJson(regObj);
            byte[] sendBytes = Encoding.ASCII.GetBytes((char)100 + json);
            _sendData(sendBytes, _serverHostname, _serverPort);
        }

        private void Punch() {
            byte[] sendBytes = Encoding.ASCII.GetBytes((char)100 + "{\"type\":\"punch\"}");
            _sendData(sendBytes, _remoteAddress, _remotePort);
        }

#if !UNITY_EDITOR && UNITY_METRO
        private async void DataSender() {
#else
        private void DataSender() {
#endif
            _sendRunning = true;

            while (_sendRunning) {
                send_MRSTE.WaitOne();
                int queueCount = 1;
                while (queueCount > 0) {
                    byte[] nextPacket = new byte[0];
                    lock (_sendQueue) {
                        queueCount = _sendQueue.Count;
                        if (queueCount > 0) {
                            nextPacket = _sendQueue.Dequeue();
                        }
                    }
                    if (nextPacket.Length != 0) {

#if !UNITY_EDITOR && UNITY_METRO
                        await _sendData(nextPacket, _remoteAddress, _remotePort);
#else
                        _sendData(nextPacket, _remoteAddress, _remotePort);
#endif
                    }
                }

            }
            if (debugLevel > 0) Debug.Log("DataSender Stopped");
        }
    }
}