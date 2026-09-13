using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XUpdate
{

    /// <summary>
    /// 纯 Update 调度器，不依赖节点树。
    /// <para>按 <see cref="UpdateLOD"/> 等级分桶管理 <see cref="IUpdateable"/> 节点，
    /// 通过时间切片算法将更新负载均匀分布到各帧，避免帧消耗集中。</para>
    /// </summary>
    public class UpdateScheduler
    {
        #region Constants

        /// <summary>最大 LOD 等级（含），由 <see cref="UpdateLOD.Max"/> 推导。</summary>
        private const int MaxLOD = (int)UpdateLOD.Max;

        /// <summary>LOD 等级总数。</summary>
        private const int LODCount = MaxLOD + 1;

        #endregion

        #region Private Types

        /// <summary>
        /// 更新条目，记录节点引用、深度及上次更新时间。
        /// </summary>
        private struct Entry
        {
            public IUpdateable Node;
            public int Depth;
            public float LastUpdateTime;
        }

        /// <summary>
        /// 待处理操作的类型。
        /// </summary>
        private enum PendingOpKind : byte
        {
            /// <summary>插入到指定 LOD 桶。</summary>
            Register,

            /// <summary>从所有桶与禁用列表中移除。</summary>
            Unregister,

            /// <summary>LOD 迁移。<b>条件操作</b>：仅当应用时节点仍在某个桶里才生效。</summary>
            Move,

            /// <summary>从桶移入禁用表，并回调 <see cref="IUpdateable.OnDisable"/>。</summary>
            Disable,

            /// <summary>从禁用表移回 LOD0，并回调 <see cref="IUpdateable.OnEnable"/>。</summary>
            Enable,
        }

        /// <summary>
        /// 待处理操作。
        /// <para>只带节点引用而<b>不带下标</b>——下标在缓冲期间早已失效，
        /// 这正是「按缓存的 i 写回」会覆盖他人条目的根源。</para>
        /// </summary>
        private struct PendingOp
        {
            public IUpdateable Node;
            public PendingOpKind Kind;
            public int Lod;
            public int Depth;
            public float Time;
        }

        #endregion

        #region Private Fields

        /// <summary>按 LOD 等级分桶的更新条目列表。索引 = LOD 等级。</summary>
        private readonly List<Entry>[] _lodEntries;

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

        /// <summary>内部帧计数器，用于计算当前时间片索引。</summary>
        private int _frameCount;

        /// <summary>禁用的节点列表。禁用时移入此列表，启用时移回原 LOD 桶。</summary>
        private readonly List<Entry> _disabledEntries = new List<Entry>();

        #endregion

        #region Constructor

        /// <summary>
        /// 创建更新调度器实例。
        /// </summary>
        public UpdateScheduler()
        {
            _lodEntries = new List<Entry>[LODCount];
            for (int i = 0; i < LODCount; i++)
            {
                _lodEntries[i] = new List<Entry>();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 执行一帧更新。按 <see cref="UpdateLOD"/> 时间切片算法分发更新。
        /// <para>每帧调用一次，建议在 MonoBehaviour.Update 中调用。</para>
        /// </summary>
        /// <param name="time">当前时间（<see cref="Time.time"/>），由外部传入避免重复获取。</param>
        public void Tick(float time)
        {
            // 重入防御：本方法持有「迭代期活表不变」的前提，重入会摧毁它
            // （旧实现里从 OnUpdate 里再调 Tick 会嵌套派发，并把闩锁提前置 false）
            if (_isIterating) return;

            _isIterating = true;
            try
            {
                TickInternal(time);
            }
            finally
            {
                // 用户回调（OnDisable/OnEnable）抛异常时也要归位，
                // 否则闩锁卡死、此后所有注册都只进缓冲
                _isIterating = false;
            }
        }

        /// <summary>
        /// 一帧的实际派发逻辑。只在 <see cref="Tick"/> 的闩锁内调用。
        /// </summary>
        private void TickInternal(float time)
        {
            // LOD=0: 每帧全量更新
            var lod0 = _lodEntries[0];
            for (int i = 0; i < lod0.Count; i++)
            {
                var entry = lod0[i];
                float realDelta = time - entry.LastUpdateTime;

                int newLOD;
                try
                {
                    newLOD = Mathf.Clamp((int)entry.Node.OnUpdate(realDelta, time), 0, MaxLOD);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UpdateScheduler] {entry.Node.GetType().Name}.OnUpdate threw exception, unregistering: {e}");
                    Enqueue(new PendingOp { Node = entry.Node, Kind = PendingOpKind.Unregister });
                    continue;
                }

                entry.LastUpdateTime = time;
                lod0[i] = entry;

                if (newLOD != 0)
                {
                    Enqueue(new PendingOp { Node = entry.Node, Kind = PendingOpKind.Move, Lod = newLOD });
                }
            }

            // LOD=1~5: 时间切片更新
            for (int lod = 1; lod < LODCount; lod++)
            {
                var entries = _lodEntries[lod];
                int count = entries.Count;
                if (count == 0) continue;

                int sliceCount = 1 << lod;
                int sliceSize = (count + sliceCount - 1) / sliceCount;
                int sliceIndex = _frameCount % sliceCount;

                int start = sliceIndex * sliceSize;
                int end = sliceIndex * sliceSize + sliceSize;
                if (end > count) end = count;

                for (int i = start; i < end; i++)
                {
                    var entry = entries[i];
                    float realDelta = time - entry.LastUpdateTime;

                    int newLOD;
                    try
                    {
                        newLOD = Mathf.Clamp((int)entry.Node.OnUpdate(realDelta, time), 0, MaxLOD);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[UpdateScheduler] {entry.Node.GetType().Name}.OnUpdate threw exception, unregistering: {e}");
                        Enqueue(new PendingOp { Node = entry.Node, Kind = PendingOpKind.Unregister });
                        continue;
                    }

                    entry.LastUpdateTime = time;
                    entries[i] = entry;

                    if (newLOD != lod)
                    {
                        Enqueue(new PendingOp { Node = entry.Node, Kind = PendingOpKind.Move, Lod = newLOD });
                    }
                }
            }

            _frameCount++;

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
        /// 注册一个可更新节点。
        /// </summary>
        /// <param name="node">要注册的节点。</param>
        /// <param name="depth">节点在树中的深度，用于排序。</param>
        /// <param name="initialLOD">初始 LOD 等级，默认为 <see cref="UpdateLOD.Frame1"/>。</param>
        public void Register(IUpdateable node, int depth, UpdateLOD initialLOD = UpdateLOD.Frame1)
        {
            if (node == null) return;

            Enqueue(new PendingOp
            {
                Node = node,
                Kind = PendingOpKind.Register,
                Lod = Mathf.Clamp((int)initialLOD, 0, MaxLOD),
                Depth = depth,
                Time = Time.time,
            });
        }

        /// <summary>
        /// 注销一个可更新节点。
        /// </summary>
        /// <param name="node">要注销的节点。</param>
        public void Unregister(IUpdateable node)
        {
            if (node == null) return;

            Enqueue(new PendingOp { Node = node, Kind = PendingOpKind.Unregister });
        }

        /// <summary>
        /// 启用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnEnable"/>。</para>
        /// <para><b>派发期间发起时推迟到帧末生效</b>（与注册/注销一致）；从迭代外调用则立即生效。
        /// 另需注意：被重新启用的节点一律回到 <see cref="UpdateLOD.Frame1"/> 桶——
        /// 桶号本身就是 LOD，条目移入禁用表时该信息即已丢失。</para>
        /// </summary>
        /// <param name="node">要启用的节点。</param>
        public void Enable(IUpdateable node)
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
        public void Disable(IUpdateable node)
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
        public bool IsEnabled(IUpdateable node)
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
        /// </summary>
        /// <param name="node">要立即更新的节点。</param>
        /// <param name="deltaTime">传入的时间差。</param>
        /// <param name="time">当前时间（<see cref="Time.time"/>）。</param>
        public void ProcessImmediate(IUpdateable node, float deltaTime, float time)
        {
            if (node == null) return;

            if (_isIterating)
            {
                for (int lod = 0; lod < LODCount; lod++)
                {
                    var entries = _lodEntries[lod];
                    for (int i = entries.Count - 1; i >= 0; i--)
                    {
                        if (entries[i].Node == node)
                        {
                            var entry = entries[i];
                            entry.LastUpdateTime = time;
                            entries[i] = entry;
                            return;
                        }
                    }
                }
                return;
            }

            for (int lod = 0; lod < LODCount; lod++)
            {
                var entries = _lodEntries[lod];
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Node == node)
                    {
                        var entry = entries[i];
                        int newLOD = Mathf.Clamp((int)node.OnUpdate(deltaTime, time), 0, MaxLOD);

                        entry.LastUpdateTime = time;

                        if (newLOD != lod)
                        {
                            entries.RemoveAt(i);
                            InsertSorted(_lodEntries[newLOD], entry);
                        }
                        else
                        {
                            entries[i] = entry;
                        }
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 清空所有 LOD 列表、禁用列表和待处理操作缓冲。
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
        /// 获取指定 <see cref="UpdateLOD"/> 等级的节点数量。
        /// </summary>
        public int GetCount(UpdateLOD lod)
        {
            int index = (int)lod;
            if (index < 0 || index > MaxLOD) return 0;
            return _lodEntries[index].Count;
        }

        /// <summary>
        /// 获取所有 LOD 等级的节点总数（不含禁用节点）。
        /// </summary>
        public int TotalCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < LODCount; i++)
                    count += _lodEntries[i].Count;
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

                    InsertSorted(_lodEntries[op.Lod], new Entry
                    {
                        Node = op.Node,
                        Depth = op.Depth,
                        LastUpdateTime = op.Time,
                    });
                    break;
                }

                case PendingOpKind.Unregister:
                    // 删净而不是「命中第一个即 return」：重复注册会留下多条条目，
                    // 只删一条会让已注销的节点继续被派发
                    RemoveAllFromBuckets(op.Node);
                    RemoveFromDisabled(op.Node);
                    break;

                case PendingOpKind.Move:
                {
                    // 条件操作：只有节点此刻仍在桶里才迁移。本帧内它若已被注销/被禁用，
                    // 这次迁移必须作废——否则就成了把它重新插回桶里（复活）
                    if (TryTakeFromBuckets(op.Node, out Entry moved))
                    {
                        InsertSorted(_lodEntries[op.Lod], moved);
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
                        InsertSorted(_lodEntries[0], enabled);
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

        #region Private Methods — 列表操作

        /// <summary>
        /// 按深度升序插入到指定 LOD 列表。
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
        /// 取出节点在桶中的条目（移除并返回）。
        /// </summary>
        /// <returns>节点在桶中时返回 true；未落桶（已注销、已禁用或仍在缓冲中）返回 false。</returns>
        private bool TryTakeFromBuckets(IUpdateable node, out Entry entry)
        {
            for (int lod = 0; lod < LODCount; lod++)
            {
                var entries = _lodEntries[lod];
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Node == node)
                    {
                        entry = entries[i];
                        entries.RemoveAt(i);
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// 从所有 LOD 列表中移除节点的<b>全部</b>条目（重复注册会留下多条）。
        /// </summary>
        private void RemoveAllFromBuckets(IUpdateable node)
        {
            for (int lod = 0; lod < LODCount; lod++)
            {
                var entries = _lodEntries[lod];
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Node == node)
                    {
                        entries.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// 从禁用列表中移除指定节点。
        /// </summary>
        private void RemoveFromDisabled(IUpdateable node)
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
        private bool TryFindInDisabled(IUpdateable node, out int index)
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
        private bool TryTakeFromDisabled(IUpdateable node, out Entry entry)
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
            for (int i = 0; i < LODCount; i++)
            {
                _lodEntries[i].Clear();
            }
            _pendingOps.Clear();
            _disabledEntries.Clear();
            _frameCount = 0;
        }

        #endregion
    }
}
