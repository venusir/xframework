using System.Collections.Generic;
using System;
using System.Threading;
using UnityEngine;
using XFramework.XLog;
using XFramework.XUpdate;

namespace XFramework.XTimer
{
    /// <summary>
    /// 无状态重载占用的表类型参数。表按状态类型分开，此类型是「没有状态」那一格。
    /// </summary>
    internal struct NoState
    {
    }

    /// <summary>
    /// 一张槽位表的最小操作面，让 <see cref="TimerCore"/> 不必知道状态类型。
    /// <para>句柄操作都以「槽位下标 + 代际」寻址：<b>两者必须同时相符</b>才算命中——
    /// 槽位会被复用，只看下标会让早已释放的旧句柄操作到别人的定时器上。</para>
    /// </summary>
    internal interface ITimerTable
    {
        /// <summary>表标识。跨核心唯一，用于让换核心后的旧句柄必然落空。</summary>
        int TableId { get; }

        /// <summary>本表所在的时间轴（<see cref="UpdateTimeMode"/> 的取值）。</summary>
        int Axis { get; }

        /// <summary>
        /// 把本表占用槽位的明细**追加**到缓冲区（不清空），返回追加条数。诊断回读用。
        /// <para>含已 <c>Stop</c> 但未释放的槽位（<see cref="TimerInfo.IsRunning"/> 为 false）——
        /// 只看正在计时的那些会把「停掉的定时器仍占着槽位」这类泄漏藏起来。</para>
        /// </summary>
        /// <param name="buffer">接收结果的缓冲区；由调用方负责清空（多表共用一份）。</param>
        /// <param name="now">本轴当前的逻辑时刻。</param>
        /// <returns>追加的条数。</returns>
        int CopyActiveSlots(System.Collections.Generic.List<TimerInfo> buffer, double now);

        /// <summary>正在计时的槽位数（<b>含令牌已取消但尚未被扫尾回收的</b>）。</summary>
        int RunningCount { get; }

        /// <summary>
        /// 扫尾一趟：回收已取消的槽位、触发到期的定时器。
        /// </summary>
        /// <param name="now">本轴当前的逻辑时刻。</param>
        /// <param name="advancePass">本次是否是一个新的「派发轮」。</param>
        /// <returns>本表内最近一次截止的剩余秒数；没有正在计时的定时器时返回
        /// <see cref="double.PositiveInfinity"/>（档位选择据此回到 <see cref="UpdateTier.Tier0"/>）。</returns>
        double Sweep(double now, bool advancePass);

        /// <summary>释放本表全部槽位。</summary>
        /// <returns>被取消的定时器数量（不含已停止的）。</returns>
        int CancelAll();

        /// <summary>槽位是否已分配且代际相符。</summary>
        /// <param name="slot">槽位下标。</param>
        /// <param name="generation">代际号。</param>
        bool Matches(int slot, int generation);

        /// <summary>槽位是否正在计时（令牌已取消即算不在）。</summary>
        /// <param name="slot">槽位下标。</param>
        /// <param name="generation">代际号。</param>
        bool IsActive(int slot, int generation);

        /// <summary>槽位的剩余秒数；已停止/已释放/已取消时为 <c>0</c>。</summary>
        /// <param name="slot">槽位下标。</param>
        /// <param name="generation">代际号。</param>
        /// <param name="now">本轴当前的逻辑时刻。</param>
        float Remaining(int slot, int generation, double now);

        /// <summary>槽位的时长（延时或间隔），供档位重估使用。</summary>
        /// <param name="slot">槽位下标。</param>
        /// <param name="generation">代际号。</param>
        float DurationOf(int slot, int generation);

        /// <summary>停表但保留槽位。</summary>
        /// <param name="slot">槽位下标。</param>
        /// <param name="generation">代际号。</param>
        bool Stop(int slot, int generation);

        /// <summary>用原时长重新起算（锚点取传入的当前逻辑时刻）。</summary>
        /// <param name="slot">槽位下标。</param>
        /// <param name="generation">代际号。</param>
        /// <param name="now">本轴当前的逻辑时刻。</param>
        bool Restart(int slot, int generation, double now);

        /// <summary>停表并释放槽位。</summary>
        /// <param name="slot">槽位下标。</param>
        /// <param name="generation">代际号。</param>
        bool Release(int slot, int generation);
    }

