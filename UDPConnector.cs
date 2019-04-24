/*
This file is part of the OpenIMPRESS project.

OpenIMPRESS is free software: you can redistribute it and/or modify
it under the terms of the Lesser GNU Lesser General Public License as published
by the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

OpenIMPRESS is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Lesser General Public License for more details.

You should have received a copy of the GNU Lesser General Public License
along with OpenIMPRESS. If not, see <https://www.gnu.org/licenses/>.
*/

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

    public enum OI_MSGFAMILY {
        MM = 0x64,
        DATA = 0x14,

        RGBD = 0x02,
        RGBD_CMD = 0x12,

        MOCAP = 0x03,
        AUDIO = 0x04,
        XR = 0x10
    }

    public enum OI_MSGTYPE_MM {

    }

    public enum OI_MSGTYPE_DATA {
        DEFAULT = 0x00
    }

    public enum OI_MSGTYPE_MOCAP {
        OI_MSG_TYPE_MOCAP_CONFIG = 0x01,
        OI_MSG_TYPE_MOCAP_BODY_FRAME_KINECTV1 = 0x12,
        OI_MSG_TYPE_MOCAP_BODY_FRAME_KINECTV2 = 0x13,

        // XSens, Vicon, OptiTrack, ...
        OI_MSG_TYPE_HUMAN_BODY_FRAME = 0x21,
        OI_MSG_TYPE_RIGIDBODY_FRAME = 0x22,

        OI_MSG_TYPE_MOCAP_LEAP_MOTION_CONFIG = 0x41,
        OI_MSG_TYPE_MOCAP_LEAP_MOTION_FRAME = 0x42
    }

    public enum OI_MSGTYPE_RGBD {
        CONFIG = 0x01,
        DEPTH = 0x11,
        DEPTH_BLOCK = 0x12,
        COLOR = 0x21,
        COLOR_BLOCK = 0x22,
        BODY_ID_TEXTURE = 0x51,
        BODY_ID_TEXTURE_BLOCK = 0x52
    }

    public enum OI_MSGTYPE_RGBD_CMD {
        REQUEST = 0x31,
        REQUEST_JSON = 0x32, // TODO: replace with content format header...
        RESPONSE = 0x41,
        RESPONSE_JSON = 0x42 // TODO: replace with content format header...
    }

    public enum OI_MSGTYPE_AUDIO {
        CONFIG = 0x01,
        DEFAULT_FRAME = 0x11
    }

    public enum OI_MSGTYPE_XR {
        TRANSFORM = 0x11,
        LINE_DRAWING = 0x21,
        MESH = 0x51,
        SPATIAL_MESH_ADD = 0x55,
        SPATIAL_MESH_REMOVE = 0x56
    }

    public class OIMSG {
        public UInt32 sequenceID;
        public UInt32 partsAm;
        public UInt32 currentPart;
        public UInt64 timestamp;
        public byte msgFamily;
        public byte msgType;
        public byte[] data;
        public OIMSG() {
        }
        public OIMSG(byte msgFamily, byte msgType, byte[] data) {
            this.data = data;
            this.msgFamily = msgFamily;
            this.msgType = msgType;
            this.timestamp = UDPConnector.NOW();
        }
    }

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
        public delegate void _DataIn(OIMSG msg);
        public event _DataIn OnDataIn;

        public delegate void _DataOut(OIMSG msg);
        public event _DataOut OnDataOut;

        // Public settings, applied in Start()
        public int debugLevel;
        public string SocketID;
        public bool UseMatchmakingServer = true;
        public string ManualHostName = "";
        public int ManualPort;
        public int ManualListenPort;
        public bool IsSender;
        // =======================================

        // Socket Description
        private string _socketID;
        private bool _isSender;

        // Socket Connection (From MM or manually set)
        public string _remoteAddress { get; private set; }
        public int _remotePort { get; private set; }
        public int _listenPort { get; private set; }
        private bool _useMatchmakingServer;

        // MM Server
        private string _serverHostname;
        private int _serverPort;

        private string UID;
        private string localIP = "";


        private bool _sendRunning = false;
        AutoResetEvent send_ResetEvent = new AutoResetEvent(false);


        private bool _listenRunning = false;
        private Queue<byte[]> _sendQueue = new Queue<byte[]>();
        private System.Object _sendQueueLock = new System.Object();
        private Queue<OIMSG> _receiveQueue = new Queue<OIMSG>();
        private System.Object _receiveQueueLock = new System.Object();
        Dictionary<UInt32, byte[][]> _dataParts = new Dictionary<UInt32, byte[][]>();

        private int headerLen = 24;
        private int cutoffLength = 60000; // TODO: parameterize udp packet size

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
                _listenPort = ManualListenPort;
            }
            _isSender = IsSender;
            _socketID = SocketID;

            //cutoffLength = 60000 - headerLen;
            localIP = GetLocalIPAddress();
            UID = sm.GetGUID();
            if (debugLevel > 0) {
                UID = UID + guidSuffix;
            }

