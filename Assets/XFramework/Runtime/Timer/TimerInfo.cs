using XFramework.XUpdate;

namespace XFramework.XTimer
{
    /// <summary>
    /// 一个计时器槽位的状态（诊断用只读快照）。
    /// <para><b>回答什么问题</b>：现在有哪些定时器在跑、各自还有多久、回调是谁——「定时器没触发」
    /// 「回调没执行」这类问题只能靠翻这张表定位。</para>
    /// <para><b>怎么取</b>：<see cref="TimerManager.CopyActiveTimers(System.Collections.Generic.List{TimerInfo})"/>。
    /// 它列出**所有占用槽位**的定时器，含已 <c>Stop</c> 但未释放的那批（<see cref="IsRunning"/> 为 false）——
    /// 那正是「停掉的定时器还占着槽位」这种泄漏的唯一可见面。</para>
    /// </summary>
    public readonly struct TimerInfo
    {
        /// <summary>所在时间轴（受时间缩放影响 / 不受影响）。</summary>
        public readonly UpdateTimeMode TimeMode;

        /// <summary>槽位下标。<b>只作诊断标识</b>——槽位会被复用，它不等于定时器身份。</summary>
        public readonly int Slot;

        /// <summary>剩余秒数；已停或已到期时为 0。</summary>
        public readonly float Remaining;

        /// <summary>时长（延时或间隔）。</summary>
        public readonly float Duration;

        /// <summary>是否是固定间隔的重复定时器。</summary>
        public readonly bool IsRepeating;

        /// <summary>是否正在计时；false = 已 <c>Stop</c> 但槽位尚未释放。</summary>
        public readonly bool IsRunning;

        /// <summary>
        /// 回调的短名（<c>类型名.方法名</c>）。
        /// <para><b>lambda 与局部函数会是编译器生成的名字</b>（如 <c>MyClass.&lt;&gt;c__DisplayClass3_0</c>），
        /// 这是闭包没有人类可读名字的必然结果——它是「同一处代码注册的定时器」的线索，不是方法名的承诺。</para>
        /// </summary>
        public readonly string CallbackName;

        internal TimerInfo(UpdateTimeMode timeMode, int slot, float remaining, float duration,
            bool isRepeating, bool isRunning, string callbackName)
        {
            TimeMode = timeMode;
            Slot = slot;
            Remaining = remaining;
            Duration = duration;
            IsRepeating = isRepeating;
            IsRunning = isRunning;
            CallbackName = callbackName;
        }

        /// <summary>单行摘要，形如 <c>#3 Scaled 剩 1.20s / 2.00s → Foo.Bar</c>。</summary>
        public override string ToString()
            => $"#{Slot} {TimeMode} 剩 {Remaining:F2}s / {Duration:F2}s → {CallbackName}";
    }
}
