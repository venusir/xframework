using UnityEditor;

namespace XFramework.XUI.Authoring.Editor
{
    /// <summary>
    /// <see cref="UIPanelDefinition"/> 的基础 Inspector：Kind、Version 与四个列表。
    /// </summary>
    [CustomEditor(typeof(UIPanelDefinition))]
    public sealed class UIPanelDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Kind"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Version"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TextBindings"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ActionBindings"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TabGroups"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DataBindings"), true);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
