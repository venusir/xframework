using System;
using XFramework.XSettings;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 设置字段句柄的静态缓存。
    /// <para>句柄把 POCO 字段包装成 <see cref="IReactiveProperty{T}"/>，既可直接订阅，也可喂给
    /// UI 绑定；设置对象被 <c>Load</c> / <c>Reset</c> / <c>Apply</c> 整体替换时句柄自动跟随。</para>
    /// <para><b>必须在 <c>SettingsManager.Initialize</c> 之后创建</b>：句柄的读写都经门面取当前实例。
    /// 本示例在 <see cref="GameSettingsBootstrapStage"/> 里初始化完之后调用 <see cref="EnsureCreated"/>。</para>
    /// </summary>
    public static class IntegrationSettingRefs
    {
        #region Private Fields

        private static SettingRef<IntegrationSettings, float> _masterVolume;
        private static SettingRef<IntegrationSettings, float> _bgmVolume;
        private static SettingRef<IntegrationSettings, float> _sfxVolume;
        private static SettingRef<IntegrationSettings, bool> _showHints;
        private static SettingRef<IntegrationSettings, string> _language;
        private static SettingRef<IntegrationSettings, string> _playerName;

        #endregion

        #region Public API

        /// <summary>主音量句柄。</summary>
        public static SettingRef<IntegrationSettings, float> MasterVolume => Require(_masterVolume);

        /// <summary>BGM 音量句柄。</summary>
        public static SettingRef<IntegrationSettings, float> BgmVolume => Require(_bgmVolume);

        /// <summary>音效音量句柄。</summary>
        public static SettingRef<IntegrationSettings, float> SfxVolume => Require(_sfxVolume);

        /// <summary>提示开关句柄。</summary>
        public static SettingRef<IntegrationSettings, bool> ShowHints => Require(_showHints);

        /// <summary>语言句柄。</summary>
        public static SettingRef<IntegrationSettings, string> Language => Require(_language);

        /// <summary>玩家名句柄。</summary>
        public static SettingRef<IntegrationSettings, string> PlayerName => Require(_playerName);

        /// <summary>
        /// 创建设置句柄（幂等）。<b>只能在 <c>SettingsManager.Initialize&lt;IntegrationSettings&gt;</c> 之后调用。</b>
        /// </summary>
        public static void EnsureCreated()
        {
            if (_masterVolume != null)
                return;

            _masterVolume = SettingsManager.Ref<IntegrationSettings, float>(s => s.MasterVolume);
            _bgmVolume = SettingsManager.Ref<IntegrationSettings, float>(s => s.BgmVolume);
            _sfxVolume = SettingsManager.Ref<IntegrationSettings, float>(s => s.SfxVolume);
            _showHints = SettingsManager.Ref<IntegrationSettings, bool>(s => s.ShowHints);
            _language = SettingsManager.Ref<IntegrationSettings, string>(s => s.Language);
            _playerName = SettingsManager.Ref<IntegrationSettings, string>(s => s.PlayerName);
        }

        #endregion

        #region Internal

        private static SettingRef<IntegrationSettings, T> Require<T>(SettingRef<IntegrationSettings, T> handle)
        {
            if (handle == null)
            {
                throw new InvalidOperationException(
                    "[Integration] 设置句柄尚未创建。请先完成启动引导（GameSettingsBootstrapStage），" +
                    "或显式调用 IntegrationSettingRefs.EnsureCreated()。");
            }

            return handle;
        }

        #endregion
    }
}
