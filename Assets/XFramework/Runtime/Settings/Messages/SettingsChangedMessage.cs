using System;

namespace XFramework.XSettings
{
    /// <summary>
    /// 设置变更消息。
    /// <para>当设置对象通过 <see cref="ISettingsManager{T}.Apply"/>、<see cref="ISettingsManager{T}.Load"/> 或
    /// <see cref="ISettingsManager{T}.Reset"/> 发生变更时，通过 <see cref="XReactive.MessageManager"/> 发布此消息。</para>
    /// <para>使用 <c>readonly struct</c> 避免堆分配，遵循项目的"避免 GC"规范。</para>
    /// </summary>
    public readonly struct SettingsChangedMessage
    {
        /// <summary>
        /// 发生变更的设置类型。
        /// <para>例如 <c>typeof(GameSettings)</c>。</para>
        /// </summary>
        public readonly Type SettingsType;

        /// <summary>
        /// 创建设置变更消息。
        /// </summary>
        /// <param name="settingsType">发生变更的设置类型。</param>
        public SettingsChangedMessage(Type settingsType)
        {
            SettingsType = settingsType;
        }
    }
}