    /// <summary>
    /// 一个 (时间轴, 状态类型) 组合的槽位表：槽位数组 + 代际 + 自由链 + 扫尾。
    /// </summary>
    /// <typeparam name="TState">回调状态类型。</typeparam>
    /// <remarks>
    /// <para><b>自由链是侵入式的</b>：空闲槽位用自身的 <c>NextFree</c> 串成单链表，分配与回收都是 O(1)，
    /// 且不为自由链单独分配任何容器。</para>
    /// <para><b>槽位数组只增不减</b>：容量按 2 倍增长，回收的槽位留在表里等待复用。定时器是高频创建
    /// 销毁的对象，缩容会让「复用」这条承诺在每次规模回落时失效。</para>
    /// <para><b>回收必须清空委托与状态</b>：否则表会长期持有已释放槽位的委托与状态对象——定时器数量
    /// 回落之后，那些闭包仍不会被回收，是最难发现的一种泄漏。</para>
    /// </remarks>
    internal sealed class TimerTable<TState> : ITimerTable
    {
        #region Constants

        /// <summary>初始容量。取 8 而不是 1：定时器几乎不会真的只有一个。</summary>
        private const int InitialCapacity = 8;

        /// <summary>活跃数首次达到此值时告警一次，用于发现漏掉 <c>Dispose</c> 的定时器。</summary>
        private const int LeakWarnThreshold = 1024;

        #endregion

        #region Private Fields

        private readonly int _axis;

        private TimerSlot<TState>[] _slots;
        private int _freeHead;
        private int _runningCount;
        private int _allocatedCount;
        private int _nextGeneration = 1;
        private bool _leakWarned;

        /// <summary>
        /// 派发轮号。<b>只在真正的派发轮（<c>TimerTicker.OnUpdate</c>）里递增</b>；<c>ProcessImmediate</c>
        /// 引发的那一趟不递增——它不是一轮派发，只是「立刻重估一次档位」。槽位据此判断自己是不是
        /// 本趟出生的（见 <see cref="TimerSlot{TState}.BornPass"/>）。
        /// </summary>
        private int _sweepPass;

        #endregion

        #region Constructors

        /// <summary>建一张空表。</summary>
        /// <param name="axis">时间轴。</param>
        /// <param name="tableId">表标识。</param>
        internal TimerTable(int axis, int tableId)
        {
            _axis = axis;
            TableId = tableId;

            _slots = new TimerSlot<TState>[InitialCapacity];
            for (int i = 0; i < _slots.Length - 1; i++)
                _slots[i].NextFree = i + 1;
            _slots[_slots.Length - 1].NextFree = -1;
            _freeHead = 0;
        }

        #endregion

        #region ITimerTable

        /// <inheritdoc />
        public int TableId { get; }

        /// <inheritdoc />
        public int Axis => _axis;

        /// <inheritdoc />
        public int RunningCount => _runningCount;

        /// <inheritdoc />
        public int CopyActiveSlots(List<TimerInfo> buffer, double now)
        {
            int written = 0;

            for (int i = 0; i < _slots.Length; i++)
            {
                TimerSlot<TState> slot = _slots[i];
                if (!slot.Allocated)
                    continue;

                // 令牌已取消的槽位算「不在计时」——与 IsActive 同一口径（它们要等下一趟扫尾才被回收）
                bool running = slot.Running && !slot.Token.IsCancellationRequested;

                float remaining = 0f;
                if (running)
                {
                    double left = slot.Deadline - now;
                    remaining = left > 0d ? (float)left : 0f;
                }

                buffer.Add(new TimerInfo((UpdateTimeMode)_axis, i, remaining, slot.Duration,
                    slot.Repeating, running, CallbackNameOf(slot)));
                written++;
            }

            return written;
        }

        private static string CallbackNameOf(in TimerSlot<TState> slot)
        {
            if (slot.Callback != null)
                return ShortNameOf(slot.Callback.Method);

            if (slot.StateCallback != null)
                return ShortNameOf(slot.StateCallback.Method);

            return "(无回调)";
        }

        private static string ShortNameOf(System.Reflection.MethodInfo method)
        {
            if (method == null)
                return "(未知)";

            var declaring = method.DeclaringType;
            return declaring != null ? declaring.Name + "." + method.Name : method.Name;
        }

