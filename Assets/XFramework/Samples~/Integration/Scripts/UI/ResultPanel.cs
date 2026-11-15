using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XData;
using XFramework.XLocalization;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 结算面板：显示本局得分与该模式最佳分，提供重试 / 返回主菜单。
    /// <para>最佳分来自 Data 模块（<see cref="IntegrationData"/>），已在 <see cref="AppFlowController.ShowResultAsync"/> 里更新并落盘。</para>
    /// </summary>
    public sealed class ResultPanel : UIPanelBase
    {
        #region Fields

        private Text _title;
        private Text _score;
        private Text _best;
        private Button _retry;
        private Button _menu;
        private IntegrationResult _result;

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            _title = Find<Text>("txt_title");
            _score = Find<Text>("txt_score");
            _best = Find<Text>("txt_best");
            _retry = Find<Button>("btn_retry");
            _menu = Find<Button>("btn_menu");

            _retry.onClick.AddListener(OnRetry);
            _menu.onClick.AddListener(OnMenu);

            if (userData is IntegrationResult result)
                _result = result;

            RefreshTexts();
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            if (_retry != null) _retry.onClick.RemoveListener(OnRetry);
            if (_menu != null) _menu.onClick.RemoveListener(OnMenu);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override void OnLanguageChanged(string lang)
        {
            RefreshTexts();
        }

        #endregion

        #region Actions

        private void OnRetry()
        {
            UIManager.PopAsync().Forget();
            AppFlowController.Current?.EnterGameAsync(_result.ModeId).Forget();
        }

        private void OnMenu()
        {
            GoMenuAsync().Forget();
        }

        private async UniTaskVoid GoMenuAsync()
        {
            // 结算后显示栈里只剩本面板；重新打开主菜单才能拿到更新后的「上次游玩」摘要。
            await UIManager.CloseAllAsync();

            var flow = AppFlowController.Current;
            if (flow != null)
                await flow.OpenMainMenuAsync();
        }

        #endregion

        #region Helpers

        private void RefreshTexts()
        {
            var data = DataManager.GetOrCreateBlock<IntegrationData>();
            var best = _result.ModeId == TapRushSession.ModeIdValue ? data.BestTapScore : data.BestOddScore;

            SetText(_title, LocalizationManager.Get("result.title"));
            SetText(_score, string.Format(LocalizationManager.Get("result.score_format"), _result.Score));
            SetText(_best, string.Format(LocalizationManager.Get("result.best_format"), best));
            SetText(_retry, LocalizationManager.Get("result.retry"));
            SetText(_menu, LocalizationManager.Get("result.menu"));
        }

        private T Find<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[Integration] ResultPanel 缺少子节点 '{childName}'。");
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
