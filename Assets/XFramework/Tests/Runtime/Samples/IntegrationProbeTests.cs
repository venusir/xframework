using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XAsset;
using XFramework.XAudio;
using XFramework.XScene;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Tests
{
    /// <summary>
    /// Integration 示例的**一次性导入验收探针**：示例先由 <c>Tools/verify-integration-sample.ps1</c>
    /// 复制到 <c>Assets/Samples/Integration</c> 并建一份临时 YooAsset 采集设置，然后以
    /// <c>XFW_INTEGRATION_PROBE=1</c> 运行本 fixture。
    /// <para><b>为什么是环境变量门控</b>：全量测试没有导入副本与采集设置，本 fixture 必须自动跳过，
    /// 否则门禁会因「示例资源不在 AssetDatabase 里」而红。</para>
    /// <para><b>它做不到什么</b>：不能引用示例脚本类型（示例在 Assembly-CSharp，测试 asmdef 引用不到），
    /// 所以用 <see cref="UIPanelBase"/> 基类按地址打开面板；也不能驱动完整业务流程（那要真实场景与输入）。</para>
    /// </summary>
    [TestFixture]
    public class IntegrationProbeTests
    {
        #region Constants

        private const string ProbeEnvVar = "XFW_INTEGRATION_PROBE";
        private const string ImportedRelative = "Samples/Integration";

        private static readonly string[] Addresses =
        {
            "Integration.MainMenu",
            "Integration.Settings",
            "Integration.Language",
            "Integration.Confirm",
            "Integration.ModeSelect",
            "Integration.GamePanel",
            "Integration.Pause",
            "Integration.Loading",
            "Integration.Result",
            "Integration.Game",
            "Integration.GameModes",
            "Integration.Rules",
            "Integration.Bgm",
            "Integration.Click",
            "Integration.Correct",
            "Integration.Wrong",
            "Integration.Lang_zh_Hans",
            "Integration.Lang_en",
        };

        #endregion

        private static string ImportedRoot => Path.Combine(Application.dataPath, ImportedRelative);

        [Test]
        public async Task ImportedSample_AssetsLoad_AndUiAudioSceneWork()
        {
            if (Environment.GetEnvironmentVariable(ProbeEnvVar) != "1")
            {
                Assert.Ignore(
                    $"一次性导入验收探针：设置 {ProbeEnvVar}=1 并运行 Tools/verify-integration-sample.ps1。");
            }

            Assert.IsTrue(Directory.Exists(ImportedRoot),
                $"导入副本不存在：{ImportedRoot}。请用 Tools/verify-integration-sample.ps1 运行本探针。");

            GameObject uiRoot = null;

            try
            {
                await AssetManager.InitializeAsync(new AssetInitOptions
                {
                    PlayMode = AssetPlayMode.EditorSimulate,
                });
                Assert.IsTrue(AssetManager.IsInitialized, "AssetManager 初始化失败。");

                foreach (var address in Addresses)
                {
                    Assert.IsTrue(AssetManager.CheckLocationValid(address),
                        $"地址不可用：{address}（检查 YooAsset 采集设置）。");
                }

                using (var menu = await AssetManager.LoadAsync<GameObject>("Integration.MainMenu"))
                    Assert.IsTrue(menu.IsValid, "MainMenu 预制体加载失败。");

                using (var clip = await AssetManager.LoadAsync<AudioClip>("Integration.Click"))
                    Assert.IsTrue(clip.IsValid, "Click 音频加载失败。");

                // UI：用基类按地址打开，验证 AssetPanelFactory → 面板实例化 → 生命周期。
                uiRoot = new GameObject("IntegrationProbeRoot", typeof(RectTransform), typeof(Canvas));
                uiRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                UIManager.Initialize(uiRoot.transform);

                var panel = await UIManager.OpenAsync<UIPanelBase>("Integration.Loading", UILayers.Default);
                Assert.IsNotNull(panel, "Loading 面板打开失败（预制体缺 UIPanelBase 派生组件？）。");
                await UIManager.CloseAllAsync();

                // Audio：初始化 + 起播。无音频设备的 batchmode 下可能拿不到句柄，记警告而不判红。
                AudioManager.Initialize(new AudioInitOptions { MaxVoices = 4 });
                var audio = await AudioManager.PlayAsync("Integration.Click");
                if (audio.IsDefault)
                {
                    Debug.LogWarning(
                        "[Integration] 无音频设备：AudioManager.PlayAsync 返回 default，已跳过播放断言。");
                }
                else
                {
                    AudioManager.Stop(audio);
                }

                // Scene：Additive 加载并卸载。
                var scene = await SceneManager.LoadSceneAsync(
                    "Integration.Game", SceneLoadOptions.Additive);
                Assert.IsFalse(scene.IsDefault, "Game 场景加载失败。");

                // 给场景里的 Start 一帧时间，再卸载。
                await UniTask.Delay(200);
                await scene.UnloadAsync();
            }
            finally
            {
                if (uiRoot != null)
                    UnityEngine.Object.Destroy(uiRoot);

                if (UIManager.IsInitialized)
                    UIManager.Destroy();
                if (AudioManager.IsInitialized)
                    AudioManager.Destroy();
                if (AssetManager.IsInitialized)
                    AssetManager.Destroy();
            }
        }
    }
}
