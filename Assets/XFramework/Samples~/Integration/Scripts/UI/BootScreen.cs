using UnityEngine;
using XFramework.XAsset;
using XFramework.XData;
using XFramework.XInput;
using XFramework.XLocalization;
using XFramework.XSave;
using XFramework.XSettings;
using XFramework.XUI;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 启动加载屏（IMGUI 演示壳，<b>不走 UIManager</b>）。
    /// <para>它存在的意义是把「各模块是否就绪」摆出来：启动阶段是异步的，业务层需要一个明确的
    /// 完成信号（<see cref="AppFlowController.NotifyBootstrapped"/> 之前的可视反馈）。</para>
    /// <para>用 IMGUI 是为了让示例不依赖预制体与 TMP 字体资源；生产项目通常会换成真正的加载界面。</para>
    /// </summary>
    public sealed class BootScreen : MonoBehaviour
    {
        #region Private State

        private bool _ready;
        private bool _exited;
        private string _error;

        #endregion

        #region Public API

        /// <summary>启动完成：隐藏加载屏。</summary>
        public void MarkReady() => _ready = true;

        /// <summary>启动失败：显示错误信息。</summary>
        public void ShowError(string message)
        {
            _error = message;
        }

        /// <summary>退出流程已清理完服务（编辑器内 <c>Application.Quit()</c> 无效）。</summary>
        public void ShowExited()
        {
            _exited = true;
            _ready = false;
        }

        #endregion

        #region IMGUI

        private void OnGUI()
        {
            if (_ready && !_exited && string.IsNullOrEmpty(_error))
                return;

            var area = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.Box(area, GUIContent.none);

            var x = Screen.width * 0.5f - 260f;
            var y = Screen.height * 0.5f - 150f;
            var rect = new Rect(x, y, 520f, 300f);
            GUILayout.BeginArea(rect);

            if (!string.IsNullOrEmpty(_error))
            {
                GUILayout.Label("启动失败");
                GUILayout.Label(_error);
            }
            else if (_exited)
            {
                GUILayout.Label("服务已清理。");
                GUILayout.Label("编辑器内 Application.Quit() 无效，请手动停止 Play。");
            }
            else
            {
                GUILayout.Label("正在启动 XFramework Integration 示例…");
                GUILayout.Space(8f);
                DrawRow("AssetManager", AssetManager.IsInitialized);
                DrawRow("DataManager", DataManager.IsInitialized);
                DrawRow("SaveManager", SaveManager.IsInitialized);
                DrawRow("UIManager", UIManager.IsInitialized);
                DrawRow("InputManager", InputManager.IsInitialized);
                DrawRow("LocalizationManager", LocalizationManager.IsInitialized);
                DrawRow("SettingsManager", SettingsManager.IsInitialized);
            }

            GUILayout.EndArea();
        }

        private static void DrawRow(string name, bool ready)
        {
            GUILayout.Label(string.Concat(ready ? "[ok]   " : "[wait] ", name));
        }

        #endregion
    }
}
