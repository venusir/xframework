using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XInput;
using XFramework.XLocalization;
using XFramework.XUI;
using XFramework.XUI.View;
using XFramework.XUpdate;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 暂停面板：继续 / 重开 / 主菜单 / 退出。
    /// <para>打开时 <c>UpdateManager.Pause()</c> 冻住全部分档更新与 Scaled 定时器；关闭时恢复。
    /// 这是框架的统一暂停口径，示例不自己维护第二套暂停状态。</para>
    /// </summary>
    public sealed class PausePanel : UIPanelBase
    {
        #region Fields

        private Text _title;
        private Text _binding;
        private Button _resume;
        private Button _restart;
        private Button _menu;
        private Button _exit;
        private string _modeId;

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            _modeId = userData as string ?? TapRushSession.ModeIdValue;

            _title = Find<Text>("txt_title");
            _binding = Find<Text>("txt_binding");
            _resume = Find<Button>("btn_resume");
            _restart = Find<Button>("btn_restart");
            _menu = Find<Button>("btn_menu");
            _exit = Find<Button>("btn_exit");

            _resume.onClick.AddListener(OnResumeClicked);
            _restart.onClick.AddListener(OnRestart);
            _menu.onClick.AddListener(OnMenu);
            _exit.onClick.AddListener(OnExit);

            UpdateManager.Pause();
            RefreshTexts();
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            UpdateManager.Resume();

            if (_resume != null) _resume.onClick.RemoveListener(OnResumeClicked);
            if (_restart != null) _restart.onClick.RemoveListener(OnRestart);
            if (_menu != null) _menu.onClick.RemoveListener(OnMenu);
            if (_exit != null) _exit.onClick.RemoveListener(OnExit);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override void OnLanguageChanged(string lang)
        {
            RefreshTexts();
        }

        #endregion

        #region Actions

        private void OnResumeClicked()
        {
            UIManager.PopAsync().Forget();
        }

        private void OnRestart()
        {
            UIManager.PopAsync().Forget();
            AppFlowController.Current?.RestartGameAsync(_modeId).Forget();
        }

        private void OnMenu()
        {
            UIManager.CloseAllAsync().Forget();
            AppFlowController.Current?.OpenMainMenuAsync().Forget();
        }

        private void OnExit()
        {
            AppFlowController.Current?.RequestExitAsync().Forget();
        }

        #endregion

        #region Helpers

        private void RefreshTexts()
        {
            SetText(_title, LocalizationManager.Get("pause.title"));
            SetText(_resume, LocalizationManager.Get("pause.resume"));
            SetText(_restart, LocalizationManager.Get("pause.restart"));
            SetText(_menu, LocalizationManager.Get("pause.menu"));
            SetText(_exit, LocalizationManager.Get("pause.exit"));

            var binding = InputManager.IsInitialized
                ? InputManager.GetBindingDisplayString("Pause")
                : string.Empty;
            _binding.text = string.Format(LocalizationManager.Get("pause.binding_format"), binding);
        }

        private T Find<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[Integration] PausePanel 缺少子节点 '{childName}'。");
                return null;
            }

            return child.GetComponent<T>();
        }

        private static void SetText(Button button, string text)
        {
            if (button == null)
                return;

            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = text;
        }

        private static void SetText(Text label, string text)
        {
            if (label != null)
                label.text = text;
        }

        #endregion
    }
}
