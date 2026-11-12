using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XLocalization;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 主菜单：打开设置 / 语言，或退出。玩法入口（Play）在后续步骤接上。
    /// <para>面板子节点按命名约定用 <c>transform.Find</c> 获取；预制体由示例的资源生成器产出，
    /// 第三方可以在导入后的 Hierarchy 里看到同样的结构。</para>
    /// </summary>
    public sealed class MainMenuPanel : UIPanelBase
    {
        #region Fields

        private Text _title;
        private Text _lastRun;
        private Button _play;
        private Button _settings;
        private Button _language;
        private Button _exit;

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            _title = Find<Text>("txt_title");
            _lastRun = Find<Text>("txt_last");
            _play = Find<Button>("btn_play");
            _settings = Find<Button>("btn_settings");
            _language = Find<Button>("btn_language");
            _exit = Find<Button>("btn_exit");

            _play.onClick.AddListener(OnPlay);
            _settings.onClick.AddListener(OnSettings);
            _language.onClick.AddListener(OnLanguage);
            _exit.onClick.AddListener(OnExit);

            // 玩法入口：打开模式选择。
            _play.interactable = true;

            if (userData is LastRunSummary summary)
                _lastRun.text = string.Format(LocalizationManager.Get("menu.last_format"),
                    summary.Sessions, summary.LastMode, summary.BestScore);

            RefreshTexts();
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            if (_play != null) _play.onClick.RemoveListener(OnPlay);
            if (_settings != null) _settings.onClick.RemoveListener(OnSettings);
            if (_language != null) _language.onClick.RemoveListener(OnLanguage);
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

        private void OnPlay()
        {
            AppFlowController.Current?.OpenModeSelectAsync().Forget();
        }

        private void OnSettings()
        {
            AppFlowController.Current?.OpenSettingsAsync().Forget();
        }

        private void OnLanguage()
        {
            AppFlowController.Current?.OpenLanguageAsync().Forget();
        }

        private void OnExit()
        {
            AppFlowController.Current?.RequestExitAsync().Forget();
        }

        #endregion

        #region Helpers

        private void RefreshTexts()
        {
            SetText(_title, LocalizationManager.Get("menu.title"));
            SetText(_play, LocalizationManager.Get("menu.play"));
            SetText(_settings, LocalizationManager.Get("menu.settings"));
            SetText(_language, LocalizationManager.Get("menu.language"));
            SetText(_exit, LocalizationManager.Get("menu.exit"));
        }

        private T Find<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[Integration] MainMenuPanel 缺少子节点 '{childName}'。");
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
