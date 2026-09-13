using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XUpdate
{

    /// <summary>
    /// 派发时机。每个时机由一套独立的 <see cref="UpdateScheduler"/> 承载（各有自己的桶、
    /// 帧计数与切片相位），因为它们由 PlayerLoop 的不同阶段驱动、节奏互不相干。
    /// <para>取值即内部数组下标，不可改动。</para>
    /// </summary>
    internal enum UpdateTiming
    {
        /// <summary>Update 时机（<c>MonoBehaviour.Update</c> 之后）。</summary>
        Update = 0,

        /// <summary>LateUpdate 时机（<c>MonoBehaviour.LateUpdate</c> 之后）。</summary>
        LateUpdate = 1,

        /// <summary>FixedUpdate 时机（Unity 固定步长，随 <c>timeScale</c> 停摆）。</summary>
        FixedUpdate = 2,
    }

    /// <summary>
    /// 纯 Update 调度器，不依赖节点树。
    /// <para>按 <see cref="UpdateLOD"/> 等级分桶管理 <see cref="IUpdateable"/> 节点，
    /// 通过时间切片算法将更新负载均匀分布到各帧，避免帧消耗集中。</para>
    /// <para>桶按<b>时间轴</b>再分一层：<see cref="UpdateTimeMode.Scaled"/> 与
    /// <see cref="UpdateTimeMode.Unscaled"/> 各有独立的桶、帧计数与切片相位，
    /// 因此暂停（逻辑时间冻结）只影响前者的派发节奏。</para>
    /// <para><b>内部实现</b>：由 <see cref="UpdateManager"/> 门面持有，不对外暴露——第三方
    /// 一律经门面注册与查询，这样内部结构（分桶方式、切片算法、索引）可以继续演进而不构成
    /// 破坏性变更。</para>
    /// </summary>
    internal sealed class UpdateScheduler
    {
        #region Constants

        /// <summary>最大 LOD 等级（含），由 <see cref="UpdateLOD.Max"/> 推导。</summary>
        private const int MaxLOD = (int)UpdateLOD.Max;

        /// <summary>LOD 等级总数。</summary>
        private const int LODCount = MaxLOD + 1;

        /// <summary>时间轴数量。轴下标即 <see cref="UpdateTimeMode"/> 的取值。</summary>
        private const int AxisCount = 2;

        #endregion

        #region Private Types

        /// <summary>
        /// 更新条目，记录节点引用、深度及上次更新时间。
        /// </summary>
        private struct Entry
        {
            public IUpdateLifecycle Node;
            public int Depth;
            public float LastUpdateTime;

            /// <summary>
            /// 所在时间轴。桶已经编码了轴，但这个字段在条目<b>离开桶</b>（进禁用表）后是
            /// 唯一的归位依据——禁用表是一维的，<see cref="Enable"/> 得知道该回哪条轴。
            /// 写入后不再改动，因此不构成「需要同步的第二份真相」。
            /// </summary>
            public byte Axis;
        }

        /// <summary>
        /// 待处理操作的类型。
        /// </summary>
        private enum PendingOpKind : byte
        {
            /// <summary>插入到指定桶。</summary>
            Register,

            /// <summary>从桶与禁用列表中移除。</summary>
            Unregister,

            /// <summary>LOD 迁移。<b>条件操作</b>：仅当应用时节点仍在某个桶里才生效。</summary>
            Move,

            /// <summary>从桶移入禁用表，并回调 <see cref="IUpdateable.OnDisable"/>。</summary>
            Disable,

            /// <summary>从禁用表移回 LOD0 桶，并回调 <see cref="IUpdateable.OnEnable"/>。</summary>
            Enable,
        }

        /// <summary>
        /// 待处理操作。
        /// <para>只带节点引用与<b>扁平桶下标</b>，不带条目下标——条目下标在缓冲期间早已失效，
        /// 这正是「按缓存的 i 写回」会覆盖他人条目的根源。</para>
        /// </summary>
        private struct PendingOp
        {
            public IUpdateLifecycle Node;
            public PendingOpKind Kind;

            /// <summary>目标桶的扁平下标（<see cref="BucketOf"/>）。</summary>
            public int Bucket;

            public int Depth;
            public float Time;
        }

        #endregion

        #region Private Fields

        /// <summary>
        /// 扁平化的桶数组。下标 = <see cref="BucketOf"/>(轴, LOD)，即「轴 × LODCount + LOD」。
        /// <para>两轴分开存储是为了让切片相位与帧计数各自独立：暂停时逻辑轴不推进，
        /// 墙钟轴的节奏不受影响。</para>
        /// </summary>
        private readonly List<Entry>[] _buckets;

        /// <summary>
        /// 待处理操作缓冲。迭代期间按<b>入队顺序</b>暂存，帧末统一应用。
        /// </summary>
        private readonly List<PendingOp> _pendingOps = new List<PendingOp>(16);

        /// <summary>
        /// 当前是否正在迭代中。
        /// <para><b>不变量：迭代期间没有任何代码能改动活表</b>——注册/注销/启用/禁用/清空
        /// 全部走 <see cref="_pendingOps"/> 缓冲，因此遍历时的写回必然落在自己的槽位上。
        /// 破坏这个不变量就会重现「写回覆盖他人条目」这类缺陷。</para>
        /// </summary>
        private bool _isIterating;

        /// <summary>迭代期间收到的清空请求，延迟到帧末统一执行。</summary>
        private bool _clearRequested;

        /// <summary>各时间轴的帧计数器，用于计算该轴当前的时间片索引。</summary>
        private readonly int[] _frameCount = new int[AxisCount];

        /// <summary>
        /// 逻辑轴是否被显式暂停（<see cref="UpdateManager.Pause"/>）。
        /// <para>与时钟里的 <see cref="UpdateClock.IsPaused"/>（<c>timeScale &lt;= 0</c>）互为补充：
        /// 前者不改动 Unity 时间，供「暂停但不动 timeScale」的场景使用，恢复时需要重锚时间基准。</para>
        /// </summary>
        private bool _paused;

        /// <summary>
        /// 恢复后需要把逻辑轴的时间基准重锚一次。
        /// <para>显式暂停期间逻辑时间仍在流逝，不重锚的话恢复当帧每个节点会拿到「整段暂停时长」
        /// 的 delta 并试图一次补完——本调度器刻意不追赶。</para>
        /// </summary>
        private bool _reanchorScaledAxis;

        /// <summary>禁用的节点列表。禁用时移入此列表，启用时移回原时间轴的 LOD0 桶。</summary>
        private readonly List<Entry> _disabledEntries = new List<Entry>();

        /// <summary>
        /// 节点 → 其条目所在桶的扁平下标。使迁移/禁用/注销不必逐个桶去找人。
        /// <para>只在 <see cref="ApplyOp"/> 与 <see cref="ClearImmediate"/> 维护——这是唯一的写入点。
        /// <b>前提是一个节点至多一条条目</b>，由 <see cref="ApplyOp"/> 的注册分支去重保证：
        /// 单值索引表达不了「两条条目分处不同桶」，那种状态下注销的「删净」语义会漏删。</para>
        /// </summary>
        private readonly Dictionary<IUpdateLifecycle, int> _bucketOf = new Dictionary<IUpdateLifecycle, int>();

        #endregion

        #region Constructor

        /// <summary>
        /// 本调度器承载的派发时机，决定调用节点上的哪个方法。
        /// </summary>
        private readonly UpdateTiming _timing;

        /// <summary>
        /// 创建更新调度器实例。
        /// </summary>
        /// <param name="timing">本实例承载的派发时机。</param>
        public UpdateScheduler(UpdateTiming timing = UpdateTiming.Update)
        {
            _timing = timing;

            _buckets = new List<Entry>[AxisCount * LODCount];
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = new List<Entry>();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 执行一帧更新。按 <see cref="UpdateLOD"/> 时间切片算法分发更新。
        /// <para>本重载用<b>同一个时刻</b>驱动两条时间轴，供手动驱动与测试使用；
        /// 生产路径请用 <see cref="Tick(UpdateClock)"/> 传入两个真实时间源。</para>
        /// </summary>
        /// <param name="time">当前时间（<see cref="Time.time"/>），由外部传入避免重复获取。</param>
        public void Tick(float time)
        {
            Tick(new UpdateClock(time, time));
        }

        /// <summary>
        /// 执行一帧更新，两条时间轴各用自己的时刻。
        /// </summary>
        /// <param name="clock">本帧的时间基。</param>
        public void Tick(in UpdateClock clock)
        {
            // 重入防御：本方法持有「迭代期活表不变」的前提，重入会摧毁它
            // （旧实现里从 OnUpdate 里再调 Tick 会嵌套派发，并把闩锁提前置 false）
            if (_isIterating) return;

            _isIterating = true;
            try
            {
                TickInternal(clock);
            }
            finally
            {
                // 用户回调（OnDisable/OnEnable）抛异常时也要归位，
                // 否则闩锁卡死、此后所有注册都只进缓冲
                _isIterating = false;
            }
        }

        /// <summary>
        /// 一帧的实际派发逻辑。只在 <see cref="Tick(UpdateClock)"/> 的闩锁内调用。
        /// </summary>
        private void TickInternal(in UpdateClock clock)
        {
            // 逻辑轴冻结的两种来源：引擎时间被冻结（timeScale <= 0），或调用方显式 Pause
            bool logicalFrozen = _paused || clock.IsPaused;

            for (int axis = 0; axis < AxisCount; axis++)
            {
                bool isLogical = axis == (int)UpdateTimeMode.Scaled;

                // 冻结时不派发、也不推进帧计数：切片相位留在暂停前的位置，恢复后与暂停前接续。
                // 若照常推进，长周期节点会白丢一轮——Frame32 在 60fps 下意味着半秒多的空窗
                if (isLogical && logicalFrozen)
                {
                    continue;
                }

                if (isLogical && _reanchorScaledAxis)
                {
                    _reanchorScaledAxis = false;
                    ReanchorAxis(axis, clock.Time);
                }

                TickAxis(axis, clock.GetTime((UpdateTimeMode)axis));

                // 本轴推进了一帧：切片相位只随自己的轴走，另一条轴冻结与否都不影响它
                _frameCount[axis]++;
            }

            // flush 时闩锁仍持有：回调里再发起的操作继续进缓冲、由本轮循环消化，
            // 而不是直接改活表（那正是旧实现 Disable/Enable 错位的来源）
            FlushPending();

            if (_clearRequested)
            {
                _clearRequested = false;
                ClearImmediate();
            }
        }

        /// <summary>
        /// 派发一条时间轴上的全部桶。
        /// </summary>
        /// <param name="axis">时间轴（即 <see cref="UpdateTimeMode"/> 的取值）。</param>
        /// <param name="now">该轴本帧的时刻。</param>
        private void TickAxis(int axis, float now)
        {
            // LOD=0: 每帧全量更新
            var lod0 = _buckets[BucketOf(axis, 0)];
            for (int i = 0; i < lod0.Count; i++)
            {
                var entry = lod0[i];
                float realDelta = ClampDelta(now - entry.LastUpdateTime);

                int newLOD;
                try
                {
                    newLOD = Mathf.Clamp(TickNode(entry.Node, realDelta, now), 0, MaxLOD);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UpdateScheduler] {entry.Node.GetType().Name}.OnUpdate threw exception, unregistering: {e}");
                    Enqueue(new PendingOp { Node = entry.Node, Kind = PendingOpKind.Unregister });
                    continue;
                }

                entry.LastUpdateTime = now;
                lod0[i] = entry;

                if (newLOD != 0)
                {
                    Enqueue(new PendingOp
                    {
                        Node = entry.Node,
                        Kind = PendingOpKind.Move,
                        Bucket = BucketOf(axis, newLOD),
                    });
                }
            }

            // LOD=1~5: 时间切片更新
            for (int lod = 1; lod < LODCount; lod++)
            {
                var entries = _buckets[BucketOf(axis, lod)];
                int count = entries.Count;
                if (count == 0) continue;

                int sliceCount = 1 << lod;
                int sliceIndex = _frameCount[axis] % sliceCount;

                // 步长切片：本帧只处理下标 ≡ sliceIndex (mod sliceCount) 的条目，
                // 因此 sliceCount 个切片恰好覆盖整桶，且每帧派发量只差 1（count < sliceCount
                // 时余下的切片无事可做，那是「节点本来就少」而非分布不均）。
                // 改前用的是区间切片（start = sliceIndex * ceil(count / sliceCount)）：
                // count 不是 sliceCount 的整数倍时，尾部切片会因越界被夹空、前面的切片超载——
                // 例如 17 个条目 8 个切片会派发成 3,3,3,3,3,2,0,0，后两帧白跑一遍循环
                for (int i = sliceIndex; i < count; i += sliceCount)
                {
                    var entry = entries[i];
                    float realDelta = ClampDelta(now - entry.LastUpdateTime);

                    int newLOD;
                    try
                    {
                        newLOD = Mathf.Clamp(TickNode(entry.Node, realDelta, now), 0, MaxLOD);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[UpdateScheduler] {entry.Node.GetType().Name}.OnUpdate threw exception, unregistering: {e}");
                        Enqueue(new PendingOp { Node = entry.Node, Kind = PendingOpKind.Unregister });
                        continue;
                    }

                    entry.LastUpdateTime = now;
                    entries[i] = entry;

                    if (newLOD != lod)
                    {
                        Enqueue(new PendingOp
                        {
                            Node = entry.Node,
                            Kind = PendingOpKind.Move,
                            Bucket = BucketOf(axis, newLOD),
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 把一条轴上所有条目的时间基准重锚到当前时刻。
        /// <para>显式 <see cref="Pause"/> 不改动 Unity 时间，恢复时若不重锚，每个节点会拿到
        /// 「整段暂停时长」的 delta 并试图一次补完——本调度器刻意不追赶，宁可让恢复后的
        /// 第一帧 delta 为 0。禁用表中的条目不需要处理：它们被 <see cref="Enable"/> 放回桶里时
        /// 会顺带重置时间基准。</para>
        /// </summary>
        private void ReanchorAxis(int axis, float now)
        {
            for (int lod = 0; lod < LODCount; lod++)
            {
                var entries = _buckets[BucketOf(axis, lod)];
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    entry.LastUpdateTime = now;
                    entries[i] = entry;
                }
            }
        }

        /// <summary>
        /// 按本调度器的时机调用节点上的派发方法。
        /// <para>条目只持有 <see cref="IUpdateLifecycle"/>，时机方法在这里转型调用：时机按实例固定，
        /// 分支可预测。注册入口按接口分开（<see cref="UpdateManager.Register(IUpdateable, int, UpdateLOD, UpdateTimeMode)"/>
        /// 与 <see cref="UpdateManager.RegisterLate(ILateUpdateable, int, UpdateLOD, UpdateTimeMode)"/>）
        /// 以保证「把对象注册进它没实现的时机」在编译期就被挡住。</para>
        /// </summary>
        private int TickNode(IUpdateLifecycle node, float deltaTime, float now)
        {
            if (_timing == UpdateTiming.LateUpdate)
            {
                return (int)((ILateUpdateable)node).OnLateUpdate(deltaTime, now);
            }

            if (_timing == UpdateTiming.FixedUpdate)
            {
                return (int)((IFixedUpdateable)node).OnFixedUpdate(deltaTime, now);
            }

            return (int)((IUpdateable)node).OnUpdate(deltaTime, now);
        }

        /// <summary>
        /// 把时间间隔钳到非负。
        /// <para><c>timeScale &lt; 0</c>（倒放）时 <see cref="Time.time"/> 会倒着走，负 delta 会让
        /// 「位置 += 速度 × delta」反向积分；时刻基准照常前进，否则下一次派发会把这段倒放
        /// 又算一遍。</para>
        /// </summary>
        private static float ClampDelta(float delta)
        {
            return delta < 0f ? 0f : delta;
        }

        /// <summary>
        /// 注册一个可更新节点。
        /// <para>同一节点重复注册视为「重新注册」：旧条目会被摘掉，不会出现两条条目、每帧派发两次。</para>
        /// </summary>
        /// <param name="node">要注册的节点。</param>
        /// <param name="depth">节点在树中的深度，用于排序。</param>
        /// <param name="initialLOD">初始 LOD 等级，默认为 <see cref="UpdateLOD.Frame1"/>。</param>
        /// <param name="timeMode">时间轴，默认为 <see cref="UpdateTimeMode.Scaled"/>。</param>
        public void Register(IUpdateLifecycle node, int depth, UpdateLOD initialLOD = UpdateLOD.Frame1,
            UpdateTimeMode timeMode = UpdateTimeMode.Scaled)
        {
            if (node == null) return;

            Enqueue(new PendingOp
            {
                Node = node,
                Kind = PendingOpKind.Register,
                Bucket = BucketOf((int)timeMode, Mathf.Clamp((int)initialLOD, 0, MaxLOD)),
                Depth = depth,
                Time = Time.time,
            });
        }

        /// <summary>
        /// 注销一个可更新节点。
        /// </summary>
        /// <param name="node">要注销的节点。</param>
        public void Unregister(IUpdateLifecycle node)
        {
            if (node == null) return;

            Enqueue(new PendingOp { Node = node, Kind = PendingOpKind.Unregister });
        }

        /// <summary>
        /// 启用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnEnable"/>。</para>
        /// <para><b>派发期间发起时推迟到帧末生效</b>（与注册/注销一致）；从迭代外调用则立即生效。
        /// 另需注意：被重新启用的节点一律回到<b>原时间轴</b>的 <see cref="UpdateLOD.Frame1"/> 桶——
        /// 桶号本身就是 LOD，条目移入禁用表时该信息即已丢失（时间轴不会丢，它记在条目上）。</para>
        /// </summary>
        /// <param name="node">要启用的节点。</param>
        public void Enable(IUpdateLifecycle node)
        {
            if (node == null) return;

            Enqueue(new PendingOp { Node = node, Kind = PendingOpKind.Enable, Time = Time.time });
        }

        /// <summary>
        /// 禁用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnDisable"/>。</para>
        /// <para><b>派发期间发起时推迟到帧末生效</b>（与注册/注销一致）：节点在本帧剩余时间里
        /// 仍可能收到一次 <see cref="IUpdateable.OnUpdate"/>，但帧末起不再派发，
        /// 且不会出现「OnDisable 之后又 OnUpdate」的倒序。需要帧内立即停止派发时，
        /// 请在派发之外调用本方法。</para>
        /// </summary>
        /// <param name="node">要禁用的节点。</param>
        public void Disable(IUpdateLifecycle node)
        {
            if (node == null) return;

            Enqueue(new PendingOp { Node = node, Kind = PendingOpKind.Disable });
        }

        /// <summary>
        /// 检查节点是否处于启用状态。
        /// <para>派发期间状态尚未落表，故先回溯待处理操作、取最后一条决定启用态的操作——
        /// 否则「OnUpdate 里 Disable(B) 后立刻查 B」会拿到过期答案，
        /// 与迭代外调用（立即生效）的观感也不一致。</para>
        /// </summary>
        /// <param name="node">要检查的节点。</param>
        /// <returns>如果节点未被禁用则返回 true；未注册过的节点同样返回 true。</returns>
        public bool IsEnabled(IUpdateLifecycle node)
        {
            if (node == null) return false;

            for (int i = _pendingOps.Count - 1; i >= 0; i--)
            {
                if (_pendingOps[i].Node != node) continue;

                switch (_pendingOps[i].Kind)
                {
                    case PendingOpKind.Disable:
                        return false;

                    case PendingOpKind.Enable:
                    case PendingOpKind.Unregister:
                        // Unregister 会连同禁用表一起清理，故与 Enable 同为「未禁用」
                        return true;

                    default:
                        // Register / Move 不决定启用态：已在禁用表中的节点被 Register 后仍是禁用态，
                        // 故继续向前找真正决定状态的那一条
                        continue;
                }
            }

            for (int i = 0; i < _disabledEntries.Count; i++)
            {
                if (_disabledEntries[i].Node == node)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 立即对指定节点执行一次更新并重新调整 LOD。
        /// <para>用于外部逻辑变化时需要立即响应，不等下一次时间切片。</para>
        /// <para><b>派发期间调用不会执行更新</b>：只把时间基准推到该时刻后返回
        /// （在别人的 <c>OnUpdate</c> 里再次回调自己会形成嵌套派发）。该次调用也不产生 delta——
        /// 下一次正常派发的间隔从这一刻重新起算。</para>
        /// </summary>
        /// <param name="node">要立即更新的节点。</param>
        /// <param name="deltaTime">传入的时间差。</param>
        /// <param name="time">当前时间（<see cref="Time.time"/>）。</param>
        public void ProcessImmediate(IUpdateLifecycle node, float deltaTime, float time)
        {
            ProcessImmediate(node, deltaTime, new UpdateClock(time, time));
        }

        /// <summary>
        /// 立即对指定节点执行一次更新并重新调整 LOD，时刻取自时钟中该节点所属的时间轴。
        /// </summary>
        /// <param name="node">要立即更新的节点。</param>
        /// <param name="deltaTime">传入的时间差。</param>
        /// <param name="clock">本帧的时间基。</param>
        public void ProcessImmediate(IUpdateLifecycle node, float deltaTime, in UpdateClock clock)
        {
            if (node == null) return;

            // 未落桶（还在缓冲里）或已禁用：与「节点不在管理中」一致，静默无操作
            if (!TryFindEntry(node, out int bucket, out int index, out Entry entry))
            {
                return;
            }

            // 时刻按条目自己的时间轴取：墙钟轴上的节点在暂停期间也要拿到在走的那个时间
            float now = clock.GetTime((UpdateTimeMode)entry.Axis);
            int lod = LodOf(bucket);
            int axis = AxisOf(bucket);

            if (_isIterating)
            {
                entry.LastUpdateTime = now;
                _buckets[bucket][index] = entry;
                return;
            }

            // 回调是用户代码，期间同样禁止直接改活表：否则回调里的注销/禁用会让随后的写回
            // 落在已经易主的槽位上（覆盖他人条目，并把已注销的节点插回桶里）。
            // 复用迭代闩锁，让这些操作进缓冲、回调返回后统一 flush。
            _isIterating = true;
            try
            {
                int newLOD = Mathf.Clamp(TickNode(node, deltaTime, now), 0, MaxLOD);

                entry.LastUpdateTime = now;

                // 活表在回调期间未被改动（改动都进了缓冲），故下标仍然有效
                _buckets[bucket][index] = entry;

                if (newLOD != lod)
                {
                    Enqueue(new PendingOp
                    {
                        Node = node,
                        Kind = PendingOpKind.Move,
                        Bucket = BucketOf(axis, newLOD),
                    });
                }

                FlushPending();

                if (_clearRequested)
                {
                    _clearRequested = false;
                    ClearImmediate();
                }
            }
            finally
            {
                // 回调抛异常时也要归位闩锁，异常照旧上抛（缓冲留给下一次 Tick 的 flush）
                _isIterating = false;
            }
        }

        /// <summary>
        /// 清空所有桶、禁用列表和待处理操作缓冲，并把调度器恢复到可用初态
        /// （含暂停开关——测试隔离因此不必额外复位暂停，否则一个 fixture 的 <see cref="Pause"/>
        /// 会静默污染后续所有用例）。
        /// <para><b>派发期间调用时推迟到帧末执行</b>：与注册/注销/启用/禁用同一套语义。
        /// 就地清空会让正在遍历的循环拿着失效下标写回（旧实现在切片分支会直接抛
        /// <c>ArgumentOutOfRangeException</c>）。</para>
        /// <para><b>不回调 <see cref="IUpdateable.OnDisable"/></b>：与 <see cref="Unregister"/>
        /// 一致（它同样不回调）。本方法的主要使用者是测试隔离，在隔离点触发用户回调
        /// 会让 fixture 的收尾去执行业务代码——那里往往引用了已拆掉的管理器。</para>
        /// </summary>
        public void Clear()
        {
            if (_isIterating)
            {
                _clearRequested = true;
                return;
            }

            ClearImmediate();
        }

        /// <summary>
        /// 暂停逻辑轴的派发（由 <see cref="UpdateManager.Pause"/> 转发）。
        /// <para>墙钟轴不受影响——暂停菜单、UI 动画这类逻辑本就该继续运行。</para>
        /// </summary>
        internal void Pause()
        {
            _paused = true;
        }

        /// <summary>
        /// 恢复逻辑轴的派发，并请求一次时间基准重锚（不追赶）。
        /// </summary>
        internal void Resume()
        {
            _paused = false;
            _reanchorScaledAxis = true;
        }

        /// <summary>
        /// 逻辑轴是否被显式暂停（不含 <c>timeScale &lt;= 0</c> 这条路径，那个由驱动方经
        /// <see cref="UpdateClock.IsPaused"/> 传入）。
        /// </summary>
        internal bool IsPaused => _paused;

        /// <summary>
        /// 获取指定 <see cref="UpdateLOD"/> 等级的节点数量（两条时间轴合计）。
        /// </summary>
        public int GetCount(UpdateLOD lod)
        {
            int index = (int)lod;
            if (index < 0 || index > MaxLOD) return 0;

            return _buckets[BucketOf(0, index)].Count + _buckets[BucketOf(1, index)].Count;
        }

        /// <summary>
        /// 获取所有 LOD 等级的节点总数（不含禁用节点，两条时间轴合计）。
        /// </summary>
        public int TotalCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _buckets.Length; i++)
                    count += _buckets[i].Count;
                return count;
            }
        }

        /// <summary>
        /// 获取禁用节点数量。
        /// </summary>
        public int DisabledCount => _disabledEntries.Count;

        #endregion

        #region Private Methods — 待处理操作

        /// <summary>
        /// 入队一条操作。
        /// <para>迭代中只入缓冲、等待帧末统一应用；其余情况立即应用。
        /// <b>两条路径共用 <see cref="ApplyOp"/></b>，避免出现「立即调用一套语义、
        /// 缓冲调用另一套语义」的分叉。</para>
        /// </summary>
        private void Enqueue(in PendingOp op)
        {
            if (_isIterating)
            {
                _pendingOps.Add(op);
                return;
            }

            ApplyOp(op);
        }

        /// <summary>
        /// 应用一条操作。每条都以「上一条生效后的活表」为基准，
        /// 因此同一帧内的 Register→Unregister→Register 序列必然得到「最后一条生效」的结果。
        /// </summary>
        private void ApplyOp(in PendingOp op)
        {
            switch (op.Kind)
            {
                case PendingOpKind.Register:
                {
                    // 已在禁用表中的节点：Register 只把它纳入管理（刷新深度）而不插桶——
                    // 插了它就会在禁用状态下继续收到 OnUpdate，违反 IUpdateable 的契约
                    if (TryFindInDisabled(op.Node, out int disabledIndex))
                    {
                        var disabledEntry = _disabledEntries[disabledIndex];
                        disabledEntry.Depth = op.Depth;
                        _disabledEntries[disabledIndex] = disabledEntry;
                        break;
                    }

                    // 去重：同一节点重复注册视为「重新注册」，先摘掉旧条目再插入。
                    // 不去重会有两个后果——同一节点每帧被派发两次；单值桶索引表达不了
                    // 「两条条目分处两个桶」，注销时的「删净」语义会漏删
                    RemoveFromIndexedBucket(op.Node);

                    InsertSorted(_buckets[op.Bucket], new Entry
                    {
                        Node = op.Node,
                        Depth = op.Depth,
                        LastUpdateTime = op.Time,
                        Axis = (byte)AxisOf(op.Bucket),
                    });
                    _bucketOf[op.Node] = op.Bucket;
                    break;
                }

                case PendingOpKind.Unregister:
                    // 按索引删净（含索引记录）：条目至多一条，但索引一旦失效这里就是最后一道防线
                    RemoveFromIndexedBucket(op.Node);
                    RemoveFromDisabled(op.Node);
                    break;

                case PendingOpKind.Move:
                {
                    // 条件操作：只有节点此刻仍在桶里才迁移。本帧内它若已被注销/被禁用，
                    // 这次迁移必须作废——否则就成了把它重新插回桶里（复活）
                    if (TryTakeFromBuckets(op.Node, out Entry moved))
                    {
                        InsertSorted(_buckets[op.Bucket], moved);
                        _bucketOf[op.Node] = op.Bucket;
                    }
                    break;
                }

                case PendingOpKind.Disable:
                {
                    // 未落桶（还在缓冲里，或本就未注册）时静默无操作，
                    // 也不回调 OnDisable——与「禁用一个不在管理中的节点」对齐
                    if (TryTakeFromBuckets(op.Node, out Entry disabled))
                    {
                        _disabledEntries.Add(disabled);
                        op.Node.OnDisable();
                    }
                    break;
                }

                case PendingOpKind.Enable:
                {
                    if (TryTakeFromDisabled(op.Node, out Entry enabled))
                    {
                        // 重置时间基准：禁用期间累积的间隔不应算作本次 delta
                        enabled.LastUpdateTime = op.Time;

                        // 回到原时间轴（桶号给的 LOD 已丢，轴还记在条目上）
                        int bucket = BucketOf(enabled.Axis, 0);
                        InsertSorted(_buckets[bucket], enabled);
                        _bucketOf[op.Node] = bucket;
                        op.Node.OnEnable();
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 刷新待处理操作缓冲：<b>按入队先后逐条应用</b>。
        /// <para>而不是「先全部注销再全部注册」——后者表达不出调用顺序：同一帧内
        /// 「迁移 LOD 的同时被注销」的节点会在注册阶段被重新插回桶里（永久复活），
        /// 而 Register→Unregister→Register 这类序列无论怎么调两阶段顺序都得不到正确结果。</para>
        /// <para>索引先自增再应用：应用会触发用户回调，回调里可能继续入队、甚至调用
        /// <see cref="Clear"/> 清空本缓冲，每轮重新读 <c>Count</c> 才能安全退出。</para>
        /// </summary>
        private void FlushPending()
        {
            int index = 0;
            while (index < _pendingOps.Count)
            {
                var op = _pendingOps[index];
                index++;
                ApplyOp(op);
            }

            _pendingOps.Clear();
        }

        #endregion

        #region Private Methods — 桶与索引

        /// <summary>
        /// 把时间轴与 LOD 合成扁平桶下标。
        /// </summary>
        private static int BucketOf(int axis, int lod)
        {
            return axis * LODCount + lod;
        }

        /// <summary>
        /// 取扁平桶下标所属的时间轴。
        /// </summary>
        private static int AxisOf(int bucket)
        {
            return bucket / LODCount;
        }

        /// <summary>
        /// 取扁平桶下标对应的 LOD 等级。
        /// </summary>
        private static int LodOf(int bucket)
        {
            return bucket % LODCount;
        }

        /// <summary>
        /// 按深度升序插入到指定桶。
        /// </summary>
        private static void InsertSorted(List<Entry> entries, Entry entry)
        {
            int lo = 0, hi = entries.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (entries[mid].Depth <= entry.Depth)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            entries.Insert(lo, entry);
        }

        /// <summary>
        /// 查找节点在桶中的位置（不移除）。
        /// </summary>
        private bool TryFindEntry(IUpdateLifecycle node, out int bucket, out int index, out Entry entry)
        {
            if (_bucketOf.TryGetValue(node, out bucket))
            {
                var entries = _buckets[bucket];
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Node == node)
                    {
                        index = i;
                        entry = entries[i];
                        return true;
                    }
                }
            }

            bucket = -1;
            index = -1;
            entry = default;
            return false;
        }

        /// <summary>
        /// 取出节点在桶中的条目（移除并返回），并清掉它的桶索引。
        /// </summary>
        /// <returns>节点在桶中时返回 true；未落桶（已注销、已禁用或仍在缓冲中）返回 false。</returns>
        private bool TryTakeFromBuckets(IUpdateLifecycle node, out Entry entry)
        {
            if (_bucketOf.TryGetValue(node, out int bucket))
            {
                var entries = _buckets[bucket];
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Node == node)
                    {
                        entry = entries[i];
                        entries.RemoveAt(i);
                        _bucketOf.Remove(node);
                        return true;
                    }
                }

                // 索引与桶内容脱节——正常路径不可达（索引只在 ApplyOp 与 ClearImmediate 写），
                // 但真出现时必须留痕，否则节点会静默变成「在桶里却谁也找不到」
                Debug.LogWarning($"[UpdateScheduler] 桶索引失效：{node.GetType().Name} 记录在桶 {bucket} 中却找不到条目");
                _bucketOf.Remove(node);
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// 移除节点在桶中的条目与桶索引记录。
        /// </summary>
        private void RemoveFromIndexedBucket(IUpdateLifecycle node)
        {
            if (!_bucketOf.TryGetValue(node, out int bucket))
            {
                return;
            }

            var entries = _buckets[bucket];
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Node == node)
                {
                    entries.RemoveAt(i);
                }
            }

            _bucketOf.Remove(node);
        }

        /// <summary>
        /// 从禁用列表中移除指定节点。
        /// </summary>
        private void RemoveFromDisabled(IUpdateLifecycle node)
        {
            for (int i = _disabledEntries.Count - 1; i >= 0; i--)
            {
                if (_disabledEntries[i].Node == node)
                {
                    _disabledEntries.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// 查找节点在禁用表中的下标（不移除）。
        /// </summary>
        private bool TryFindInDisabled(IUpdateLifecycle node, out int index)
        {
            for (int i = 0; i < _disabledEntries.Count; i++)
            {
                if (_disabledEntries[i].Node == node)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        /// <summary>
        /// 取出节点在禁用表中的条目（移除并返回）。
        /// </summary>
        /// <returns>节点处于禁用态时返回 true。</returns>
        private bool TryTakeFromDisabled(IUpdateLifecycle node, out Entry entry)
        {
            for (int i = _disabledEntries.Count - 1; i >= 0; i--)
            {
                if (_disabledEntries[i].Node == node)
                {
                    entry = _disabledEntries[i];
                    _disabledEntries.RemoveAt(i);
                    return true;
                }
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// 立即清空。只由 <see cref="Clear"/> 与帧末的延迟清空调用。
        /// </summary>
        private void ClearImmediate()
        {
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i].Clear();
            }
            _pendingOps.Clear();
            _disabledEntries.Clear();
            _bucketOf.Clear();
            _paused = false;
            _reanchorScaledAxis = false;
            for (int axis = 0; axis < AxisCount; axis++)
            {
                _frameCount[axis] = 0;
            }
        }

        #endregion
    }
}
