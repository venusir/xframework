using System;

namespace XFramework.XSettings
{
    /// <summary>
    /// 一项已注册的设置类型（诊断用只读快照）。
    /// <para><b>回答什么问题</b>：这个项目注册了哪些设置类型、谁还挂着未提交的改动。
    /// 此前只能按已知类型问 <c>IsRegistered&lt;T&gt;()</c> / <c>IsDirty&lt;T&gt;()</c>——不知道类型就问不出来。</para>
    /// <para><b>怎么取</b>：<see cref="SettingsManager.CopyRegisteredTypes(System.Collections.Generic.List{SettingsTypeInfo})"/>。</para>
    /// <para><b><see cref="IsDirty"/> 的口径</b>与 <c>ISettingsManager&lt;T&gt;.IsDirty</c> 逐字一致：
    /// 它表示「有改动尚未提交给存储后端」，<b>不等于已落盘</b>。</para>
    /// </summary>
    public readonly struct SettingsTypeInfo
    {
        /// <summary>设置类型。</summary>
        public readonly Type SettingsType;

        /// <summary>是否有未提交的改动（语义见 <c>ISettingsManager&lt;T&gt;.IsDirty</c>）。</summary>
        public readonly bool IsDirty;

        internal SettingsTypeInfo(Type settingsType, bool isDirty)
        {
            SettingsType = settingsType;
            IsDirty = isDirty;
        }

        /// <summary>单行摘要，形如 <c>GameSettings(脏)</c>。</summary>
        public override string ToString()
            => IsDirty ? $"{SettingsType?.Name}(脏)" : SettingsType?.Name;
    }
}
