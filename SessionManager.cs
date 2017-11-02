using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace oi.core.network {
	public class SessionManager : MonoBehaviour {

		// TODO: these should be serialized and not publicly editable at runtime.
		public string Session = "";
		public string GUIDSuffix = "";
		public string MMHostName = "mm.openimpress.org";
		public int MMPort = 6312;

		// this is a walkaround...
		private string _guid;
		private string _session;
		private string _mmHostName;
		private int _mmPort;

		void Awake() {
			_guid = SystemInfo.deviceUniqueIdentifier + GUIDSuffix;
			_session = Session;
			_mmHostName = MMHostName;
			_mmPort = MMPort;
		}

		void Start () {
			
		}
		
		void Update () {
			
		}

		public string GetGUID() {
			return _guid;
		}

		public string GetSession() {
			return _session;
		}

		public string GetMMHostName() {
			return _mmHostName;
		}
		public int GetMMPort() {
			return _mmPort;
		}

	}
}