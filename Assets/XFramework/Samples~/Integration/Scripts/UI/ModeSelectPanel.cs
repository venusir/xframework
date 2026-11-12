using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XConfig;
using XFramework.XLocalization;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 模式选择：从 Config 的模式表读出两个模式，点击进入对应玩法。
    /// <para>两个模式（点星 / 找异色）从 Config 的模式表读出，点击进入对应玩法。</para>
    /// </summary>
    public sealed class ModeSelectPanel : UIPanelBase
    {
        #region Fields

        private Text _title;
        private Text _hint;
        private Button _tap;
        private Button _odd;
        private Button _back;

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            _title = Find<Text>("txt_title");
            _hint = Find<Text>("txt_hint");
            _tap = Find<Button>("btn_mode_tap");
            _odd = Find<Button>("btn_mode_odd");
            _back = Find<Button>("btn_back");

            _tap.onClick.AddListener(OnTap);
            _odd.onClick.AddListener(OnOdd);
            _back.onClick.AddListener(OnBack);

            RefreshTexts();
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            if (_tap != null) _tap.onClick.RemoveListener(OnTap);
            if (_odd != null) _odd.onClick.RemoveListener(OnOdd);
            if (_back != null) _back.onClick.RemoveListener(OnBack);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override void OnLanguageChanged(string lang)
        {
            RefreshTexts();
        }

        #endregion

        #region Actions

        private void OnTap()
        {
            UIManager.PopAsync().Forget();
            AppFlowController.Current?.EnterGameAsync(TapRushSession.ModeIdValue).Forget();
        }

        private void OnOdd()
        {
            UIManager.PopAsync().Forget();
            AppFlowController.Current?.EnterGameAsync(OddOneOutSession.ModeIdValue).Forget();
        }

        private void OnBack()
        {
            UIManager.PopAsync().Forget();
        }

        #endregion

        #region Helpers

        private void RefreshTexts()
        {
            SetText(_title, LocalizationManager.Get("mode.title"));
            SetText(_back, LocalizationManager.Get("mode.back"));

            var table = ConfigManager.GetTable<GameModeRow>();
            if (table.TryGet(TapRushSession.ModeIdValue, out var tapRow))
                SetText(_tap, LocalizationManager.Get(tapRow.DisplayKey));

            if (table.TryGet("odd", out var oddRow))
                SetText(_odd, LocalizationManager.Get(oddRow.DisplayKey));

            _odd.interactable = true;
            _hint.text = string.Empty;
        }

        private T Find<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[Integration] ModeSelectPanel 缺少子节点 '{childName}'。");
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
