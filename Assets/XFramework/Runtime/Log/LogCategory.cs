using System;
using XFramework.XLog.Internal;

namespace XFramework.XLog
{
    /// <summary>
    /// 日志分类句柄。<c>[模块]</c> 前缀的唯一真相——控制台里渲染成 <c>[Name]</c>，不再靠调用点手抄。
    /// <para><b>获取方式</b>：框架内置分类见 <see cref="LogCategories"/>；第三方自定义用
    /// <see cref="Get(string)"/>（幂等，同名返回同一状态对象，任意线程安全）。</para>
    /// <para><b>它是 readonly struct 且可安全复制</b>：档位存在被引用的状态对象里，
    /// <c>SetCategoryLevel</c> 对所有已复制出去的句柄立即生效——不存在「副本还拿着旧档位」。</para>
    /// <para><b><c>default(LogCategory)</c> 合法且永不抛</b>：呈现为 <c>[Unregistered]</c>、命中全局档位，
    /// 作为「忘记注册」的可见信号。把它传给 <c>SetCategoryLevel</c> 则会抛 <see cref="ArgumentException"/>
    /// ——那是配置错误，静默忽略只会让问题更难查。</para>
    /// </summary>
    public readonly struct LogCategory : IEquatable<LogCategory>
    {
        #region Private Fields

        /// <summary>未注册分类的呈现名。</summary>
        internal const string UnregisteredName = "Unregistered";

        private readonly LogCategoryState _state;

        #endregion

        #region Internal API

        internal LogCategory(LogCategoryState state)
        {
            _state = state;
        }

        /// <summary>内部状态对象；<c>default</c> 时为 null。值比较即引用比较的依据。</summary>
        internal LogCategoryState State => _state;

        /// <summary>档位覆盖；未注册或未设置时为 <see cref="LogCategoryState.FollowGlobal"/>。</summary>
        internal int LevelOverride => _state != null ? _state.LevelOverride : LogCategoryState.FollowGlobal;

        #endregion

        #region Public API

        /// <summary>分类名（<b>不含</b>方括号，如 <c>Save</c>）；未注册时为 <c>"Unregistered"</c>。</summary>
        public string Name => _state != null ? _state.Name : UnregisteredName;

        /// <summary>是否为已注册分类；<c>default</c> 为 <c>false</c>。</summary>
        public bool IsValid => _state != null;

        /// <summary>已注册的分类数（含框架内置）。诊断用。</summary>
        public static int RegisteredCount => LogRegistry.Count;

        /// <summary>
        /// 按名称取得（或创建）分类。大小写敏感、按序号比较，任意线程安全。
        /// </summary>
        /// <param name="name">分类名，不含方括号（如 <c>"MyGame"</c>）。</param>
        /// <returns>分类句柄；<paramref name="name"/> 为 null 或空白时返回 <c>default</c>。</returns>
        public static LogCategory Get(string name)
        {
            LogCategoryState state = LogRegistry.GetOrCreate(name);
            return state != null ? new LogCategory(state) : default;
        }

        #endregion

        #region Equality

        /// <summary>按状态对象引用比较——同名分类全局同一个状态对象，故等价于「同名」。</summary>
        /// <param name="other">另一个分类句柄。</param>
        /// <returns>两者是否指向同一分类。</returns>
        public bool Equals(LogCategory other)
        {
            return ReferenceEquals(_state, other._state);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is LogCategory other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return _state != null ? _state.GetHashCode() : 0;
        }

        /// <summary>返回不含方括号的分类名。</summary>
        /// <returns>分类名。</returns>
        public override string ToString()
        {
            return Name;
        }

        /// <summary>相等运算符。</summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>是否相等。</returns>
        public static bool operator ==(LogCategory left, LogCategory right)
        {
            return left.Equals(right);
        }

        /// <summary>不等运算符。</summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>是否不等。</returns>
        public static bool operator !=(LogCategory left, LogCategory right)
        {
            return !left.Equals(right);
        }

        #endregion
    }
}
