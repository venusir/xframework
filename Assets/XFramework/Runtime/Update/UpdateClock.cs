namespace XFramework.XUpdate
{
    /// <summary>
    /// 更新调度使用的时间轴。
    /// <para><b>枚举值即轴下标</b>：调度器按轴分桶，该值直接用作桶数组的一部分下标，
    /// 因此既有取值不可改动。</para>
    /// </summary>
    public enum UpdateTimeMode
    {
        /// <summary>受 <see cref="UnityEngine.Time.timeScale"/> 影响（逻辑时间）。</summary>
        Scaled = 0,

        /// <summary>
        /// 不受 <see cref="UnityEngine.Time.timeScale"/> 影响（墙钟时间）。
        /// <para>用于「暂停期间仍需运行」的逻辑：暂停菜单、UI 动画、手柄振动到期等。</para>
        /// </summary>
        Unscaled = 1,
    }

    /// <summary>
    /// 一帧的时间基。由驱动方构造后交给 <see cref="UpdateManager.Tick(UpdateClock)"/>。
    /// <para>两个时间源由外部成对传入，而不是让调度器自己去读 <see cref="UnityEngine.Time"/>：
    /// 这样调度器保持纯函数、可被单测精确驱动，也不会在一帧内先后读到不一致的瞬时值。</para>
    /// </summary>
    public readonly struct UpdateClock
    {
        /// <summary>逻辑时间（<see cref="UnityEngine.Time.time"/>），受 timeScale 影响。</summary>
        public readonly float Time;

        /// <summary>墙钟时间（<see cref="UnityEngine.Time.unscaledTime"/>），不受 timeScale 影响。</summary>
        public readonly float UnscaledTime;

        /// <summary>
        /// 构造时钟。
        /// </summary>
        /// <param name="time">逻辑时间。</param>
        /// <param name="unscaledTime">墙钟时间。</param>
        public UpdateClock(float time, float unscaledTime)
        {
            Time = time;
            UnscaledTime = unscaledTime;
        }

        /// <summary>
        /// 取指定时间轴对应的时刻。
        /// </summary>
        /// <param name="mode">时间轴。</param>
        public float GetTime(UpdateTimeMode mode)
        {
            return mode == UpdateTimeMode.Unscaled ? UnscaledTime : Time;
        }
    }
}
