using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XFramework.XCamera.Cinemachine.Samples;

namespace XFramework.Samples.Generation
{
    /// <summary>
    /// 一次性生成 Cinemachine 示例的启动场景。
    /// <para><b>为什么需要它</b>：<c>Samples~/</c> 对 Unity 不可见（AssetDatabase 不导入），场景不能在示例
    /// 目录里直接生成。流程是：<c>Tools/generate-sample-scene.ps1 -Sample Cinemachine</c> 把示例脚本复制到
    /// <c>Assets/__XfwCinemachineGen__/</c>（保持 <c>.meta</c> GUID）→ 本类在临时目录里生成场景 →
    /// 脚本把产物移回 <c>Samples~/Cinemachine/</c> → 删除临时目录。</para>
    /// <para>本文件不随包发布，也不参与 Unity 的常规编译；只在生成时被复制进临时工程。</para>
    /// </summary>
    public static class CinemachineSceneGenerator
    {
        #region Constants

        private const string TempRoot = "Assets/__XfwCinemachineGen__";
        private const string GeneratedRoot = TempRoot + "/Generated";
        private const string ScenePath = GeneratedRoot + "/Cinemachine.unity";

        #endregion

        #region Entry Point

        /// <summary>生成示例场景。由 Unity batchmode 的 <c>-executeMethod</c> 调用。</summary>
        public static void Generate()
        {
            EnsureFolder(GeneratedRoot);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener),
                typeof(CinemachineBrain));
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
            cameraGo.transform.position = new Vector3(0f, 1.5f, -8f);
            var brain = cameraGo.GetComponent<CinemachineBrain>();

            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            target.name = "FollowTarget";
            target.transform.position = new Vector3(0f, 1f, 0f);

            var firstGo = new GameObject("First", typeof(CinemachineCamera), typeof(CinemachineImpulseListener));
            firstGo.transform.position = new Vector3(0f, 1.6f, -4.5f);
            var first = firstGo.GetComponent<CinemachineCamera>();
            first.LookAt = target.transform;

            var secondGo = new GameObject("Second", typeof(CinemachineCamera), typeof(CinemachineImpulseListener));
            secondGo.transform.position = new Vector3(4f, 2.4f, -4.5f);
            var second = secondGo.GetComponent<CinemachineCamera>();
            second.LookAt = target.transform;

            var sampleGo = new GameObject("CameraSample", typeof(CameraSample));
            var sample = sampleGo.GetComponent<CameraSample>();
            var so = new SerializedObject(sample);
            so.FindProperty("_brain").objectReferenceValue = brain;
            so.FindProperty("_first").objectReferenceValue = first;
            so.FindProperty("_second").objectReferenceValue = second;
            so.FindProperty("_follow").objectReferenceValue = target.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            var overlayGo = new GameObject("Overlay", typeof(CinemachineSampleOverlay));
            var overlay = overlayGo.GetComponent<CinemachineSampleOverlay>();
            var overlaySo = new SerializedObject(overlay);
            overlaySo.FindProperty("_sample").objectReferenceValue = sample;
            overlaySo.FindProperty("_target").objectReferenceValue = target.transform;
            overlaySo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Cinemachine] Scene generation finished.");
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
