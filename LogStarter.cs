using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;


namespace oi.core.network {

    public class LogStarter : MonoBehaviour {

        private UDPConnector[] clients;
        private StreamWriter writer;
        private string folderPath;


        private void Start() {
            var time = System.DateTime.UtcNow;
            string timeString = time.Year.ToString("0000") + time.Month.ToString("00") + time.Day.ToString("00") + "_" + time.Hour.ToString("00") + time.Minute.ToString("00") + time.Second.ToString("00");

            folderPath = "Recordings/" + timeString;
            if (!Directory.Exists(folderPath)) {
                DirectoryInfo di = Directory.CreateDirectory(folderPath);
            }


            clients = FindObjectsOfType<UDPConnector>();
            foreach (UDPConnector client in clients) {
                //if (client.socketID != "kinect1") continue;

                ConnectorLogger newLoggerIn = gameObject.AddComponent<ConnectorLogger>() as ConnectorLogger;
                newLoggerIn.Init(folderPath, client, true);

                ConnectorLogger newLoggerOut = gameObject.AddComponent<ConnectorLogger>() as ConnectorLogger;
                newLoggerOut.Init(folderPath, client, false);
            }

        }
    }
}
