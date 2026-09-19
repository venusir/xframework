using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XUpdate
{

    /// <summary>
    /// 派发时机。每个时机由一套独立的 <see cref="UpdateScheduler"/> 承载（各有自己的桶、
    /// 切片节拍与相位），因为它们由 PlayerLoop 的不同阶段驱动、节奏互不相干。
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
    /// 纯 Update 调度器，不依赖任何场景对象。
    /// <para>按 <see cref="UpdateTier"/> 档位分桶管理 <see cref="IUpdateable"/> 节点，
    /// 通过时间切片算法把更新负载摊到各<b>节拍格</b>上，避免帧消耗集中。</para>
    /// <para>节拍按<b>时间</b>推进而非按帧（<see cref="TickPeriod"/>，60Hz 基准），因此第 k 档的
    /// 周期是 2^k × TickPeriod、与帧率无关。这也意味着高帧率下会出现「本帧不推进」的空帧——
    /// 峰值负载不变，只是负载摊得更粗。</para>
    /// <para>桶按<b>时间轴</b>再分一层：<see cref="UpdateTimeMode.Scaled"/> 与
    /// <see cref="UpdateTimeMode.Unscaled"/> 各有独立的桶、节拍与切片相位，
    /// 因此暂停（逻辑时间冻结）只影响前者的派发节奏。</para>
    /// <para><b>内部实现</b>：由 <see cref="UpdateManager"/> 门面持有，不对外暴露——第三方
    /// 一律经门面注册与查询，这样内部结构（分桶方式、切片算法、索引）可以继续演进而不构成
    /// 破坏性变更。</para>
    /// </summary>
    internal sealed class UpdateScheduler
    {
        #region Constants

        /// <summary>最大档位（含），由 <see cref="UpdateTier.Max"/> 推导。</summary>
        private const int MaxTier = (int)UpdateTier.Max;

        /// <summary>档位总数。</summary>
        private const int TierCount = MaxTier + 1;

        /// <summary>时间轴数量。轴下标即 <see cref="UpdateTimeMode"/> 的取值。</summary>
        private const int AxisCount = 2;

        /// <summary>
        /// 切片节拍的设计周期：60Hz。
        /// <para>切片第 k 档的周期 = 2^k × 本常量，因此<b>与帧率无关</b>——Tier3 在 30fps 与
        /// 144fps 下都约等于 133ms，而不再随帧率缩放（旧实现以帧为节拍，跨度可达 4.8 倍）。</para>
        /// </summary>
        internal const float TickPeriod = 1f / 60f;

        /// <summary>
        /// 一帧最多推进的格数。
        /// <para>它决定「周期精确」能覆盖到多慢的帧：余量恒小于一格，故帧长不足 N 格时该补的
        /// 格数不超过 N，逐一补完即不失真。取 3 意味着帧长 50ms（约 20fps）以内都精确。</para>
        /// <para>取 2 的边界恰好压在 30fps（33.3ms）上，任何抖动都会击穿它——实测 30fps 上下
        /// ±5% 抖动时 60 帧只推进 95 格（应 120），因为截断只砍多、不补少。</para>
        /// <para>上限同时是防雪崩的闸门：卡顿时攒下的几十格若一次补完，会让最慢的那一帧
        /// 雪上加霜。3 与 2 在这个意义上没有区别——正常帧根本用不到第 3 格。</para>
        /// </summary>
        private const int MaxTicksPerFrame = 3;

        #endregion

        #region Private Types

        /// <summary>
        /// 更新条目，记录节点引用、排序号及上次更新时间。
        /// </summary>
        private struct Entry
        {
            public IUpdateLifecycle Node;

            /// <summary>
            /// 上次派发时刻。<b>用 double 存</b>：节点拿到的 delta 是它与当前时刻之差，而会话跑长
            /// 之后 float 的 ULP 会逼近一帧（27.8 小时时约 7.8ms）——两个大 float 相减会让 delta 在
            /// 相邻量化台阶之间跳（实测 60 帧里 8 帧偏高 40%、其余偏低 6%）。存精确值、求差后再截到
            /// float（派发契约不变），量化误差就从「两个绝对值各一次」降为「结果一次」。
            /// <para><b>字段顺序是按对齐挑的</b>：double 需要 8 字节对齐，若排在 int 之后，在「按声明
            /// 顺序排布」的布局下会多出 4 字节填充（结构体由 24 涨到 32）——引用之后紧跟 double 才是
            /// 24。桶是 <c>List&lt;Entry&gt;</c>，每条多 8 字节是白付的。</para>
            /// </summary>
            public double LastUpdateTime;

            public int Order;

            /// <summary>
            /// 尚未经历过一次派发，故首次派发的 delta 记 0。
            /// <para>注册与重新启用时一律置位，由首次派发清除。这样调度器不必去猜「现在几点」——
            /// 驱动方给的时间轴未必是 Unity 的 <c>Time.time</c>（测试与确定性回放都自带时刻），
            /// 而 <c>timeScale != 1</c> 时墙钟轴与逻辑时间还差着截距，猜错就是一次成片的假 delta。</para>
            /// </summary>
            public bool NeedsAnchor;

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

            /// <summary>档位迁移。<b>条件操作</b>：仅当应用时节点仍在某个桶里才生效。</summary>
            Move,

            /// <summary>从桶移入禁用表，并回调 <see cref="IUpdateable.OnDisable"/>。</summary>
            Disable,

            /// <summary>从禁用表移回 Tier0 桶，并回调 <see cref="IUpdateable.OnEnable"/>。</summary>
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

            public int Order;
        }

        #endregion

        #region Private Fields

        /// <summary>
        /// 扁平化的桶数组。下标 = <see cref="BucketOf"/>(轴, Tier)，即「轴 × TierCount + Tier」。
        /// <para>两轴分开存储是为了让切片相位与节拍各自独立：暂停时逻辑轴不推进，
        /// 墙钟轴的节奏不受影响。</para>
        /// </summary>
        private readonly List<Entry>[] _buckets;

        /// <summary>
        /// 待处理操作缓冲。派发期间按<b>入队顺序</b>暂存，由本调度器收尾（<see cref="ApplyDeferred"/>）
        /// 或下一次派发之前统一应用。
        /// </summary>
        private readonly List<PendingOp> _pendingOps = new List<PendingOp>(16);

        /// <summary>
        /// 当前是否有<b>任一套</b>调度器正在派发。
        /// <para><b>不变量：派发期间没有任何代码能改动任何活表</b>——注册/注销/启用/禁用/清空
        /// 全部走各实例自己的 <see cref="_pendingOps"/> 缓冲，因此遍历时的写回必然落在自己的槽位上。
        /// 破坏这个不变量就会重现「写回覆盖他人条目」这类缺陷。</para>
        /// <para><b>为什么是 static（跨实例共享）：</b>门面的 Enable/Disable/Unregister/Tick/
        /// ProcessImmediate 是<b>向三套调度器全部转发</b>的。闩锁若每实例一份，「当前这一套在迭代、
        /// 另外两套不在」时，按名字找上门的操作会被另外两套<b>立即</b>应用——用户回调当场嵌进别人的
        /// 回调栈里，同桶靠后的条目还会在本帧倒序收到「OnDisable 之后又 OnUpdate」（两者都是 README
        /// 明确承诺不会发生、且被单调度器用例钉住的）。共享闩锁后，各调度器改在自己收尾
        /// （<see cref="ApplyDeferred"/>）或下一次派发之前应用这些操作。</para>
        /// </summary>
        private static bool _isIterating;

        /// <summary>派发期间收到的清空请求，延迟到本调度器收尾（或下一次派发之前）执行。</summary>
        private bool _clearRequested;

        /// <summary>各时间轴累计推进的格数。切片相位由它对 2^k 取模得到。</summary>
        private readonly int[] _vTick = new int[AxisCount];

        /// <summary>
        /// 各时间轴的时间累加器：不足一格的余量留在这里，它同时是切片相位的连续量。
        /// <para>用 double 而非 float：它要与时刻相减，而余量的量级（毫秒）远小于时刻本身
        /// （秒），float 相减会把它抹掉。</para>
        /// </summary>
        private readonly double[] _tickAccumulator = new double[AxisCount];

        /// <summary>各时间轴上一帧的时刻，用于求本帧间隔。</summary>
        private readonly double[] _lastFrameTime = new double[AxisCount];

        /// <summary>
        /// 各时间轴的时间基准是否已锚定。未锚定时首帧只锚定、不累积。
        /// <para>新实例的 <see cref="_lastFrameTime"/> 是 0 而驱动方给的时刻早已不是 0，
        /// 不锚定的话首次 Tick 的间隔会是「会话已运行时长」。</para>
        /// </summary>
        private readonly bool[] _timeBaseAnchored = new bool[AxisCount];

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

        /// <summary>禁用的节点列表。禁用时移入此列表，启用时移回原时间轴的 Tier0 桶。</summary>
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

            _buckets = new List<Entry>[AxisCount * TierCount];
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = new List<Entry>();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 执行一帧更新。按 <see cref="UpdateTier"/> 时间切片算法分发更新。
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
            // 先消化「本调度器上一趟派发期间缓冲下来的操作」。那些请求可能来自另一时机的回调
            // （另一套正在派发时，这里只入缓冲），它们的发起时刻早于本次派发——若留到本趟收尾才应用，
            // 被禁用/注销的条目会在本次派发里多跑一轮
            ApplyDeferred();

            // 逻辑轴冻结的两种来源：引擎时间被冻结（timeScale <= 0），或调用方显式 Pause
            bool logicalFrozen = _paused || clock.IsPaused;

            for (int axis = 0; axis < AxisCount; axis++)
            {
                bool isLogical = axis == (int)UpdateTimeMode.Scaled;

                // 时刻全程用 double，直到派发边界才截到 float（OnUpdate 的 delta 与 time 都是 float）：
                // 补格判定与节点 delta 都靠它与条目基准求差，精度损失只允许发生一次
                double nowD = clock.GetTime((UpdateTimeMode)axis);

                // 冻结时不派发、也不推进格数：切片相位留在暂停前的位置，恢复后与暂停前接续。
                // 若照常推进，长周期节点会白丢一轮——Tier5 在 60fps 下意味着半秒多的空窗。
                // 但时间基准必须照常前推：不推的话恢复时累加器会吃掉「整段冻结时长」并把攒下的
                // 格一次补出来，等价于追赶——本调度器刻意不追赶
                if (isLogical && logicalFrozen)
                {
                    _lastFrameTime[axis] = nowD;
                    continue;
                }

                if (isLogical && _reanchorScaledAxis)
                {
                    _reanchorScaledAxis = false;
                    ReanchorAxis(axis, nowD);
                }

                // 每帧档位放在补格循环之外：帧率高于节拍时本帧可能一格都不推进，但 Tier0 照发
                TickEveryFrameBucket(axis, nowD);

                // 本轴本帧要推进 n 格：低帧率下 n 可能为 2，高帧率下可能为 0。
                // t 一并传下去：同帧内的多格共用同一个时刻，切片档要靠它避免重复派发
                for (int t = 0, n = AdvanceTicks(axis, nowD); t < n; t++)
                {
                    TickSlicedBuckets(axis, nowD, _vTick[axis], t);
                    _vTick[axis]++;
                }
            }

            // 收尾时闩锁仍持有：回调里再发起的操作继续进缓冲、由本轮循环消化，
            // 而不是直接改活表（那正是旧实现 Disable/Enable 错位的来源）
            ApplyDeferred();
        }

        /// <summary>
        /// 派发每帧档（Tier0）的桶。该档不切片，每帧全量。
        /// </summary>
        /// <param name="axis">时间轴（即 <see cref="UpdateTimeMode"/> 的取值）。</param>
        /// <param name="now">该轴本帧的精确时刻（求 delta 用 <c>double</c>，派发时截到 <c>float</c>）。</param>
        private void TickEveryFrameBucket(int axis, double now)
        {
            // Tier0: 每帧全量更新
            var tier0 = _buckets[BucketOf(axis, 0)];
            for (int i = 0; i < tier0.Count; i++)
            {
                var entry = tier0[i];
                float realDelta = TakeDelta(ref entry, now);

                int newTier;
                try
                {
                    newTier = Mathf.Clamp(TickNode(entry.Node, realDelta, (float)now), 0, MaxTier);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UpdateScheduler] {entry.Node.GetType().Name}.OnUpdate threw exception, unregistering: {e}");
                    Enqueue(new PendingOp { Node = entry.Node, Kind = PendingOpKind.Unregister });
                    continue;
                }

                tier0[i] = entry;

                if (newTier != 0)
                {
                    Enqueue(new PendingOp
                    {
                        Node = entry.Node,
                        Kind = PendingOpKind.Move,
                        Bucket = BucketOf(axis, newTier),
                    });
                }
            }
        }

        /// <summary>
        /// 派发一条轴上全部切片桶（档位≥1）中<b>站在本格</b>上的条目。
        /// </summary>
        /// <param name="axis">时间轴（即 <see cref="UpdateTimeMode"/> 的取值）。</param>
        /// <param name="now">该轴本帧的精确时刻（求 delta 用 <c>double</c>，派发时截到 <c>float</c>）。</param>
        /// <param name="tickIndex">本格的序号，相位由它对 2^k 取模得到。</param>
        /// <param name="tickOffset">本格在<b>本帧内</b>的序号（0 起）。同一帧内的多格共用同一个
        /// <paramref name="now"/>，故要靠它避免在同一帧里重复访问同一档位。</param>
        private void TickSlicedBuckets(int axis, double now, int tickIndex, int tickOffset)
        {
            // Tier1~MaxTier: 时间切片更新
            for (int tier = 1; tier < TierCount; tier++)
            {
                // 同帧内不重复访问同一档位：本帧推进 n 格时，第 tier 档只有 2^tier 个切片相位，
                // n > 2^tier 就必然有节点被轮到两次——而同一帧内的多格共用同一个 now，第二次的
                // delta 是 0（它的 LastUpdateTime 刚被推成 now）。截到「帧内前 2^tier 格」即可消除：
                // 帧内格序号连续，前 2^tier 个恰好把该档相位各覆盖一次，于是每个节点每帧至多派发
                // 一次，且**每帧档（每帧一次）不会再被切片档反超**——帧长超过一格时后者本可在一帧
                // 里轮到 1.5 次（20fps 下实测 Tier1 每秒 29.3 次 vs Tier0 的 20 次）
                if (tickOffset >= (1 << tier)) continue;

                var entries = _buckets[BucketOf(axis, tier)];
                int count = entries.Count;
                if (count == 0) continue;

                int sliceCount = 1 << tier;
                // 取模改掩码：sliceCount 恒为 2 的幂，掩码既更快，也避免 tickIndex 溢出成负数后
                // 取模得到负下标（2^31 格约合 413 天连续运行）
                int sliceIndex = tickIndex & (sliceCount - 1);

                // 步长切片：本格只处理下标 ≡ sliceIndex (mod sliceCount) 的条目，
                // 因此 sliceCount 个切片恰好覆盖整桶，且每格派发量只差 1（count < sliceCount
                // 时余下的切片无事可做，那是「节点本来就少」而非分布不均）。
                // 改前用的是区间切片（start = sliceIndex * ceil(count / sliceCount)）：
                // count 不是 sliceCount 的整数倍时，尾部切片会因越界被夹空、前面的切片超载——
                // 例如 17 个条目 8 个切片会派发成 3,3,3,3,3,2,0,0，后两帧白跑一遍循环
                for (int i = sliceIndex; i < count; i += sliceCount)
                {
                    var entry = entries[i];
                    float realDelta = TakeDelta(ref entry, now);

                    int newTier;
                    try
                    {
                        newTier = Mathf.Clamp(TickNode(entry.Node, realDelta, (float)now), 0, MaxTier);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[UpdateScheduler] {entry.Node.GetType().Name}.OnUpdate threw exception, unregistering: {e}");
                        Enqueue(new PendingOp { Node = entry.Node, Kind = PendingOpKind.Unregister });
                        continue;
                    }

                    entries[i] = entry;

                    if (newTier != tier)
                    {
                        Enqueue(new PendingOp
                        {
                            Node = entry.Node,
                            Kind = PendingOpKind.Move,
                            Bucket = BucketOf(axis, newTier),
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 推进本轴的切片节拍，返回本帧应处理的格数。
        /// <para>节拍按<b>时间</b>走（<see cref="TickPeriod"/>），故第 k 档的周期是
        /// 2^k × TickPeriod，与帧率无关：帧率高于节拍时会出现「本帧不推进」的空帧（返回 0），
        /// 低于节拍时每帧补多格（上限 <see cref="MaxTicksPerFrame"/>）。补不上就丢弃<b>整格</b>
        /// 债务而不是累积到下一帧，因此卡顿不会滚雪球。</para>
        /// </summary>
        /// <param name="axis">时间轴（即 <see cref="UpdateTimeMode"/> 的取值）。</param>
        /// <param name="now">该轴本帧的时刻。取 <c>double</c>：逐帧增量要与一格（约 16.7ms）比较，
        /// 而长会话下 <c>float</c> 的分辨率会逼近一格。</param>
        private int AdvanceTicks(int axis, double now)
        {
            // 固定步轴恒为 1 格：Time.fixedTime 每步恰好前进一个固定步长，本就没有需要修正的
            // 漂移；走墙钟累加器会把 50Hz 的固定步派成 60Hz 的 1/1/1/1/2 节奏，等于改掉固定步
            // 档位的语义（那里「第 k 档」应当是 k 个固定步）
            if (_timing == UpdateTiming.FixedUpdate)
            {
                return 1;
            }

            // 首帧只锚定并恰好推进一格。不锚定的话首次 Tick 的间隔是「会话已运行时长」，
            // 相位会直接跳过第 0 格
            if (!_timeBaseAnchored[axis])
            {
                _timeBaseAnchored[axis] = true;
                _lastFrameTime[axis] = now;
                return 1;
            }

            double delta = now - _lastFrameTime[axis];
            _lastFrameTime[axis] = now;

            // timeScale < 0（倒放）时逻辑时刻会倒退。与 ClampDelta 同源：不钳的话累加器会倒退，
            // 切片桶静默停摆（而旧的帧计数照常推进，故这是本改动引入的新风险面）
            if (delta < 0d)
            {
                delta = 0d;
            }

            _tickAccumulator[axis] += delta;

            int ticks = 0;
            while (_tickAccumulator[axis] >= TickPeriod && ticks < MaxTicksPerFrame)
            {
                _tickAccumulator[axis] -= TickPeriod;
                ticks++;
            }

            // 补不上时丢弃整格债务（不追赶、不雪崩），但保留不足一格的余量——余量就是切片相位。
            // 不可在此夹到 TickPeriod * MaxTicksPerFrame：那会把已挣到的余量一并销毁，而 30fps
            // 每帧恰好吃掉 2 格、累加器正压在边界上，会系统性丢格
            if (_tickAccumulator[axis] >= TickPeriod)
            {
                _tickAccumulator[axis] %= TickPeriod;
            }

            return ticks;
        }

        /// <summary>
        /// 把一条轴上所有条目的时间基准重锚到当前时刻。
        /// <para>显式 <see cref="Pause"/> 不改动 Unity 时间，恢复时若不重锚，每个节点会拿到
        /// 「整段暂停时长」的 delta 并试图一次补完——本调度器刻意不追赶，宁可让恢复后的
        /// 第一帧 delta 为 0。禁用表中的条目不需要处理：它们被 <see cref="Enable"/> 放回桶里时
        /// 会顺带重置时间基准。</para>
        /// </summary>
        private void ReanchorAxis(int axis, double now)
        {
            for (int tier = 0; tier < TierCount; tier++)
            {
                var entries = _buckets[BucketOf(axis, tier)];
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
        /// 分支可预测。注册入口按接口分开（<see cref="UpdateManager.Register(IUpdateable, int, UpdateTier, UpdateTimeMode)"/>
        /// 与 <see cref="UpdateManager.RegisterLate(ILateUpdateable, int, UpdateTier, UpdateTimeMode)"/>）
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
        private static float ClampDelta(double delta)
        {
            return delta < 0d ? 0f : (float)delta;
        }

        /// <summary>
        /// 取条目距上次派发的真实间隔，并把它的时间基准推到本帧。
        /// <para>首次派发（注册或重新启用之后）一律记 0：调度器不知道「注册那一刻」在各时间轴
        /// 上是几点，去猜会在 <c>timeScale != 1</c> 的自定义驱动下算出成片的假间隔。</para>
        /// <para>求差在 <c>double</c> 域完成、只把结果截到 <c>float</c>：条目与当前时刻都是大数，
        /// 先各自截断再相减会叠加两次量化误差（长会话下实测峰峰抖动 46.9%）。</para>
        /// </summary>
        /// <param name="entry">条目副本，由调用方写回。</param>
        /// <param name="now">该轴本帧的精确时刻。</param>
        private static float TakeDelta(ref Entry entry, double now)
        {
            float delta = entry.NeedsAnchor ? 0f : ClampDelta(now - entry.LastUpdateTime);
            entry.NeedsAnchor = false;
            entry.LastUpdateTime = now;
            return delta;
        }

        /// <summary>
        /// 注册一个可更新节点。
        /// <para>同一节点重复注册视为「重新注册」：旧条目会被摘掉，不会出现两条条目、每帧派发两次。</para>
        /// </summary>
        /// <param name="node">要注册的节点。</param>
        /// <param name="order">节点在桶内的排序号，越小越靠前；同值时按注册先后。</param>
        /// <param name="initialTier">初始档位，默认为 <see cref="UpdateTier.Tier0"/>。</param>
        /// <param name="timeMode">时间轴，默认为 <see cref="UpdateTimeMode.Scaled"/>。</param>
        public void Register(IUpdateLifecycle node, int order, UpdateTier initialTier = UpdateTier.Tier0,
            UpdateTimeMode timeMode = UpdateTimeMode.Scaled)
        {
            if (node == null) return;

            Enqueue(new PendingOp
            {
                Node = node,
                Kind = PendingOpKind.Register,
                Bucket = BucketOf((int)timeMode, Mathf.Clamp((int)initialTier, 0, MaxTier)),
                Order = order,
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
        /// <para><b>派发期间发起时推迟到本调度器收尾时生效</b>（与注册/注销一致；跨时机调用则
        /// 顺延到本调度器下一次派发之前）；从派发之外调用则立即生效。
        /// 另需注意：被重新启用的节点一律回到<b>原时间轴</b>的 <see cref="UpdateTier.Tier0"/> 桶——
        /// 桶号本身就是档位，条目移入禁用表时该信息即已丢失（时间轴不会丢，它记在条目上）。</para>
        /// </summary>
        /// <param name="node">要启用的节点。</param>
        public void Enable(IUpdateLifecycle node)
        {
            if (node == null) return;

            Enqueue(new PendingOp { Node = node, Kind = PendingOpKind.Enable });
        }

        /// <summary>
        /// 禁用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnDisable"/>。</para>
        /// <para><b>派发期间发起时推迟到本调度器收尾时生效</b>（与注册/注销一致；跨时机调用则
        /// 顺延到本调度器下一次派发之前）：节点在本趟剩余时间里仍可能收到一次
        /// <see cref="IUpdateable.OnUpdate"/>，但收尾后不再派发，且不会出现
        /// 「OnDisable 之后又 OnUpdate」的倒序。需要帧内立即停止派发时，请在派发之外调用本方法。</para>
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
        /// 立即对指定节点执行一次更新并重新调整档位。
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
        /// 立即对指定节点执行一次更新并重新调整档位，时刻取自时钟中该节点所属的时间轴。
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
            double now = clock.GetTime((UpdateTimeMode)entry.Axis);
            int tier = TierOf(bucket);
            int axis = AxisOf(bucket);

            if (_isIterating)
            {
                // 只推时间基准、不派发。NeedsAnchor 保持原样：它若仍为 true（注册后还没派发过），
                // 「首次派发 delta = 0」那条锚定规则要照常生效，不该被这次调用顶掉
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
                int newTier = Mathf.Clamp(TickNode(node, deltaTime, (float)now), 0, MaxTier);

                entry.NeedsAnchor = false;
                entry.LastUpdateTime = now;

                // 活表在回调期间未被改动（改动都进了缓冲），故下标仍然有效
                _buckets[bucket][index] = entry;

                if (newTier != tier)
                {
                    Enqueue(new PendingOp
                    {
                        Node = node,
                        Kind = PendingOpKind.Move,
                        Bucket = BucketOf(axis, newTier),
                    });
                }

                ApplyDeferred();
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
        /// <para><b>派发期间调用时推迟到本调度器收尾执行</b>：与注册/注销/启用/禁用同一套语义。
        /// 就地清空会让正在遍历的循环拿着失效下标写回（旧实现在切片分支会直接抛
        /// <c>ArgumentOutOfRangeException</c>）。调用发生在<b>另一时机</b>的派发期间时，
        /// 顺延到本调度器下一次派发之前。</para>
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
        /// <para><b>未处于暂停态时什么也不做</b>：重锚会把每个条目的时间基准推到当前时刻，也就是
        /// 让下一次派发的 delta 变成 0。没暂停过就没有任何暂停时长需要抹掉，此时重锚只是凭空丢
        /// 一帧——重复调用本方法这种无害写法就会踩到。</para>
        /// <para>被 <c>timeScale = 0</c> 冻结的那条路径（<see cref="UpdateClock.IsPaused"/>）本就
        /// 不需要重锚：逻辑时刻在冻结期间没有前进，解除冻结后首帧的 delta 自然接近 0。</para>
        /// </summary>
        internal void Resume()
        {
            if (!_paused) return;

            _paused = false;
            _reanchorScaledAxis = true;
        }

        /// <summary>
        /// 逻辑轴是否被显式暂停（不含 <c>timeScale &lt;= 0</c> 这条路径，那个由驱动方经
        /// <see cref="UpdateClock.IsPaused"/> 传入）。
        /// </summary>
        internal bool IsPaused => _paused;

        /// <summary>
        /// 获取指定 <see cref="UpdateTier"/> 等级的节点数量（全部时间轴合计）。
        /// </summary>
        public int GetCount(UpdateTier tier)
        {
            int index = (int)tier;
            if (index < 0 || index > MaxTier) return 0;

            // 按 AxisCount 迭代而不是写死 0/1——加轴时不会静默少计
            int count = 0;
            for (int axis = 0; axis < AxisCount; axis++)
            {
                count += _buckets[BucketOf(axis, index)].Count;
            }
            return count;
        }

        /// <summary>
        /// 获取所有档位的节点总数（不含禁用节点，两条时间轴合计）。
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
        /// <para>任一套调度器派发期间只入缓冲，由本调度器收尾时统一应用；其余情况立即应用。
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
                    // 已在禁用表中的节点：Register 只把它纳入管理（刷新排序号）而不插桶——
                    // 插了它就会在禁用状态下继续收到 OnUpdate，违反 IUpdateable 的契约
                    if (TryFindInDisabled(op.Node, out int disabledIndex))
                    {
                        var disabledEntry = _disabledEntries[disabledIndex];
                        disabledEntry.Order = op.Order;
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
                        Order = op.Order,
                        Axis = (byte)AxisOf(op.Bucket),
                        NeedsAnchor = true,
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
                        NotifyLifecycle(op.Node, enable: false);
                    }
                    break;
                }

                case PendingOpKind.Enable:
                {
                    if (TryTakeFromDisabled(op.Node, out Entry enabled))
                    {
                        // 重置时间基准：禁用期间累积的间隔不应算作本次 delta
                        enabled.NeedsAnchor = true;

                        // 回到原时间轴（桶号给的档位已丢，轴还记在条目上）
                        int bucket = BucketOf(enabled.Axis, 0);
                        InsertSorted(_buckets[bucket], enabled);
                        _bucketOf[op.Node] = bucket;
                        NotifyLifecycle(op.Node, enable: true);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 调用节点的启用/禁用回调，并把回调里的异常隔离在本次调用内。
        /// <para><b>为什么必须隔离：</b>活表在回调之前就已改完，异常本身不会留下不一致的状态，
        /// 但它会中断 <see cref="FlushPending"/> 的循环——排在后面的操作本帧全部失效，而下一帧是
        /// 「先派发、后 flush」，于是已请求禁用/注销的条目会再多派发一次，当帧
        /// <see cref="IsEnabled"/> 也仍报「未禁用」（与 README 的承诺相反）。这里对齐
        /// <see cref="IUpdateable.OnUpdate"/> 的既有约定：记 LogError，但不打断调度。</para>
        /// <para><b>与 OnUpdate 的区别：</b>那里抛异常要连带注销节点（它下一帧多半还会抛、且
        /// 每帧都被调用），而生命周期回调只在状态迁移时触发，一次异常不会变成每帧刷屏，
        /// 注销反而会把「回调写错了」放大成「对象凭空消失」。</para>
        /// </summary>
        /// <param name="node">回调所属节点。</param>
        /// <param name="enable">true 走 <see cref="IUpdateLifecycle.OnEnable"/>，
        /// false 走 <see cref="IUpdateLifecycle.OnDisable"/>。</param>
        private static void NotifyLifecycle(IUpdateLifecycle node, bool enable)
        {
            try
            {
                if (enable)
                {
                    node.OnEnable();
                }
                else
                {
                    node.OnDisable();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError(
                    $"[UpdateScheduler] {node.GetType().Name}.On{(enable ? "Enable" : "Disable")} threw exception: {e}");
            }
        }

        /// <summary>
        /// 刷新待处理操作缓冲：<b>按入队先后逐条应用</b>。
        /// <para>而不是「先全部注销再全部注册」——后者表达不出调用顺序：同一帧内
        /// 「迁移档位的同时被注销」的节点会在注册阶段被重新插回桶里（永久复活），
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

        /// <summary>
        /// 应用本调度器缓冲下来的待处理操作与清空请求。
        /// <para>三个调用点：<see cref="TickInternal"/> 的<b>开头</b>（消化上一趟缓冲下来的操作，
        /// 使它们赶在本次派发之前生效）、<b>末尾</b>（本趟发起的操作在这里落地），以及
        /// <see cref="ProcessImmediate"/> 的收尾。</para>
        /// <para>就地清空（<see cref="ClearImmediate"/>）要求「没有正在进行的桶遍历」，三处调用点
        /// 都满足：派发开始之前、一帧派发结束之后、单条立即派发之后。</para>
        /// </summary>
        private void ApplyDeferred()
        {
            FlushPending();

            if (_clearRequested)
            {
                _clearRequested = false;
                ClearImmediate();
            }
        }

        #endregion

        #region Private Methods — 桶与索引

        /// <summary>
        /// 把时间轴与档位合成扁平桶下标。
        /// </summary>
        private static int BucketOf(int axis, int tier)
        {
            return axis * TierCount + tier;
        }

        /// <summary>
        /// 取扁平桶下标所属的时间轴。
        /// </summary>
        private static int AxisOf(int bucket)
        {
            return bucket / TierCount;
        }

        /// <summary>
        /// 取扁平桶下标对应的档位。
        /// </summary>
        private static int TierOf(int bucket)
        {
            return bucket % TierCount;
        }

        /// <summary>
        /// 按排序号升序插入到指定桶。
        /// </summary>
        private static void InsertSorted(List<Entry> entries, Entry entry)
        {
            int lo = 0, hi = entries.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (entries[mid].Order <= entry.Order)
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
                // 时间基准必须一并复位：不复位的话清空后首次 Tick 的间隔会算成「上一段会话的
                // 时长」，而 PlayMode 下所有用例共享一个 player 实例，污染会传到下一个 fixture
                _vTick[axis] = 0;
                _tickAccumulator[axis] = 0d;
                _lastFrameTime[axis] = 0d;
                _timeBaseAnchored[axis] = false;
            }
        }

        #endregion
    }
}
