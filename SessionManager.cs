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