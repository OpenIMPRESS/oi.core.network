using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Threading;

#if !UNITY_EDITOR && UNITY_METRO
using System.Threading.Tasks;
#else
#endif


namespace oi.core.network {

    public struct Message {
        public byte[] data;
        public float timestamp;
    }


    public class ConnectorLogger : MonoBehaviour {

#if !UNITY_EDITOR && UNITY_METRO
        private Task _writeTask;
#else
        private Thread _writeThread;
#endif
        private bool _writeRunning = false;
        private ManualResetEvent write_MRSTE = new ManualResetEvent(false);
        private Queue<Message> _writeQueue = new Queue<Message>();
        private string type;
        private string socketID;

        private float currentTime = 0;
        private string folderPath;

        public void Init(string path, UDPConnector client, bool dataIn) {
            folderPath = path;
            socketID = client.socketID;

            if (dataIn) {
                client.OnDataIn += NewData;
                type = "IN";
            } else {
                client.OnDataOut += NewData;
                type = "OUT";
            }

#if !UNITY_EDITOR && UNITY_METRO
            _writeTask = Task.Run(() => DataWriter());
#else
            _writeThread = new Thread(DataWriter);
		    _writeThread.Start();
#endif
        }

        private void NewData(byte[] data) {
            Message mes = new Message();
            mes.data = data;
            mes.timestamp = currentTime;
            lock (_writeQueue) {
                _writeQueue.Enqueue(mes);
                write_MRSTE.Set();
                write_MRSTE.Reset();
            }
        }

#if !UNITY_EDITOR && UNITY_METRO
        private async void DataWriter() {
#else
        private void DataWriter() {
#endif
            _writeRunning = true;

            string path = folderPath + "/" + socketID + "_" + type + ".rec";
            using (BinaryWriter writer = new BinaryWriter(File.Open(path, FileMode.Append))) {

                while (_writeRunning) {
                    write_MRSTE.WaitOne();
                    //Debug.Log(socketID + " writer Unlocked");
                    int queueCount = 1;

                    while (queueCount > 0) {
                        Message nextMes = new Message();
                        bool gotMes = false;
                        lock (_writeQueue) {
                            queueCount = _writeQueue.Count;
                            if (queueCount > 0) {
                                nextMes = _writeQueue.Dequeue();
                                gotMes = true;
                            }
                        }
                        if (gotMes) {
                            writer.Write(nextMes.timestamp);
                            writer.Write(nextMes.data.Length);
                            writer.Write(nextMes.data);
                            writer.Flush();
                        }
                    }
                }
                writer.Close();

            }
        }

        private void OnApplicationQuit() {
            _writeRunning = false;
        }

        private void Update() {
            currentTime = Time.time;
        }

    }


}