using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerHandFeedback))]
[CanEditMultipleObjects]
public class PlayerHandFeedbackEditor : Editor
{
    private SerializedProperty _debugHold;
    private SerializedProperty _debugPose;

    private void OnEnable()
    {
        _debugHold = serializedObject.FindProperty("debugHoldPoseForTuning");
        _debugPose = serializedObject.FindProperty("debugTuningPose");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject,
            "m_Script",
            "debugHoldPoseForTuning",
            "debugTuningPose");

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_debugHold, new GUIContent("Hold pose for tuning"));
        EditorGUILayout.PropertyField(_debugPose, new GUIContent("Frozen pose"));

        EditorGUILayout.Space(4f);
        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("Play Interact (E)"))
        {
            foreach (var t in targets)
                ((PlayerHandFeedback)t).PlayGesture(PlayerHandFeedback.HandGestureKind.Interact);
        }

        if (GUILayout.Button("Play Inspect (R)"))
        {
            foreach (var t in targets)
                ((PlayerHandFeedback)t).PlayGesture(PlayerHandFeedback.HandGestureKind.HeatInspect);
        }

        EditorGUI.EndDisabledGroup();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode, enable Hold pose for tuning to see the hand frozen on the chosen pose, " +
                "then adjust Hidden / Visible positions and Eulers in real time. Use the buttons to preview full gestures.",
                MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
