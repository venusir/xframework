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

        /// <summary>当前是否正在迭代中。</summary>
        private bool _isIterating;

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
            _isIterating = true;

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
            _isIterating = false;

            FlushPending();
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
        /// </summary>
        /// <param name="node">要启用的节点。</param>
        public void Enable(IUpdateable node)
        {
            if (node == null) return;

            for (int i = _disabledEntries.Count - 1; i >= 0; i--)
            {
                if (_disabledEntries[i].Node == node)
                {
                    var entry = _disabledEntries[i];
                    _disabledEntries.RemoveAt(i);

                    entry.LastUpdateTime = Time.time;
                    InsertSorted(_lodEntries[0], entry);

                    node.OnEnable();
                    return;
                }
            }
        }

        /// <summary>
        /// 禁用指定节点的 Update 调用。
        /// <para>会触发 <see cref="IUpdateable.OnDisable"/>。</para>
        /// </summary>
        /// <param name="node">要禁用的节点。</param>
        public void Disable(IUpdateable node)
        {
            if (node == null) return;

            for (int lod = 0; lod < LODCount; lod++)
            {
                var entries = _lodEntries[lod];
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Node == node)
                    {
                        var entry = entries[i];
                        entries.RemoveAt(i);
                        _disabledEntries.Add(entry);

                        node.OnDisable();
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 检查节点是否处于启用状态。
        /// </summary>
        /// <param name="node">要检查的节点。</param>
        /// <returns>如果节点未被禁用则返回 true。</returns>
        public bool IsEnabled(IUpdateable node)
        {
            if (node == null) return false;

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
        /// <para><b>不回调 <see cref="IUpdateable.OnDisable"/></b>：与 <see cref="Unregister"/>
        /// 一致（它同样不回调）。本方法的主要使用者是测试隔离，在隔离点触发用户回调
        /// 会让 fixture 的收尾去执行业务代码——那里往往引用了已拆掉的管理器。</para>
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < LODCount; i++)
            {
                _lodEntries[i].Clear();
            }
            _pendingOps.Clear();
            _disabledEntries.Clear();
            _frameCount = 0;
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
                    InsertSorted(_lodEntries[op.Lod], new Entry
                    {
                        Node = op.Node,
                        Depth = op.Depth,
                        LastUpdateTime = op.Time,
                    });
                    break;

                case PendingOpKind.Unregister:
                    // 删净而不是「命中第一个即 return」：重复注册会留下多条条目，
                    // 只删一条会让已注销的节点继续被派发
                    RemoveAllFromBuckets(op.Node);
                    RemoveFromDisabled(op.Node);
                    break;

                case PendingOpKind.Move:
                    // 条件操作：只有节点此刻仍在桶里才迁移。本帧内它若已被注销/被禁用，
                    // 这次迁移必须作废——否则就成了把它重新插回桶里（复活）
                    if (TryTakeFromBuckets(op.Node, out Entry entry))
                    {
                        InsertSorted(_lodEntries[op.Lod], entry);
                    }
                    break;
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

        #endregion
    }
}
