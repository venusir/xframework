using System;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUpdate;

namespace XFramework.XTimer
{
    /// <summary>
    /// 一条时间轴一个的驱动器：把该轴收到的 <c>deltaTime</c> 灌进逻辑时钟、扫尾该轴的全部槽位表，
    /// 并按「最近一次截止」决定自己下一拍该落在哪一档。
    /// <para><b>它是本模块唯一接触 Update 的地方</b>，也是「暂停语义免费」的落点：定时器的截止时刻建在
    /// 这个由 <c>deltaTime</c> 累加出的逻辑时钟上，于是 <c>timeScale = 0</c> 或
    /// <c>UpdateManager.Pause()</c> 时该轴不派发、时钟自然冻结，恢复后从冻结点续上——
    /// 本模块没有一行暂停代码，也没有第二条时间口径。</para>
    /// <para><b>为何两条轴各要一个实例</b>：调度器内部按节点维护单值的「轴 × 档位」桶索引，同一个节点
    /// 无法同时挂在两条轴上。</para>
    /// <para><b>档位只由它自己声明</b>：注册时给一个初值，之后每拍由 <see cref="OnUpdate"/> 的返回值更新
    /// （Update 的档位机制就是这么用的）。于是「离截止远时降频、临近时自动升回 Tier0」不需要任何轮询，
    /// 也不需要本模块自己维护分桶。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>异常绝不允许逃出 <see cref="OnUpdate"/></b>：<c>UpdateScheduler</c> 会把抛出异常的节点
    /// 当作坏节点<b>永久注销</b>——那会表现为「该轴全部定时器静默死亡」，且没有恢复路径。故每个回调在
    /// 槽位表的扫尾里单独收口，这里再兜一层，保证任何遗漏也不会波及驱动器本身。</para>
    /// <para><b>空闲即退出调度</b>：该轴一个正在计时的定时器都没有时注销自己，让「框架装了但没用定时器」
    /// 的开销真正归零；下一次创建会重新注册。</para>
    /// </remarks>
    internal sealed class TimerTicker : IUpdateable
    {
        #region Private Fields

        private readonly TimerCore _core;
        private readonly int _axis;

        private bool _registered;

        /// <summary>注册之后是否真的被派发过。决定「换档位」该走哪条路，见 <see cref="RequestTier"/>。</summary>
        private bool _dispatched;

        /// <summary>本次 <see cref="OnUpdate"/> 是否由 <see cref="RequestTier"/> 的立即重估引发。</summary>
        private bool _inImmediate;

        /// <summary>上一次派发收到的时刻。<b>原样回传给 <c>ProcessImmediate</c></b>，见 <see cref="RequestTier"/>。</summary>
        private float _lastSeen;

        private UpdateTier _tier = UpdateTier.Tier0;

        #endregion

        #region Constructors

        /// <summary>建一个尚未注册的驱动器。</summary>
        /// <param name="core">所属核心。</param>
        /// <param name="axis">时间轴。</param>
        internal TimerTicker(TimerCore core, int axis)
        {
            _core = core;
            _axis = axis;
        }

        #endregion

        #region Internal — Registration

        /// <summary>该驱动器当前是否已注册进 Update。</summary>
        internal bool IsRegistered => _registered;

        /// <summary>该驱动器下一拍将要落在的档位。</summary>
        internal UpdateTier Tier => _tier;

        /// <summary>
        /// 请求把档位拉细到 <paramref name="needed"/>（比当前更粗的请求一律忽略——拉粗由每趟扫尾的返回值
        /// 决定，那里才知道真实的最近截止）。
        /// <para><b>为什么需要它</b>：扫尾里返回的档位要等到<b>下一拍</b>才生效。若此刻正停在粗档
        /// （比如为一条 60 秒定时器降到了 Tier7），新建一条 0.1 秒的定时器就得等两秒才会被第一次看到。
        /// 本方法把「重估档位」这件事提前到创建的那一刻。</para>
        /// <para><b>为什么用 <c>ProcessImmediate</c> 而不是重新 <c>Register</c></b>：重新注册会插入一条
        /// <c>NeedsAnchor = true</c> 的新条目，下一次派发的 <c>deltaTime</c> 记 0——等于每次创建都吞掉
        /// 一个档位周期的时间，高频创建时逻辑时钟会永久停摆、定时器永不触发。而 <c>ProcessImmediate</c>
        /// 只把时间基准写成传入的时刻，<b>原样传回上一次收到的时刻即可做到零漂移</b>。</para>
        /// <para><b>时钟为什么不能用 <c>Time.time</c></b>：那会把基准推到真实时刻，而逻辑时钟是按
        /// <c>deltaTime</c> 累积的，两者在暂停/时间缩放之后不再同源——传错会把整段暂停算成流逝时间。</para>
        /// <para><b>在别的回调里调用时不生效</b>：调度器的迭代闩锁下 <c>ProcessImmediate</c> 只重锚不派发，
        /// 于是跨轴创建更近的定时器最坏要等一个旧档周期（同轴不受影响，那趟扫尾的返回值本就涵盖它）。</para>
        /// </summary>
        /// <param name="needed">希望落到的档位。</param>
        internal void RequestTier(UpdateTier needed)
        {
            if (_registered && needed >= _tier)
                return;

            if (!_registered || !_dispatched)
            {
                // 还没被派发过：重新注册是安全的——注册会重置时间锚，而「还没派发过」意味着没有时间可丢
                _registered = true;
                _dispatched = false;
                _tier = needed;
                UpdateManager.Register(this, order: 0, initialTier: needed, timeMode: (UpdateTimeMode)_axis);
                return;
            }

            _inImmediate = true;
            try
            {
                UpdateManager.ProcessImmediate(this, 0f, new UpdateClock(_lastSeen, _lastSeen));
            }
            finally
            {
                _inImmediate = false;
            }
        }

        /// <summary>退出调度；重复调用是安全的。档位与时刻镜像一并复位，下次注册从头来过。</summary>
        internal void GoIdle()
        {
            if (!_registered)
                return;

            _registered = false;
            _dispatched = false;
            _tier = UpdateTier.Tier0;
            _lastSeen = 0f;
            UpdateManager.Unregister(this);
        }

        #endregion

        #region IUpdateable

        /// <inheritdoc />
        public void OnEnable()
        {
        }

        /// <inheritdoc />
        public void OnDisable()
        {
        }

        /// <inheritdoc />
        public UpdateTier OnUpdate(float deltaTime, float time)
        {
            _dispatched = true;
            _lastSeen = time;

            try
            {
                // ProcessImmediate 里的这一趟不是派发轮，不推进趟号：于是刚建好的槽位仍算「本趟出生」，
                // 只参与最近截止的估计、不会当场触发（见 TimerTable.Sweep）
                double min = _core.Sweep(_axis, deltaTime, advancePass: !_inImmediate);
                _tier = TimerCore.SelectTier(min);
            }
            catch (Exception e)
            {
                LogManager.Error(LogCategories.Timer, "定时器扫尾出现未预期异常，本拍已跳过：{0}", e);
            }

            return _tier;
        }

        #endregion
    }
}
