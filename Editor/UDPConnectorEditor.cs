#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace oi.core.network.editor {

	[CustomEditor(typeof(UDPConnector))]
	public class UDPConnectorEditor : Editor {
		
		UDPConnector udpc;
		SerializedProperty debugLevelSP;
		SerializedProperty guidSuffixSP;
		SerializedProperty UseMatchmakingServerSP;
		SerializedProperty ManualHostNameSP;
		SerializedProperty ManualPortSP;
        SerializedProperty ManualListenPortSP;
        SerializedProperty SocketIDSP;
		SerializedProperty IsSenderSP;
		

		void OnEnable() {
			debugLevelSP = serializedObject.FindProperty("debugLevel");
			guidSuffixSP = serializedObject.FindProperty("guidSuffix");
			UseMatchmakingServerSP = serializedObject.FindProperty("UseMatchmakingServer");
			ManualHostNameSP = serializedObject.FindProperty("ManualHostName");
			ManualPortSP = serializedObject.FindProperty("ManualPort");
            ManualListenPortSP = serializedObject.FindProperty("ManualListenPort");
            SocketIDSP = serializedObject.FindProperty("SocketID");
			IsSenderSP = serializedObject.FindProperty("IsSender");
		}
		
		public override void OnInspectorGUI() {
			udpc = (UDPConnector) target;
			if (udpc == null) return;
        	serializedObject.Update();
			

			if (Application.isPlaying) {
				EditorGUILayout.Toggle("Connected? ", udpc.connected);
			}

			debugLevelSP.intValue = EditorGUILayout.IntField("Debug Level: ", udpc.debugLevel);

			EditorGUI.BeginDisabledGroup(Application.isPlaying);
			if (udpc.debugLevel > 0) {
				guidSuffixSP.stringValue = EditorGUILayout.TextField("GUID Suffix:", udpc.guidSuffix);
			}

			UseMatchmakingServerSP.boolValue = EditorGUILayout.Toggle("Use Matchmaking: ", udpc.UseMatchmakingServer);
			if (!udpc.UseMatchmakingServer || Application.isPlaying) {
				ManualHostNameSP.stringValue = EditorGUILayout.TextField("Send Host:",
					Application.isPlaying ? udpc._remoteAddress : udpc.ManualHostName);
				ManualPortSP.intValue = EditorGUILayout.IntField("Send Port:",
					Application.isPlaying ? udpc._remotePort : udpc.ManualPort);
                ManualListenPortSP.intValue = EditorGUILayout.IntField("Listen Port:",
                    Application.isPlaying ? udpc._listenPort : udpc.ManualListenPort);
            }
			
			SocketIDSP.stringValue = EditorGUILayout.TextField("Socket ID:", udpc.SocketID);
			IsSenderSP.boolValue = EditorGUILayout.Toggle("Is Sender:", udpc.IsSender);
			EditorGUI.EndDisabledGroup();

			if (!Application.isPlaying) serializedObject.ApplyModifiedProperties();
		}
	}
}
#endif