#if !UNITY_EDITOR && UNITY_METRO
            await Task.Delay(UnityEngine.Random.Range(500, 1500));
            _listenTask = Task.Run(() => DataListener());
            await Task.Delay(UnityEngine.Random.Range(500, 1500));
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
            } else {
                if (Time.time > lastSentHB + HBInterval) {
                    lastSentHB = Time.time;
                    Punch();
                }
            }
        }

        UInt32 packageSequenceID = 0;
        public void SendData(byte[] nextPacket, byte msgFamily, byte msgType) {
            OIMSG msg = new OIMSG(msgFamily, msgType, nextPacket);
            msg.timestamp = NOW();
            SendData(msg);
        }


        public void SendData(byte[] nextPacket) {
            SendData(nextPacket, 0x14, 0x00);
        }

        public void SendData(OIMSG msg) {
            if (msg.data.Length != 0)
                if (OnDataOut != null) OnDataOut.Invoke(msg);

            if (connected) {
                if (msg.data.Length != 0) {
                    packageSequenceID++;
                    UInt32 partsAm = (UInt32)((msg.data.Length + cutoffLength - 1) / cutoffLength); // Round Up The Result Of Integer Division
                    UInt32 currentPart = 1;

                    while (msg.data.Length > 0) {
                        byte[] cutData = new byte[0];
                        if (msg.data.Length > cutoffLength) {
                            cutData = new byte[cutoffLength];
                            Array.Copy(msg.data, cutData, cutoffLength);

                            int remainingLen = msg.data.Length - cutoffLength;
                            byte[] remainder = new byte[remainingLen];
                            Array.Copy(msg.data, cutoffLength, remainder, 0, remainingLen);
                            msg.data = remainder;
                        } else {
                            cutData = msg.data;
                            msg.data = new byte[0];
                        }

                        byte[] sendBytes;
                        using (MemoryStream fs = new MemoryStream())
                        using (BinaryWriter writer = new BinaryWriter(fs)) {
                            writer.Write((byte)msg.msgFamily);
                            writer.Write((byte)msg.msgType);
                            writer.Write((UInt16)0);
                            writer.Write(packageSequenceID);
                            writer.Write(partsAm);
                            writer.Write(currentPart);
                            writer.Write((UInt64)msg.timestamp);
                            writer.Write(cutData);
                            sendBytes = fs.ToArray();
                        }
                        _BufferSendData(sendBytes);
                        currentPart++;
                    }
                }
            }
        }

        public OIMSG GetNewData() {
            OIMSG returnMsg = null;
            //byte[] returnBytes = null;
            lock (_receiveQueueLock) {
                if (_receiveQueue.Count > 0) {
                    //returnBytes = _receiveQueue.Dequeue();
                    returnMsg = _receiveQueue.Dequeue();
                }
            }
            //return returnBytes;
            return returnMsg;
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
            udpClient.Control.InboundBufferSizeInBytes = 65507;
            //udpClient.Control.DontFragment = true;
            //udpClient.Control.QualityOfService = SocketQualityOfService.LowLatency;
            udpClient.MessageReceived += Listener_MessageReceived;
            try {

                if (_useMatchmakingServer) {
                    await udpClient.BindEndpointAsync(null, "0");
                } else {
                    await udpClient.BindEndpointAsync(null, _listenPort.ToString());
                }
                //if(debugLevel > 0)
                Debug.Log("Listening on port: " + udpClient.Information.LocalPort);
            } catch (Exception e) {
                //if(debugLevel > 0)
                Debug.Log("DATA LISTENER START EXCEPTION: " + e.ToString());
                //if(debugLevel > 0)
                Debug.Log(SocketError.GetStatus(e.HResult).ToString());
                return;
            }
        }
