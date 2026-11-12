using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XLocalization;
using XFramework.XMessage;
using XFramework.XSettings;
using XFramework.XUI;
using XFramework.XUI.View;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 设置面板：三个音量滑条 + 提示开关。
    /// <para>双向绑定是手写的（滑条 → <c>SettingRef</c>，<c>SettingRef</c> → 滑条），因为核心的
    /// <c>UIBinder</c> 绑定目标是 TMP 文本/滑条组合；示例用普通 <c>Text</c>，故显式写出来更直观。
    /// <c>SettingRef</c> 本身实现 <c>IReactiveProperty</c>，生产项目可按自己的 UI 技术栈绑定。</para>
    /// </summary>
    public sealed class SettingsPanel : UIPanelBase
    {
        #region Fields

        private Text _title;
        private Text _masterLabel;
        private Text _bgmLabel;
        private Text _sfxLabel;
        private Text _status;
        private Slider _master;
        private Slider _bgm;
        private Slider _sfx;
        private Toggle _hints;
        private Button _apply;
        private Button _reset;
        private Button _back;

        #endregion

        #region Lifecycle

        /// <inheritdoc/>
        protected override UniTask OnOpen(object userData)
        {
            _title = Find<Text>("txt_title");
            _masterLabel = Find<Text>("txt_master");
            _bgmLabel = Find<Text>("txt_bgm");
            _sfxLabel = Find<Text>("txt_sfx");
            _status = Find<Text>("txt_status");
            _master = Find<Slider>("sld_master");
            _bgm = Find<Slider>("sld_bgm");
            _sfx = Find<Slider>("sld_sfx");
            _hints = Find<Toggle>("tgl_hints");
            _apply = Find<Button>("btn_apply");
            _reset = Find<Button>("btn_reset");
            _back = Find<Button>("btn_back");

            BindSlider(_master, IntegrationSettingRefs.MasterVolume);
            BindSlider(_bgm, IntegrationSettingRefs.BgmVolume);
            BindSlider(_sfx, IntegrationSettingRefs.SfxVolume);

            _hints.onValueChanged.AddListener(OnHintsChanged);
            _hints.SetIsOnWithoutNotify(IntegrationSettingRefs.ShowHints.Value);

            _apply.onClick.AddListener(OnApply);
            _reset.onClick.AddListener(OnReset);
            _back.onClick.AddListener(OnBack);

            RefreshTexts();
            _status.text = string.Empty;
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override UniTask OnClose()
        {
            if (_master != null) _master.onValueChanged.RemoveListener(OnMasterChanged);
            if (_bgm != null) _bgm.onValueChanged.RemoveListener(OnBgmChanged);
            if (_sfx != null) _sfx.onValueChanged.RemoveListener(OnSfxChanged);
            if (_hints != null) _hints.onValueChanged.RemoveListener(OnHintsChanged);
            if (_apply != null) _apply.onClick.RemoveListener(OnApply);
            if (_reset != null) _reset.onClick.RemoveListener(OnReset);
            if (_back != null) _back.onClick.RemoveListener(OnBack);
            return UniTask.CompletedTask;
        }

        /// <inheritdoc/>
        protected override void OnLanguageChanged(string lang)
        {
            RefreshTexts();
        }

        #endregion

        #region Binding

        private void BindSlider(Slider slider, XFramework.XReactive.IReactiveProperty<float> source)
        {
            slider.SetValueWithoutNotify(source.Value);

            // 值句柄 → 滑条：随设置对象整体替换（Reset / Apply）自动重放。
            Track(source.Subscribe(v => slider.SetValueWithoutNotify(v)));
        }

        private void OnMasterChanged(float value) => IntegrationSettingRefs.MasterVolume.Value = value;
        private void OnBgmChanged(float value) => IntegrationSettingRefs.BgmVolume.Value = value;
        private void OnSfxChanged(float value) => IntegrationSettingRefs.SfxVolume.Value = value;
        private void OnHintsChanged(bool value) => IntegrationSettingRefs.ShowHints.Value = value;

        #endregion

        #region Actions

        private async void OnApply()
        {
            await SettingsManager.SaveAsync<IntegrationSettings>(destroyCancellationToken);
            MessageManager.Publish(new SettingsAppliedMessage(
                IntegrationSettingRefs.MasterVolume.Value, IntegrationSettingRefs.ShowHints.Value));
            _status.text = LocalizationManager.Get("settings.saved");
        }

        private void OnReset()
        {
            SettingsManager.Reset<IntegrationSettings>();
            _hints.SetIsOnWithoutNotify(IntegrationSettingRefs.ShowHints.Value);
            _status.text = LocalizationManager.Get("settings.reset_done");
        }

        private void OnBack()
        {
            UIManager.PopAsync().Forget();
        }

        #endregion

        #region Helpers

        private void RefreshTexts()
        {
            SetText(_title, LocalizationManager.Get("settings.title"));
            SetText(_masterLabel, LocalizationManager.Get("settings.master"));
            SetText(_bgmLabel, LocalizationManager.Get("settings.bgm"));
            SetText(_sfxLabel, LocalizationManager.Get("settings.sfx"));
            SetText(_apply, LocalizationManager.Get("settings.apply"));
            SetText(_reset, LocalizationManager.Get("settings.reset"));
            SetText(_back, LocalizationManager.Get("settings.back"));
        }

        private T Find<T>(string childName) where T : Component
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[Integration] SettingsPanel 缺少子节点 '{childName}'。");
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