        /// <inheritdoc />
        public double Sweep(double now, bool advancePass)
        {
            if (advancePass)
                _sweepPass++;

            double min = double.PositiveInfinity;

            // 每轮重读 _slots：回调里创建定时器可能触发扩容换数组，缓存的局部引用会指向旧数组
            for (int i = 0; i < _slots.Length; i++)
            {
                TimerSlot<TState> slot = _slots[i];

                if (!slot.Allocated || !slot.Running)
                    continue;

                if (slot.Token.IsCancellationRequested)
                {
                    ReleaseSlot(i);
                    continue;
                }

                // 本趟出生的槽位（回调里刚建的，或 ProcessImmediate 那一趟里刚建完的）绝不触发——
                // 否则 `After(0f)` 会当场回调，用户代码就在 Create 还没返回、句柄还没拿到时跑起来了。
                // 但它仍要参与「最近截止」的估计，否则刚建的长定时器会被算漏、档位白拉细一趟
                if (slot.BornPass == _sweepPass)
                {
                    double born = slot.Deadline - now;
                    if (born < min)
                        min = born;
                    continue;
                }

                double remaining = slot.Deadline - now;
                if (remaining > 0d)
                {
                    if (remaining < min)
                        min = remaining;
                    continue;
                }

                Fire(i, now, slot);

                // 触发后重新取剩余：重复定时器已推进到下一拍，一次性定时器已释放
                TimerSlot<TState> after = _slots[i];
                if (after.Allocated && after.Running && after.Generation == slot.Generation)
                {
                    double next = after.Deadline - now;
                    if (next < min)
                        min = next;
                }
            }

            return min;
        }

        /// <inheritdoc />
        public int CancelAll()
        {
            int cancelled = 0;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].Allocated)
                    continue;

                if (IsRunningAndLive(i))
                    cancelled++;

