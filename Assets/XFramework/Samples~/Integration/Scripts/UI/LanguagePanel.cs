using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XLocalization;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 语言面板：中文 / English 两个按钮，当前语言加标记。
    /// <para>语言表经 <c>LocalizationManager.LanguageAssetPath</c> 模板从 YooAsset 地址加载；
    /// 切换后所有面板的 <c>OnLanguageChanged</c> 会被框架回调，各自刷新文本。</para>
    /// </summary>
    public sealed class LanguagePanel : UIPanelBase
    {
        #region Fields

        private Text _title;
        private Text _status;
        private Button _zh;
        private Button _en;
        private Button _back;

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            _title = Find<Text>("txt_title");
            _status = Find<Text>("txt_status");
            _zh = Find<Button>("btn_zh");
            _en = Find<Button>("btn_en");
            _back = Find<Button>("btn_back");

            _zh.onClick.AddListener(OnChinese);
            _en.onClick.AddListener(OnEnglish);
            _back.onClick.AddListener(OnBack);

            RefreshTexts();
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            if (_zh != null) _zh.onClick.RemoveListener(OnChinese);
            if (_en != null) _en.onClick.RemoveListener(OnEnglish);
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

        private void OnChinese()
        {
            SwitchAsync("zh_Hans");
        }

        private void OnEnglish()
        {
            SwitchAsync("en");
        }

        private void SwitchAsync(string language)
        {
            SwitchAsyncCore(language).Forget();
        }

        private async UniTask SwitchAsyncCore(string language)
        {
            await AppFlowController.Current.SwitchLanguageAsync(language);
            RefreshTexts();
        }

        private void OnBack()
        {
            UIManager.PopAsync().Forget();
        }

        #endregion

        #region Helpers

        private void RefreshTexts()
        {
            SetText(_title, LocalizationManager.Get("language.title"));
            SetText(_zh, LocalizationManager.Get("language.zh"));
            SetText(_en, LocalizationManager.Get("language.en"));
            SetText(_back, LocalizationManager.Get("language.back"));

            var current = LocalizationManager.CurrentLanguage;
            _status.text = string.Format(LocalizationManager.Get("language.current_format"), current);

            // 当前语言按钮置灰，避免重复点击。
            _zh.interactable = current != "zh_Hans";
            _en.interactable = current != "en";
        }

        private T Find<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[Integration] LanguagePanel 缺少子节点 '{childName}'。");
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
