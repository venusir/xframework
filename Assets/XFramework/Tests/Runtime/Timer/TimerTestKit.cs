using System.Collections.Generic;
using XFramework.XUpdate;

namespace XFramework.XTimer.Tests
{
    /// <summary>
    /// 定时器侧用例的手动驱动器。
    /// <para><b>为什么不靠 PlayerLoop</b>：本模块的全部行为都建在「各轴 <c>deltaTime</c> 累加出的逻辑时钟」
    /// 上，而 <c>UpdateManager.Tick</c> 允许自带时刻，于是用例可以精确到帧地驱动、断言到帧地核对——
    /// 既不需要等待真实时间，也不会被宿主机器的帧率带偏。</para>
    /// <para>它同时被用作「当前时刻」的读数来源：回调在被派发的当拍执行，此刻 <see cref="ScaledTime"/>
    /// 正是该拍的时刻，于是记录型回调能给出逐拍精确的触发时刻。</para>
    /// </summary>
    internal sealed class TimerDriver
    {
        /// <summary>逻辑轴时刻。</summary>
        internal double ScaledTime { get; private set; }

        /// <summary>墙钟轴时刻。</summary>
        internal double UnscaledTime { get; private set; }

        /// <summary>每帧步长；默认 1/60 秒。跨多拍的用例会把它调大。</summary>
        internal float Step { get; set; } = 1f / 60f;

        /// <summary>推进一帧：两条轴都走。</summary>
        internal void Advance()
        {
            ScaledTime += Step;
            UnscaledTime += Step;
            UpdateManager.Tick(new UpdateClock(ScaledTime, UnscaledTime));
        }

        /// <summary>推进一帧，但逻辑轴冻结（等价于 <c>timeScale = 0</c>），墙钟轴照走。</summary>
        internal void AdvanceFrozen()
        {
            UnscaledTime += Step;
            UpdateManager.Tick(new UpdateClock(ScaledTime, UnscaledTime, isPaused: true));
        }

        /// <summary>连续推进指定帧数。</summary>
        /// <param name="frames">帧数。</param>
        internal void Advance(int frames)
        {
            for (int i = 0; i < frames; i++)
                Advance();
        }
    }

    /// <summary>
    /// 记录型回调：把每次被调用的<b>时刻</b>记下来。
    /// <para>「不漂移」这条承诺只有拿到逐拍时刻才验得了——只数次数的话，逐拍累加误差的写法同样能过。</para>
    /// </summary>
    internal sealed class TimerFires
    {
        private readonly TimerDriver _driver;
        private readonly List<double> _times = new List<double>();

        /// <summary>建一个记录器。</summary>
        /// <param name="driver">时刻读数来源。</param>
        internal TimerFires(TimerDriver driver)
        {
            _driver = driver;
        }

        /// <summary>已触发的次数。</summary>
        internal int Count => _times.Count;

        /// <summary>逐次触发的时刻。</summary>
        internal IReadOnlyList<double> Times => _times;

        /// <summary>记录一次触发。以方法组形式作为回调传给门面。</summary>
        internal void Record()
        {
            _times.Add(_driver.ScaledTime);
        }

        /// <summary>清空记录，供「前半段驱动、后半段只统新账」的用例使用。</summary>
        internal void Clear()
        {
            _times.Clear();
        }
    }

    /// <summary>
    /// 定时器侧用例的公共复位。
    /// <para><b>PlayMode 下所有用例共享一个 player 实例</b>，静态门面不复位即互相污染；本模块还额外持有
    /// 内部驱动器与槽位，复位顺序因此有硬要求：<b>先让 Timer 停净自己（它要经调度器注销驱动器），
    /// 再复位 Update</b>。反过来的话 Timer 会以为自己还注册着，而调度器里其实已经没有了。</para>
    /// </summary>
    internal static class TimerTestKit
    {
        /// <summary>用例开始前的复位：关闭自动驱动、重建调度器并清空。</summary>
        internal static void SetUp()
        {
            UpdateManager.AutoDriveEnabled = false;
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        /// <summary>用例结束后的复位：先停净定时器，再复位 Update。</summary>
        internal static void TearDown()
        {
            TimerManager.CancelAll();

            UpdateManager.AutoDriveEnabled = true;
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }
    }
}