                ReleaseSlot(i);
            }

            return cancelled;
        }

        /// <inheritdoc />
        public bool Matches(int slot, int generation)
        {
            return IsInRange(slot)
                   && _slots[slot].Allocated
                   && _slots[slot].Generation == generation;
        }

        /// <inheritdoc />
        public bool IsActive(int slot, int generation)
        {
            if (!IsInRange(slot))
                return false;

            TimerSlot<TState> s = _slots[slot];
            return s.Allocated
                   && s.Running
                   && s.Generation == generation
                   && !s.Token.IsCancellationRequested;
        }

        /// <inheritdoc />
        public float Remaining(int slot, int generation, double now)
        {
            if (!IsActive(slot, generation))
                return 0f;

            // 锚点在创建/重开的那一刻就按逻辑时钟写好了，因此这里总是实打实的「截止 − 当前」
            double remaining = _slots[slot].Deadline - now;
            if (remaining < 0d)
                remaining = 0d;

            return (float)remaining;
        }

        /// <inheritdoc />
        public float DurationOf(int slot, int generation)
        {
            return Matches(slot, generation) ? _slots[slot].Duration : 0f;
        }

        /// <inheritdoc />
        public bool Stop(int slot, int generation)
        {
            if (!IsInRange(slot))
                return false;

            TimerSlot<TState> s = _slots[slot];
            if (!s.Allocated || s.Generation != generation || !s.Running)
                return false;

            // 令牌已取消的定时器在查询里已经算「不在计时」，这里必须给同一个答案
            if (s.Token.IsCancellationRequested)
                return false;

            s.Running = false;
            _slots[slot] = s;
            _runningCount--;
            return true;
        }

        /// <inheritdoc />
        public bool Restart(int slot, int generation, double now)
        {
            if (!IsInRange(slot))
                return false;

            TimerSlot<TState> s = _slots[slot];
            if (!s.Allocated || s.Generation != generation)
                return false;

            if (s.Token.IsCancellationRequested)
                return false;

            if (!s.Running)
            {
                s.Running = true;
                _runningCount++;
            }

            // 重新定锚：截止时刻 = 重开时刻 + 原时长
            s.Anchor = now;
            s.Deadline = now + s.Duration;
            s.BornPass = _sweepPass;
            _slots[slot] = s;
            return true;
        }

        /// <inheritdoc />
        public bool Release(int slot, int generation)
        {
            if (!Matches(slot, generation))
                return false;

            ReleaseSlot(slot);
            return true;
        }

        #endregion

        #region Internal — Allocation

        /// <summary>
        /// 分配一个槽位并写入初值。
        /// <para><b>无状态与带状态两条路径共用本方法</b>：前者填 <paramref name="callback"/>、后者填
        /// <paramref name="stateCallback"/>，扫尾按「哪个非空」调用。</para>
        /// </summary>
        /// <param name="callback">无状态回调；带状态路径传 <c>null</c>。</param>
        /// <param name="stateCallback">带状态回调；无状态路径传 <c>null</c>。</param>
        /// <param name="state">回调状态。</param>
        /// <param name="duration">延时或间隔。</param>
        /// <param name="repeating">是否固定间隔。</param>
        /// <param name="token">取消令牌；<c>default</c> 表示不受约束。</param>
        /// <param name="now">本轴当前的逻辑时刻——锚点取它，于是「延时」从创建那一刻起算。</param>
        /// <returns>新句柄。</returns>
        internal TimerHandle Allocate(Action callback, Action<TState> stateCallback, TState state,
            float duration, bool repeating, CancellationToken token, double now)
        {
            if (_freeHead < 0)
                Grow();

            int index = _freeHead;
            _freeHead = _slots[index].NextFree;

            int generation = NextGeneration();

            ref TimerSlot<TState> slot = ref _slots[index];
            slot.Callback = callback;
            slot.StateCallback = stateCallback;
            slot.State = state;
            slot.Duration = duration;
            slot.Repeating = repeating;
            slot.Allocated = true;
            slot.Running = true;
            slot.Anchor = now;
            slot.Deadline = now + duration;
            slot.BornPass = _sweepPass;
            slot.Generation = generation;
            slot.Token = token;
            slot.NextFree = -1;

            _runningCount++;
            _allocatedCount++;

            WarnIfLeaking();

            return new TimerHandle(TableId, index, generation);
        }

        #endregion

        #region Private — Sweep

        /// <summary>
        /// 触发一个到期的槽位。
        /// <para><b>一次性定时器先释放再回调</b>：于是「回调里看到的自己是已失效句柄」是明确语义，
        /// 也让「回调里立刻创建新定时器」不会与本趟收尾互相踩。</para>
        /// <para><b>重复定时器的下一拍写在回调之前</b>，且直接落到「锚点 + k × interval」网格上的下一个未来拍——
        /// 既保证不漂移，也保证一帧跨多拍时不补发（卡顿 1 秒不会让 0.1 秒的定时器连发 10 次）。</para>
        /// <para><b>回调抛异常只停这一个定时器</b>：异常绝不允许逃出 <c>TimerTicker.OnUpdate</c>，
        /// 否则 Update 会把它当作坏节点并注销我们的驱动器，表现为「该轴所有定时器静默死亡」。</para>
        /// </summary>
        /// <param name="index">槽位下标。</param>
        /// <param name="now">本轴当前的逻辑时刻。</param>
        /// <param name="slot">触发前的槽位快照。</param>
        private void Fire(int index, double now, in TimerSlot<TState> slot)
        {
            int generation = slot.Generation;
            bool repeating = slot.Repeating;

            if (repeating)
            {
                double elapsed = now - slot.Anchor;
                if (elapsed < 0d)
                    elapsed = 0d;

                long k = (long)Math.Floor(elapsed / slot.Duration) + 1;
                if (k < 1)
                    k = 1;

                TimerSlot<TState> current = _slots[index];
                if (current.Allocated && current.Generation == generation)
                {
                    current.Deadline = slot.Anchor + k * slot.Duration;
                    _slots[index] = current;
                }
            }
            else
            {
                ReleaseSlot(index);
            }

            bool failed = false;
            try
            {
                if (slot.StateCallback != null)
                    slot.StateCallback(slot.State);
                else
                    slot.Callback();
            }
            catch (Exception e)
            {
                failed = true;
                LogManager.Exception(LogCategories.Timer, e, "定时器回调抛出异常，已停止该定时器");
            }

            if (!repeating)
                return;

            // 回调可能已经停表、释放、甚至让槽位易主——那都是它的决定，本趟不再插手
            TimerSlot<TState> after = _slots[index];
            if (!after.Allocated || after.Generation != generation)
                return;

            if (failed)
                ReleaseSlot(index);
        }

        /// <summary>
        /// 释放槽位：清空委托与状态、归还自由链、递减活跃计数。
        /// <para><b>不在这里递增代际</b>：代际由分配侧发号（全局单调），回收后槽位不再属于任何代际——
        /// 有效性判定靠「<c>Allocated</c> + 代际相符」两条同时成立，不必靠回收时改号。</para>
        /// </summary>
        /// <param name="index">槽位下标。</param>
        private void ReleaseSlot(int index)
        {
            if (!_slots[index].Allocated)
                return;

            TimerSlot<TState> slot = _slots[index];
            slot.Callback = null;
            slot.StateCallback = null;
            slot.State = default;
            slot.Token = default;
            slot.Allocated = false;
            slot.Deadline = 0d;
            slot.Anchor = 0d;
            if (slot.Running)
            {
                slot.Running = false;
                _runningCount--;
            }

            slot.NextFree = _freeHead;
            _slots[index] = slot;

            _freeHead = index;
            _allocatedCount--;
        }

        #endregion

        #region Private — Helpers

        private bool IsInRange(int slot)
        {
            return (uint)slot < (uint)_slots.Length;
        }

        private bool IsRunningAndLive(int index)
        {
            TimerSlot<TState> s = _slots[index];
            return s.Running && !s.Token.IsCancellationRequested;
        }

        /// <summary>发一个新代际号；<c>0</c> 是 <c>default(TimerHandle)</c> 的保留值，永不被发出。</summary>
        private int NextGeneration()
        {
            int generation = _nextGeneration++;

            if (_nextGeneration <= 0)
                _nextGeneration = 1;

            return generation;
        }

        /// <summary>容量翻倍，新槽位挂到自由链上（旧槽位的链接关系原样保留）。</summary>
        private void Grow()
        {
            int oldLength = _slots.Length;
            int newLength = oldLength * 2;

            var grown = new TimerSlot<TState>[newLength];
            Array.Copy(_slots, grown, oldLength);
            _slots = grown;

            for (int i = oldLength; i < newLength - 1; i++)
                _slots[i].NextFree = i + 1;
            _slots[newLength - 1].NextFree = -1;
            _freeHead = oldLength;
        }

        /// <summary>
        /// 活跃数首次达到 <see cref="LeakWarnThreshold"/> 时告警一次。
        /// <para>本模块<b>不设容量上限</b>：槽位是纯托管内存，封顶后唯一能给的失败形态是「静默返回空句柄」，
        /// 那比内存增长更难诊断。这条告警是它的替代品——只报一次，不刷屏。</para>
        /// </summary>
        private void WarnIfLeaking()
        {
            if (_leakWarned || _runningCount < LeakWarnThreshold)
                return;

            _leakWarned = true;
            LogManager.Warning(LogCategories.Timer,
                "单个表上正在计时的定时器已达 {0} 个（轴 {1}，已分配 {2} 个槽位）。" +
                "若非有意为之，请检查是否有忘记 Dispose 的定时器；本条不再重复打印。",
                LeakWarnThreshold, (UpdateTimeMode)_axis, _allocatedCount);
        }

        #endregion
    }

    /// <summary>
    /// 一个定时器槽位。
    /// <para><b>是结构体而不是类</b>：槽位数组是连续内存，扫尾是线性遍历，没有虚调用也没有指针追逐。
    /// 代价是「取出来 — 改 — 写回」的写法，以及跨回调必须用代际守卫（回调期间槽位可能易主）。</para>
    /// </summary>
    /// <typeparam name="TState">回调状态类型。</typeparam>
    internal struct TimerSlot<TState>
    {
        /// <summary>无状态回调。</summary>
        public Action Callback;

        /// <summary>带状态回调。与 <see cref="Callback"/> 至多一个非空。</summary>
        public Action<TState> StateCallback;

        /// <summary>回调状态。</summary>
        public TState State;

        /// <summary>取消令牌；<c>default</c> 表示不受约束。回收时清空。</summary>
        public CancellationToken Token;

        /// <summary>首次锚定的逻辑时刻——「不漂移」的基准点。</summary>
        public double Anchor;

        /// <summary>下一拍触发的逻辑时刻。</summary>
        public double Deadline;

        /// <summary>延时（一次性）或间隔（重复）。</summary>
        public float Duration;

        /// <summary>代际号；与句柄比对，让复用后的旧句柄失效。</summary>
        public int Generation;

        /// <summary>自由链后继；在用时恒为 <c>-1</c>。</summary>
        public int NextFree;

        /// <summary>槽位是否已分配（<see cref="Running"/> 为假时仍然算已分配）。</summary>
        public bool Allocated;

        /// <summary>是否正在计时。</summary>
        public bool Running;

        /// <summary>是否为固定间隔定时器。</summary>
        public bool Repeating;

        /// <summary>
        /// 出生趟号：被创建（或重开）时所在的扫尾轮次。
        /// <para>扫尾遇到「趟号 == 本趟」的槽位<b>绝不触发</b>（但仍参与最近截止的估计），保证回调不会在
        /// <c>Create</c> 返回句柄之前跑起来——包括「回调里创建 <c>After(0f)</c>」以及
        /// <c>ProcessImmediate</c> 那一趟里刚建完的槽位这两种当场就该到期的情形。</para>
        /// </summary>
        public int BornPass;
    }
}
