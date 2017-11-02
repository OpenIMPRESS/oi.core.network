#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;


namespace oi.core.network.editor {

	[CustomEditor(typeof(SessionManager))]
	public class SessionManagerEditor : Editor {

		SessionManager sessionManager;

		SerializedProperty GUIDSuffixSP;
		SerializedProperty SessionSP;
		SerializedProperty MMHostNameSP;
		SerializedProperty MMPortSP;

		void OnEnable() {
			GUIDSuffixSP = serializedObject.FindProperty("GUIDSuffix");
			SessionSP = serializedObject.FindProperty("Session");
			MMHostNameSP = serializedObject.FindProperty("MMHostName");
			MMPortSP = serializedObject.FindProperty("MMPort");
		}

		public override void OnInspectorGUI() {
			sessionManager = (SessionManager) target;
			if (sessionManager == null) return;
        	//serializedObject.Update();

			// SHOW GUID
			string _guid = "<NOT VISIBLE IN EDIT MODE>";
			if (Application.isPlaying) _guid = sessionManager.GetGUID();
			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.TextField( "GUID:", _guid);
			EditorGUI.EndDisabledGroup();

			EditorGUI.BeginChangeCheck();


			EditorGUI.BeginDisabledGroup(Application.isPlaying);

			GUIDSuffixSP.stringValue = EditorGUILayout.TextField("GUID Suffix:", sessionManager.GUIDSuffix);
			SessionSP.stringValue = EditorGUILayout.TextField("Session:", sessionManager.Session);

			MMHostNameSP.stringValue = EditorGUILayout.TextField("MM Host:", sessionManager.MMHostName);
			MMPortSP.intValue = EditorGUILayout.IntField("MM Port:", sessionManager.MMPort);
			EditorGUI.EndDisabledGroup();

			if (!Application.isPlaying) serializedObject.ApplyModifiedProperties();
		}
	}
}
#endif