#else
        private void DataListener() {
            IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
            if (!_useMatchmakingServer) anyIP = new IPEndPoint(IPAddress.Any, _listenPort);
            udpClient = new UdpClient(anyIP);

            udpClient.Client.ReceiveBufferSize = 65507 * 32;
            udpClient.Client.SendBufferSize = 65507 * 32;
            //_udpClient.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, a big value like 0x40000)

            _listenPort = ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
            if (debugLevel > 0) Debug.Log("Client listening on " + _listenPort);

            _listenRunning = true;
            IPEndPoint recvEP = new IPEndPoint(IPAddress.Any, 0);
            while (_listenRunning) {
                try {
                    byte[] receivedPackage = udpClient.Receive(ref recvEP);
                    HandleReceivedData(receivedPackage);
                } catch (Exception e) {
                    if (_listenRunning) Debug.LogWarning("Exception in UDPConnector.DataListener: "+e);
                }
            }
            udpClient.Close();
            if (debugLevel > 0) Debug.Log("DataListener Stopped");
        }
#endif


#if !UNITY_EDITOR && UNITY_METRO
        private void Listener_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args) {
            try {
                Stream streamIn = args.GetDataStream().AsStreamForRead();
                MemoryStream ms = ToMemoryStream(streamIn);
                byte[] receivedPackage = ms.ToArray();
                HandleReceivedData(receivedPackage);
            } catch (Exception e) {
                if (debugLevel > 0) Debug.Log("DATA LISTENER EXCEPTION: " + e.ToString());
                if (debugLevel > 0) Debug.Log(SocketError.GetStatus(e.HResult).ToString());
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
            if (debugLevel > 3) {
                string dString = Encoding.ASCII.GetString(inData);
                Debug.Log(dString.Length + "  " + (byte)inData[0] + "  " + dString);
            }

            byte magicByte = inData[0];
            if (magicByte == 100) {
                string json = Encoding.ASCII.GetString(inData, 1, inData.Length - 1);
                try {
                    AnswerObject obj = JsonUtility.FromJson<AnswerObject>(json);
                    if (obj.type == "answer") {
                        if (debugLevel > 1) Debug.Log("MM Answer: " + obj.address + ":" + obj.port);
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
            } else { //if (magicByte == 0x14) {
                lastReceivedHB = currentTime;
                OIMSG msg = new OIMSG();

                using (MemoryStream str = new MemoryStream(inData)) {
                    using (BinaryReader reader = new BinaryReader(str)) {
                        msg.msgFamily = reader.ReadByte();
                        msg.msgType = reader.ReadByte();
                        UInt16 unused2 = reader.ReadUInt16();
                        msg.sequenceID = reader.ReadUInt32();
                        msg.partsAm = reader.ReadUInt32();
                        msg.currentPart = reader.ReadUInt32();
                        msg.timestamp = reader.ReadUInt64();
                    }
                }

                msg.data = new byte[inData.Length - headerLen];
                Array.Copy(inData, headerLen, msg.data, 0, inData.Length - headerLen);

                //if (debugLevel > 1) Debug.Log("family: "+msg.msgFamily+" type: " +msg.msgType+" packageSequenceID:  " + msg.sequenceID + ", partsAm: " + msg.partsAm + ", currentPart: " + msg.currentPart + ", size: " + inData.Length);
                if (debugLevel > 1) Debug.Log("family: " + msg.msgFamily + " type: " + msg.msgType + ", partsAm: " + msg.partsAm + ", currentPart: " + msg.currentPart);
                if (msg.partsAm == 1) {
                    lock (_receiveQueueLock) {
                        _receiveQueue.Enqueue(msg);
                    }
                    if (OnDataIn != null) OnDataIn.Invoke(msg);
                } else if (msg.partsAm > 1) {
                    if (!_dataParts.ContainsKey(msg.sequenceID)) {
                        byte[][] parts = new byte[msg.partsAm][];
                        parts[msg.currentPart-1] = msg.data;
                        _dataParts.Add(msg.sequenceID, parts);
                    } else {
                        byte[][] parts = _dataParts[msg.sequenceID];
                        parts[msg.currentPart-1] = msg.data;

                        bool dataComplete = true;
                        int concatDataSize = 0;
                        for (int i = 0; i < msg.partsAm; i++) {
                            if (parts[i] == null) {
                                dataComplete = false;
                                break;
                            }
                            concatDataSize += parts[i].Length;
                        }
                        if (dataComplete) {
                            _dataParts.Remove(msg.sequenceID);
                            byte[] concatData = new byte[concatDataSize];
                            int idx = 0;
                            for (int i = 0; i < msg.partsAm; i++) {
                                Array.Copy(parts[i], 0, concatData, idx, parts[i].Length);
                                idx += parts[i].Length;
                            }

                            msg.data = concatData;
                            lock (_receiveQueueLock)
                                _receiveQueue.Enqueue(msg);
                            if (OnDataIn != null) OnDataIn.Invoke(msg);
                        }
                    }
                }
            }
            /*
            else {
                lastReceivedHB = currentTime;
                if (OnDataIn != null) OnDataIn.Invoke(inData);
                lock (_receiveQueueLock)
                    _receiveQueue.Enqueue(inData);
            }
             */
        }


        //------------- SEND STUFF -----------------

        private void _BufferSendData(byte[] dataBufferToSend) {
            lock (_sendQueueLock) {
                _sendQueue.Enqueue(dataBufferToSend);
                send_ResetEvent.Set();
            }
        }

#if !UNITY_EDITOR && UNITY_METRO
        private async void _sendData(byte[] data, string hostName, int port) {
            using (var stream = await udpClient.GetOutputStreamAsync(new HostName(hostName), port.ToString())) {
                using (var writer = new DataWriter(stream)) {
                    writer.WriteBytes(data);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                }
            }
            if (debugLevel > 3) Debug.Log("Sent bytes: " + data.Length + " host: " + hostName + " port: " + port);
        }
#else
        private void _sendData(byte[] data, string hostName, int port) {
            udpClient.Send(data, data.Length, hostName, port);
        }
#endif

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

        public static UInt64 NOW() {
            return ((UInt64)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds);
        }

#if !UNITY_EDITOR && UNITY_METRO
        private async void DataSender() {
#else
        private void DataSender() {
#endif
            _sendRunning = true;

            while (_sendRunning) {
                send_ResetEvent.WaitOne();
                int queueCount = 1;
                while (queueCount > 0) {
                    byte[] nextPacket = new byte[0];
                    lock (_sendQueueLock) {
                        queueCount = _sendQueue.Count;
                        if (queueCount > 0) {
                            nextPacket = _sendQueue.Dequeue();
                        }
                    }
                    if (nextPacket.Length != 0) {
                        _sendData(nextPacket, _remoteAddress, _remotePort);
                    }
                }

            }
            if (debugLevel > 0) Debug.Log("DataSender Stopped");

#if !UNITY_EDITOR && UNITY_METRO
            await Task.Delay(100);
#endif
        }
    }
}
