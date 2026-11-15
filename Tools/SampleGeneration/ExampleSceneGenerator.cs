using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XFramework.Example;

namespace XFramework.Samples.Generation
{
    /// <summary>
    /// 一次性生成 Example 示例的启动场景。
    /// <para><b>为什么需要它</b>：<c>Samples~/</c> 对 Unity 不可见（AssetDatabase 不导入），场景不能在示例
    /// 目录里直接生成。流程是：<c>Tools/generate-sample-scene.ps1 -Sample Example</c> 把示例脚本复制到
    /// <c>Assets/__XfwExampleGen__/</c>（保持 <c>.meta</c> GUID）→ 本类在临时目录里生成场景 →
    /// 脚本把产物移回 <c>Samples~/Example/</c> → 删除临时目录。</para>
    /// <para>本文件不随包发布，也不参与 Unity 的常规编译；只在生成时被复制进临时工程。</para>
    /// </summary>
    public static class ExampleSceneGenerator
    {
        #region Constants

        private const string TempRoot = "Assets/__XfwExampleGen__";
        private const string GeneratedRoot = TempRoot + "/Generated";
        private const string ScenePath = GeneratedRoot + "/Example.unity";

        #endregion

        #region Entry Point

        /// <summary>生成示例场景。由 Unity batchmode 的 <c>-executeMethod</c> 调用。</summary>
        public static void Generate()
        {
            EnsureFolder(GeneratedRoot);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
            cameraGo.transform.position = new Vector3(0f, 1f, -10f);

            var root = new GameObject("ExampleRoot");
            root.AddComponent<ReactiveSample>();
            root.AddComponent<MessageSample>();
            var timer = root.AddComponent<TimerSample>();
            var audio = root.AddComponent<AudioSample>();
            var hub = root.AddComponent<ExampleSampleHub>();

            var so = new SerializedObject(hub);
            so.FindProperty("_timer").objectReferenceValue = timer;
            so.FindProperty("_audio").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Example] Scene generation finished.");
        }

        #endregion

        #region Helpers

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        #endregion
    }
}
