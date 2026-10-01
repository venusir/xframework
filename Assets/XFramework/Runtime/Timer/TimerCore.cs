using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUpdate;

namespace XFramework.XTimer
{
    /// <summary>
    /// 定时器引擎。持有两条时间轴各自的逻辑时钟、槽位表集合与驱动器，负责创建、查询、停表、重开、释放与全停。
    /// <para><b>对门面不透明</b>：句柄的全部查询与控制都经 <see cref="TimerManager"/> 转发到这里，
    /// 于是分表方式、槽位复用与代际规则可以继续演进而不构成破坏性变更。</para>
    /// <para><b>时间口径完全来自 <c>XUpdate</c></b>：一切截止时刻都建在「各轴 <c>deltaTime</c> 累加出的
    /// 逻辑时钟」上，本类<b>不读 <c>UnityEngine.Time</c></b>——暂停与时间缩放因此完全由 Update 的双时间轴
    /// 决定，本模块不新建时间模型（撞不上框架的第一条非目标）。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>为什么按 (时间轴, 状态类型) 分表</b>：<c>Action&lt;TState&gt;</c> 无法转成
    /// <c>Action&lt;object&gt;</c>，若用 <c>object</c> 存状态，值类型状态会在每次创建时装箱——那会让
    /// 「零闭包重载」的承诺对值类型载荷失效。分表让值类型状态也零装箱，代价只是带状态的那条创建路径多一次
    /// 字典查找（无状态路径由字段直连，不查字典）。被否决的简化形态见 <c>Documentation/Modules/Timer.md</c>。</para>
    /// <para><b>表标识跨核心唯一</b>：<see cref="TimerManager.AutoInit"/> 会整个换掉核心，而使用方手上的
    /// 旧句柄还带着旧标识。标识取全局单调计数而非「核心内下标」，旧句柄在新核心里必然落空——
    /// 这正是「换核心后旧句柄安全失效」不需要额外登记表的依据。</para>
    /// </remarks>
    internal sealed class TimerCore
    {
        #region Constants

        /// <summary>时间轴数量（<see cref="UpdateTimeMode"/> 的取值个数）。</summary>
        private const int AxisCount = 2;

        #endregion

        #region Constants — Tier

        /// <summary>
        /// 变步长轴的节拍基准（Hz）。<b>必须与 <c>UpdateScheduler.TickPeriod</c> 一致</b>——后者是
        /// <c>internal const</c>、跨模块读不到（模块边界不许引用别的模块的 Internal 命名空间），
        /// 故此处是一份<b>副本</b>，由 <c>TimerTierTableTests</c> 对着 Update 的<b>真实派发节奏</b>锁定：
        /// 任一侧漂移都会红。
        /// </summary>
        internal const int TicksPerSecond = 60;

        /// <summary>
        /// 选档的安全系数：要求「档位周期 × 本系数 ≤ 剩余」。
        /// <para>于是最坏相对超时 = 1/8 = 12.5%，且截止前至少还有 8 次派发机会——足以吸收低帧率下
        /// Update 的周期拉长（帧长超过 50ms 时每档周期会随帧率线性变长）。取 4 太薄（一次卡顿就吃掉
        /// 半个余量），取 16 则把小定时器全压回 <see cref="UpdateTier.Tier0"/>、省不出开销。</para>
        /// </summary>
        private const double TierSafetyFactor = 8d;

        #endregion

        #region Static Fields

        /// <summary>表标识发号器。<b>跨核心共享</b>，见类型注释。</summary>
        private static int _nextTableId;

        #endregion

        #region Private Fields

        /// <summary>免字典的槽位表：无状态重载直接落在这里。</summary>
        private readonly TimerTable<NoState>[] _plainTables;

        /// <summary>带状态的回调按类型分表，此字典只承担「找表」。</summary>
        private readonly Dictionary<(int Axis, Type State), ITimerTable> _stateTables = new();

        /// <summary>全部表（两张无状态 + 若干带状态），供扫尾与全停遍历。</summary>
        private readonly List<ITimerTable> _allTables = new();

        private readonly TimerTicker[] _tickers;

        /// <summary>各轴的逻辑时钟：只由该轴收到的 <c>deltaTime</c> 推进，因此暂停时自然冻结。</summary>
        private readonly double[] _logicalNow = new double[AxisCount];

        #endregion

