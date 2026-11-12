using UnityEngine;
using XFramework.XSettings;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 设置校验钩子：把持久层里可能被手改 / 位翻转 / 旧版本写坏的值校正回合法范围。
    /// <para>由 <c>SettingsManager.SetValidator</c> 注册；在 <c>Load</c> / <c>Reset</c> / <c>Apply</c>
    /// 之后、订阅者被通知之前执行。校验器<b>不抛异常</b>——损坏的配置不该让游戏启动失败。</para>
    /// </summary>
    public sealed class IntegrationSettingsValidator : ISettingsValidator<IntegrationSettings>
    {
        /// <inheritdoc/>
        public void Validate(IntegrationSettings settings)
        {
            settings.MasterVolume = Mathf.Clamp01(settings.MasterVolume);
            settings.BgmVolume = Mathf.Clamp01(settings.BgmVolume);
            settings.SfxVolume = Mathf.Clamp01(settings.SfxVolume);

            if (string.IsNullOrEmpty(settings.Language))
                settings.Language = "zh_Hans";

            if (string.IsNullOrEmpty(settings.PlayerName))
                settings.PlayerName = "Player";
        }
    }
}
