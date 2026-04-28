using System.Collections.Generic;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 更新调度器。按 <see cref="UpdateLOD"/> 等级分桶管理 <see cref="IUpdateable"/> 节点，
    /// 通过时间切片算法将更新负载均匀分布到各帧，避免帧消耗集中。
    /// <para>同一 LOD 内的节点按深度升序排列，确保父节点先于子节点更新。</para>
    /// <para>节点的 LOD 由 <see cref="IUpdateable.OnUpdate(float)"/> 的返回值决定，每次更新后自动调整。</para>
    /// <para>deltaTime 通过 <see cref="Time.time"/> 差值计算，不受 LOD 迁移影响，帧率波动时仍保持正确。</para>
    /// </summary>
    public class Updater
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

        #endregion

        #region Private Fields

        /// <summary>按 LOD 等级分桶的更新条目列表。索引 = LOD 等级。</summary>
        private readonly List<Entry>[] _lodEntries;

        /// <summary>待添加的条目缓冲（迭代期间暂存）。</summary>
        private readonly List<Entry>[] _pendingAdd;

        /// <summary>待移除的节点缓冲（迭代期间暂存）。</summary>
        private readonly List<IUpdateable> _pendingRemove;

        /// <summary>当前是否正在迭代中。</summary>
        private bool _isIterating;

        /// <summary>内部帧计数器，用于计算当前时间片索引。</summary>
        private int _frameCount;

        #endregion

        #region Constructor

        /// <summary>
        /// 创建更新调度器实例。
        /// </summary>
        public Updater()
        {
            _lodEntries = new List<Entry>[LODCount];
            _pendingAdd = new List<Entry>[LODCount];
            for (int i = 0; i < LODCount; i++)
            {
                _lodEntries[i] = new List<Entry>();
                _pendingAdd[i] = new List<Entry>();
            }
            _pendingRemove = new List<IUpdateable>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 注册一个可更新节点。
        /// <para>如果当前正在迭代中，会暂存到待添加缓冲，迭代结束后统一处理。</para>
        /// </summary>
        /// <param name="node">可更新节点。</param>
        /// <param name="depth">节点在树中的深度。</param>
        /// <param name="initialLOD">初始 <see cref="UpdateLOD"/> 等级，默认 <see cref="UpdateLOD.EveryFrame"/>。</param>
        public void Register(IUpdateable node, int depth, UpdateLOD initialLOD = UpdateLOD.EveryFrame)
        {
            if (node == null) return;

            int lod = Mathf.Clamp((int)initialLOD, 0, MaxLOD);
            var entry = new Entry { Node = node, Depth = depth, LastUpdateTime = Time.time };

            if (_isIterating)
            {
                _pendingAdd[lod].Add(entry);
            }
            else
            {
                InsertSorted(_lodEntries[lod], entry);
            }
        }

        /// <summary>
        /// 注销一个可更新节点。
        /// <para>如果当前正在迭代中，会暂存到待移除缓冲，迭代结束后统一处理。</para>
        /// </summary>
        /// <param name="node">可更新节点。</param>
        public void Unregister(IUpdateable node)
        {
            if (node == null) return;

            if (_isIterating)
            {
                _pendingRemove.Add(node);
            }
            else
            {
                RemoveFromList(_lodEntries, node);
            }
        }

        /// <summary>
        /// 执行一帧更新。按 <see cref="UpdateLOD"/> 时间切片算法分发更新。
        /// <para>每帧调用一次，建议在 MonoBehaviour.Update 中调用。</para>
        /// <para>更新后根据 <see cref="IUpdateable.OnUpdate(float)"/> 的返回值自动调整 LOD 桶。</para>
        /// <para>deltaTime 通过 <paramref name="time"/> 与 <see cref="Entry.LastUpdateTime"/> 的差值计算。</para>
        /// </summary>
        /// <param name="time">当前时间（<see cref="Time.time"/>），由外部传入避免每节点重复获取。</param>
        public void Tick(float time)
        {
            _isIterating = true;

            // LOD=0: 每帧全量更新
            var lod0 = _lodEntries[0];
            for (int i = 0; i < lod0.Count; i++)
            {
                var entry = lod0[i];
                float realDelta = time - entry.LastUpdateTime;
                int newLOD = Mathf.Clamp((int)entry.Node.OnUpdate(realDelta, time), 0, MaxLOD);

                // 更新 LastUpdateTime
                entry.LastUpdateTime = time;
                lod0[i] = entry;

                if (newLOD != 0)
                {
                    _pendingAdd[newLOD].Add(entry);
                    _pendingRemove.Add(entry.Node);
                }
            }

            // LOD=1~5: 时间切片更新
            for (int lod = 1; lod < LODCount; lod++)
            {
                var entries = _lodEntries[lod];
                int count = entries.Count;
                if (count == 0) continue;

                // 切片数量 = 1 << lod
                int sliceCount = 1 << lod;
                // 切片大小（向上取整）
                int sliceSize = (count + sliceCount - 1) / sliceCount;
                // 当前帧应处理的切片索引（每帧轮换一个切片）
                int sliceIndex = _frameCount % sliceCount;

                int start = sliceIndex * sliceSize;
                int end = sliceIndex * sliceSize + sliceSize;
                if (end > count) end = count;

                for (int i = start; i < end; i++)
                {
                    var entry = entries[i];
                    float realDelta = time - entry.LastUpdateTime;
                    int newLOD = Mathf.Clamp((int)entry.Node.OnUpdate(realDelta, time), 0, MaxLOD);

                    // 更新 LastUpdateTime
                    entry.LastUpdateTime = time;
                    entries[i] = entry;

                    if (newLOD != lod)
                    {
                        _pendingAdd[newLOD].Add(entry);
                        _pendingRemove.Add(entry.Node);
                    }
                }
            }

            _frameCount++;
            _isIterating = false;

            // 迭代结束后统一处理 pending 缓冲（移除旧桶 + 插入新桶）
            FlushPending();
        }

        /// <summary>
        /// 清空所有 LOD 列表和 pending 缓冲。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < LODCount; i++)
            {
                _lodEntries[i].Clear();
                _pendingAdd[i].Clear();
            }
            _pendingRemove.Clear();
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
        /// 获取所有 LOD 等级的节点总数。
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
        /// 立即对指定节点执行一次更新并重新调整 LOD。
        /// <para>用于外部逻辑变化时需要立即响应，不等下一次时间切片。</para>
        /// <para>如果当前正在迭代中（即节点自身的 <see cref="IUpdateable.OnUpdate(float, float)"/> 内调用），
        /// 则仅重置 <see cref="Entry.LastUpdateTime"/> 而不操作列表，
        /// <see cref="IUpdateable.OnUpdate(float, float)"/> 由本轮 <see cref="Tick(float)"/> 自然处理。</para>
        /// </summary>
        /// <param name="node">要立即更新的节点。</param>
        /// <param name="deltaTime">传入的时间差。</param>
        /// <param name="time">当前时间（<see cref="UnityEngine.Time.time"/>）。</param>
        public void ProcessImmediate(IUpdateable node, float deltaTime, float time)
        {
            if (node == null) return;

            // 迭代期间：只更新 LastUpdateTime，不修改列表
            // OnUpdate 由本轮 Tick 自然处理，且后续 deltaTime 正确
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

            // 非迭代期间：执行 OnUpdate + 调整 LOD
            for (int lod = 0; lod < LODCount; lod++)
            {
                var entries = _lodEntries[lod];
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Node == node)
                    {
                        var entry = entries[i];
                        int newLOD = Mathf.Clamp((int)node.OnUpdate(deltaTime, time), 0, MaxLOD);

                        // 重置 LastUpdateTime，避免下次正常更新时收到累积 deltaTime
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

        #endregion

        #region Private Methods

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
        /// 从所有 LOD 列表中移除指定节点。
        /// </summary>
        private static void RemoveFromList(List<Entry>[] lodEntries, IUpdateable node)
        {
            for (int lod = 0; lod < LODCount; lod++)
            {
                var entries = lodEntries[lod];
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].Node == node)
                    {
                        entries.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 刷新 pending 缓冲，将迭代期间暂存的注册/注销操作应用到主列表。
        /// </summary>
        private void FlushPending()
        {
            // 先处理移除（包括 LOD 变化导致的旧桶移除）
            for (int i = 0; i < _pendingRemove.Count; i++)
            {
                RemoveFromList(_lodEntries, _pendingRemove[i]);
            }
            _pendingRemove.Clear();

            // 再处理添加（包括 LOD 变化导致的新桶插入）
            for (int lod = 0; lod < LODCount; lod++)
            {
                var pending = _pendingAdd[lod];
                for (int i = 0; i < pending.Count; i++)
                {
                    InsertSorted(_lodEntries[lod], pending[i]);
                }
                pending.Clear();
            }
        }

        #endregion
    }
}
