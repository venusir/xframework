using System;

namespace XFramework.XAudio
{
    /// <summary>
    /// 一个 provider 中立的音频参数（数值或标签）。
    /// <para>起播前的参数通过 <see cref="AudioPlayOptions.InitialParameters"/> 传入；运行中与全局参数
    /// 通过 <c>AudioManager.SetParameter</c> / <c>AudioManager.SetGlobalParameter</c> 等门面方法写入。</para>
    /// <para><b>本类型只是数据载体</b>：核心不解释参数含义，由 provider 决定如何应用。内置 provider
    /// 忽略参数；音频中间件 provider 应在事件 <c>start()</c> 前应用起播前参数。</para>
    /// <para><b>代际与默认值</b>：<c>default(AudioParameter)</c> 的 <see cref="Name"/> 为 <c>null</c>，
    /// 视为无效；核心在播放入口校验并拒绝它。</para>
    /// </summary>
    public readonly struct AudioParameter
    {
        #region Public Fields

        /// <summary>参数名；不能为 <c>null</c>/空白。</summary>
        public readonly string Name;

        /// <summary>数值参数的值；标签参数为 <c>0</c>。</summary>
        public readonly float Value;

        /// <summary>标签参数的值；数值参数为 <c>null</c>。</summary>
        public readonly string Label;

        #endregion

        #region Public Properties

        /// <summary>是否是标签参数（<see cref="Label"/> 非 <c>null</c>）。</summary>
        public bool IsLabel => Label != null;

        #endregion

        #region Constructors

        /// <summary>
        /// 构造一个数值参数。
        /// </summary>
        /// <param name="name">参数名，不能为 <c>null</c>/空白。</param>
        /// <param name="value">参数值。</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> 为 <c>null</c>/空白时抛出。</exception>
        public AudioParameter(string name, float value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("[Audio] 参数名不能为 null 或空白。", nameof(name));

            Name = name;
            Value = value;
            Label = null;
        }

        /// <summary>
        /// 构造一个标签参数。
        /// </summary>
        /// <param name="name">参数名，不能为 <c>null</c>/空白。</param>
        /// <param name="label">标签值，不能为 <c>null</c>/空白。</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> 或 <paramref name="label"/> 为
        /// <c>null</c>/空白时抛出。</exception>
        public AudioParameter(string name, string label)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("[Audio] 参数名不能为 null 或空白。", nameof(name));
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("[Audio] 参数标签不能为 null 或空白。", nameof(label));

            Name = name;
            Value = 0f;
            Label = label;
        }

        #endregion
    }
}