        #region Constructors

        /// <summary>建一个空核心。表与驱动器都是惰性的，除两张免字典表外不预分配任何东西。</summary>
        internal TimerCore()
        {
            _plainTables = new TimerTable<NoState>[AxisCount];
            _tickers = new TimerTicker[AxisCount];

            for (int axis = 0; axis < AxisCount; axis++)
            {
                var plain = new TimerTable<NoState>(axis, NextTableId());
                _plainTables[axis] = plain;
                _allTables.Add(plain);
                _tickers[axis] = new TimerTicker(this, axis);
            }
        }

        #endregion

        #region Internal — Create

        /// <summary>创建一个无状态定时器。</summary>
        /// <param name="duration">延时（一次性）或间隔（重复）秒数。</param>
        /// <param name="callback">到期回调。</param>
        /// <param name="timeMode">时间轴。</param>
        /// <param name="cancellationToken">取消令牌；<c>default</c> 表示不受令牌约束。</param>
        /// <param name="repeating">是否为固定间隔定时器。</param>
        /// <returns>定时器句柄；创建时令牌已被取消时返回 <c>default(TimerHandle)</c>。</returns>
        internal TimerHandle Create(float duration, Action callback, UpdateTimeMode timeMode,
            CancellationToken cancellationToken, bool repeating)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            int axis = ValidateTimeMode(timeMode);
            ValidateDuration(duration, repeating);

            if (cancellationToken.IsCancellationRequested)
            {
                LogManager.Warning(LogCategories.Timer, "取消令牌在创建前已被取消，本次创建被忽略并返回默认句柄。");
                return default;
            }

            TimerHandle handle = _plainTables[axis].Allocate(
                callback, null, default, duration, repeating, cancellationToken, _logicalNow[axis]);

            // 先分配再重估档位：那一趟扫尾要看得见这条新定时器，否则算出来的还是旧档位
            _tickers[axis].RequestTier(SelectTier(duration));
            return handle;
        }

        /// <summary>创建一个带状态定时器（状态随槽位存储，值类型不装箱）。</summary>
        /// <typeparam name="TState">状态类型。</typeparam>
        /// <param name="duration">延时（一次性）或间隔（重复）秒数。</param>
        /// <param name="callback">到期回调。</param>
        /// <param name="state">回调收到的状态。</param>
        /// <param name="timeMode">时间轴。</param>
        /// <param name="cancellationToken">取消令牌；<c>default</c> 表示不受令牌约束。</param>
        /// <param name="repeating">是否为固定间隔定时器。</param>
        /// <returns>定时器句柄；创建时令牌已被取消时返回 <c>default(TimerHandle)</c>。</returns>
        internal TimerHandle Create<TState>(float duration, Action<TState> callback, TState state,
            UpdateTimeMode timeMode, CancellationToken cancellationToken, bool repeating)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            int axis = ValidateTimeMode(timeMode);
            ValidateDuration(duration, repeating);

            if (cancellationToken.IsCancellationRequested)
            {
                LogManager.Warning(LogCategories.Timer, "取消令牌在创建前已被取消，本次创建被忽略并返回默认句柄。");
                return default;
            }

            TimerHandle handle = StateTable<TState>(axis).Allocate(
                null, callback, state, duration, repeating, cancellationToken, _logicalNow[axis]);

