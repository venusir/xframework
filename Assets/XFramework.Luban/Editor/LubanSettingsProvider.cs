using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// <see cref="LubanGeneratorSettings"/> 的 Project Settings 页面。
    /// </summary>
    internal sealed class LubanSettingsProvider : SettingsProvider
    {
        private SerializedObject _serialized;

        private LubanSettingsProvider(string path, SettingsScope scope)
            : base(path, scope)
        {
        }

        [SettingsProvider]
        internal static SettingsProvider Create()
        {
            return new LubanSettingsProvider("Project/XFramework/Luban", SettingsScope.Project)
            {
                label = "XFramework Luban",
                keywords = new[] { "Luban", "Config", "XFramework" },
            };
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _serialized = new SerializedObject(LubanGeneratorSettings.instance);
        }

        public override void OnGUI(string searchContext)
        {
            if (_serialized == null)
            {
                return;
            }

            _serialized.Update();

            EditorGUILayout.LabelField("工具", EditorStyles.boldLabel);
            Draw("DotnetPath", "dotnet 路径");
            Draw("LubanDllPath", "Luban.dll 路径（项目相对或绝对）");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Luban 工程", EditorStyles.boldLabel);
            Draw("ConfPath", "luban.conf 路径（项目相对或绝对）");
            Draw("TargetName", "Target 名");
            Draw("TablesClassName", "Tables 类名");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("输出目录（项目相对，必须在 Assets/ 下）", EditorStyles.boldLabel);
            Draw("CodeOutputDir", "代码输出目录");
            Draw("DataOutputDir", "数据输出目录");
            Draw("GlueOutputDir", "接缝与适配器目录");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("生成目标", EditorStyles.boldLabel);
            Draw("CodeTarget", "代码目标");
            Draw("DataTarget", "数据目标");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("适配器", EditorStyles.boldLabel);
            Draw("AdapterNamespace", "命名空间");
            Draw("AdapterClassName", "类名");
            Draw("GenerateSeams", "生成 IConfigRow 接缝");
            Draw("ExtraArguments", "附加参数（原样追加）");

            if (_serialized.ApplyModifiedProperties())
            {
                LubanGeneratorSettings.instance.SaveSettings();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "接缝与适配器必须与 Luban 生成的代码处于同一个 asmdef 作用域；" +
                "该 asmdef 需直接引用 Venusy609.Xframework、Venusy609.Xframework.Luban 与 Luban.Runtime。",
                MessageType.Info);

            if (GUILayout.Button("生成配置表（运行 Luban + 刷新胶水）"))
            {
                EditorApplication.ExecuteMenuItem(LubanMenu.GenerateMenu);
            }
        }

        private void Draw(string propertyName, string label)
        {
            var property = _serialized.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label), true);
            }
        }
    }
}
