using System;

namespace XFramework.XSettings
{
    /// <summary>
    /// 持久化信封：把格式版本与设置载荷一起落盘，仅在启用版本化（
    /// <see cref="SettingsOptions.CurrentVersion"/> 大于 0）时使用。
    /// </summary>
    /// <remarks>
    /// <para>放在管理器层而不是存储层，是为了让版本能力对<b>任意</b>
    /// <see cref="ISettingsStore"/> 实现都成立——存储接口只有泛型的 Load/Save，
    /// 没有版本通道；若把版本处理下推进 store，每个第三方 store 都得自己实现一遍。</para>
    /// <para>副作用是落盘 JSON 多一层嵌套，这正是版本化默认关闭的原因。</para>
    /// </remarks>
    /// <typeparam name="T">设置对象类型。</typeparam>
    [Serializable]
    internal sealed class SettingsEnvelope<T>
    {
        /// <summary>落盘时的格式版本。</summary>
        public int Version;

        /// <summary>设置载荷。</summary>
        public T Data;
    }
}