            _tickers[axis].RequestTier(SelectTier(duration));
            return handle;
        }

        #endregion

        #region Internal — Handle Operations

        /// <summary>句柄当前是否仍在计时（令牌已取消即算不在）。</summary>
        /// <param name="handle">定时器句柄。</param>
        internal bool IsActive(in TimerHandle handle)
        {
            ITimerTable table = Resolve(in handle);
            return table != null && table.IsActive(handle.Slot, handle.Generation);
        }

        /// <summary>距下一拍触发的剩余秒数；无效/已停/已取消时为 <c>0</c>。</summary>
        /// <param name="handle">定时器句柄。</param>
        internal float Remaining(in TimerHandle handle)
        {
            ITimerTable table = Resolve(in handle);
            if (table == null)
                return 0f;

            return table.Remaining(handle.Slot, handle.Generation, _logicalNow[table.Axis]);
        }

        /// <summary>停表但保留槽位。</summary>
        /// <param name="handle">定时器句柄。</param>
        internal bool Stop(in TimerHandle handle)
        {
            ITimerTable table = Resolve(in handle);
            if (table == null || !table.Stop(handle.Slot, handle.Generation))
                return false;

            MaybeGoIdle(table.Axis);
            return true;
        }

        /// <summary>用原时长重新起算。</summary>
        /// <param name="handle">定时器句柄。</param>
        internal bool Restart(in TimerHandle handle)
        {
            ITimerTable table = Resolve(in handle);
            if (table == null || !table.Restart(handle.Slot, handle.Generation, _logicalNow[table.Axis]))
                return false;

            _tickers[table.Axis].RequestTier(SelectTier(table.DurationOf(handle.Slot, handle.Generation)));
            return true;
        }

        /// <summary>停表并释放槽位。</summary>
        /// <param name="handle">定时器句柄。</param>
        internal void Release(in TimerHandle handle)
        {
            ITimerTable table = Resolve(in handle);
            if (table == null || !table.Release(handle.Slot, handle.Generation))
                return;

            MaybeGoIdle(table.Axis);
        }

        #endregion

        #region Internal — Lifecycle

        /// <summary>两条轴上正在计时的定时器数量之和。</summary>
        internal int ActiveCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _allTables.Count; i++)
                    total += _allTables[i].RunningCount;

                return total;
            }
        }

        /// <summary>停净全部定时器（含已停止未释放的），并让两个驱动器退出调度。</summary>
        /// <returns>被取消的定时器数量。</returns>
        internal int CancelAll()
        {
            int cancelled = 0;

            for (int i = 0; i < _allTables.Count; i++)
                cancelled += _allTables[i].CancelAll();

            for (int axis = 0; axis < AxisCount; axis++)
                _tickers[axis].GoIdle();

            return cancelled;
        }

        #endregion

        #region Internal — Drive

        /// <summary>
        /// 推进逻辑时钟并扫尾该轴的全部表，由该轴的 <see cref="TimerTicker"/> 每拍调用一次。
        /// </summary>
        /// <param name="axis">时间轴。</param>
        /// <param name="deltaTime">该轴本拍的真实间隔；<c>ProcessImmediate</c> 引发的那一趟传 0。</param>
        /// <param name="advancePass">本次是否是一轮真正的派发（<c>ProcessImmediate</c> 那趟为 <c>false</c>）。</param>
        /// <returns>该轴最近一次截止的剩余秒数；没有正在计时的定时器时为 <see cref="double.PositiveInfinity"/>。</returns>
        internal double Sweep(int axis, float deltaTime, bool advancePass)
        {
            _logicalNow[axis] += deltaTime;
            double now = _logicalNow[axis];

            double min = double.PositiveInfinity;

            // 每轮重读 Count：回调里创建带状态定时器会新增一张表
            for (int i = 0; i < _allTables.Count; i++)
            {
                ITimerTable table = _allTables[i];
                if (table.Axis != axis)
                    continue;

                double candidate = table.Sweep(now, advancePass);
                if (candidate < min)
                    min = candidate;
            }

            MaybeGoIdle(axis);
            return min;
        }

        /// <summary>
        /// 按「最近一次截止的剩余秒数」选档：取最大的 k ∈ <c>[1, Tier7]</c> 使
        /// <c>2^k / 60 × 8 ≤ remaining</c>，都不满足则回到 <see cref="UpdateTier.Tier0"/>。
        /// <para>实测曲线（单轴、最近截止为 D）：0.1 秒 → Tier0、0.5 秒 → Tier1、1 秒 → Tier2、
        /// 2 秒 → Tier3、5 秒 → Tier5、10 秒 → Tier6、60 秒 → Tier7。0.1 秒以下一律 Tier0 是刻意的：
        /// 小冷却对精度最敏感，而降频收益最小。</para>
        /// <para><b>不漂移与不晚点</b>：选档要求「周期 ≤ 剩余 / 8」，而档位在下一拍生效——于是每一拍
        /// 醒来时距截止至少还有一个周期，永远不会跨过截止才醒。真正触发的那一拍必定落在 Tier0 附近。</para>
        /// </summary>
        /// <param name="minRemaining">最近一次截止的剩余秒数；无活跃定时器时为
        /// <see cref="double.PositiveInfinity"/>。</param>
        internal static UpdateTier SelectTier(double minRemaining)
        {
            if (double.IsPositiveInfinity(minRemaining))
                return UpdateTier.Tier0;

            for (int k = (int)UpdateTier.Max; k >= 1; k--)
            {
                if (minRemaining >= TierPeriodOf((UpdateTier)k) * TierSafetyFactor)
                    return (UpdateTier)k;
            }

            return UpdateTier.Tier0;
        }

        /// <summary>
        /// 第 k 档的标称周期（秒）= 2^k 个节拍格。<b>只对 k ≥ 1 有意义</b>——<see cref="UpdateTier.Tier0"/>
        /// 是每帧，周期由帧率决定，不在这张表里。
        /// <para>用公式而不是字面量：字面量会引入「抄错一位」这条第二条漂移轴，公式只有一个可漂移点
        /// （<see cref="TicksPerSecond"/>），而锁定它的集成测试直接对着 Update 的实际派发节奏断言。</para>
        /// </summary>
        /// <param name="tier">档位。</param>
        internal static double TierPeriodOf(UpdateTier tier)
        {
            return (1 << (int)tier) / (double)TicksPerSecond;
        }

        /// <summary>该轴的驱动器。供测试断言注册状态使用。</summary>
        /// <param name="axis">时间轴。</param>
        /// <param name="axis">时间轴。</param>
        internal TimerTicker TickerOf(int axis)
        {
            return _tickers[axis];
        }

        #endregion

        #region Private — Helpers

        /// <summary>按表标识找表；换核心后的旧句柄会在这里落空并返回 <c>null</c>。</summary>
        /// <param name="handle">定时器句柄。</param>
        private ITimerTable Resolve(in TimerHandle handle)
        {
            for (int i = 0; i < _allTables.Count; i++)
            {
                if (_allTables[i].TableId == handle.TableId)
                    return _allTables[i];
            }

            return null;
        }

        /// <summary>取（必要时新建）某个状态类型在该轴上的表。</summary>
        /// <typeparam name="TState">状态类型。</typeparam>
        /// <param name="axis">时间轴。</param>
        private TimerTable<TState> StateTable<TState>(int axis)
        {
            var key = (axis, typeof(TState));

            if (_stateTables.TryGetValue(key, out ITimerTable existing))
                return (TimerTable<TState>)existing;

            var table = new TimerTable<TState>(axis, NextTableId());
            _stateTables.Add(key, table);
            _allTables.Add(table);
            return table;
        }

        /// <summary>该轴一个正在计时的定时器都没有时，让驱动器退出调度（下一次创建会重新注册）。</summary>
        /// <param name="axis">时间轴。</param>
        private void MaybeGoIdle(int axis)
        {
            for (int i = 0; i < _allTables.Count; i++)
            {
                if (_allTables[i].Axis == axis && _allTables[i].RunningCount > 0)
                    return;
            }

            _tickers[axis].GoIdle();
        }

        /// <summary>校验时间轴取值，返回轴下标。</summary>
        /// <param name="timeMode">时间轴。</param>
        private static int ValidateTimeMode(UpdateTimeMode timeMode)
        {
            int axis = (int)timeMode;

            if (axis < 0 || axis >= AxisCount)
            {
                throw new ArgumentOutOfRangeException(nameof(timeMode), timeMode,
                    "[Timer] 时间轴取值无效。请传 UpdateTimeMode.Scaled 或 UpdateTimeMode.Unscaled。");
            }

            return axis;
        }

        /// <summary>校验时长：必须有限；一次性允许为 0，重复必须为正。</summary>
        /// <param name="duration">延时或间隔。</param>
        /// <param name="repeating">是否为固定间隔定时器。</param>
        private static void ValidateDuration(float duration, bool repeating)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration))
            {
                throw new ArgumentOutOfRangeException(repeating ? "interval" : "delay", duration,
                    "[Timer] 时长必须是有限值。");
            }

            if (repeating ? duration <= 0f : duration < 0f)
            {
                throw new ArgumentOutOfRangeException(repeating ? "interval" : "delay", duration,
                    repeating ? "[Timer] 间隔必须大于 0。" : "[Timer] 延时不能为负。");
            }
        }

        /// <summary>发一个表标识；跨核心单调递增，保证换核心后的旧句柄必然落空。</summary>
        private static int NextTableId()
        {
            return ++_nextTableId;
        }

        #endregion
    }
}
