using XFramework.XSettings;

namespace XFramework.Samples.Integration
{
    /// <summary>
    /// 设置格式迁移钩子（0 → 1）。
    /// <para>示例把 <c>SettingsOptions.CurrentVersion</c> 设为 1，因此落盘是版本信封；
    /// 读到旧版本数据时由本类就地补齐新增字段的默认值。迁移在订阅者被通知之前执行。</para>
    /// </summary>
    public sealed class IntegrationSettingsMigrator : ISettingsMigrator<IntegrationSettings>
    {
        /// <inheritdoc/>
        public void Migrate(int fromVersion, int toVersion, IntegrationSettings settings)
        {
            if (fromVersion < 1)
            {
                // 版本 0 没有 PlayerName 字段：JsonUtility 会填 null，这里补默认值。
                if (string.IsNullOrEmpty(settings.PlayerName))
                    settings.PlayerName = "Player";
            }
        }
    }
}
