using System.Collections.Generic;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 更新调度器。按 LOD 等级分桶管理 <see cref="IUpdateable"/> 节点，
    /// 通过时间切片算法将更新负载均匀分布到各帧，避免帧消耗集中。
    /// <para>LOD 等级与帧间隔：LOD=0(1帧) LOD=1(2帧) LOD=2(4帧) LOD=3(8帧) LOD=4(16帧) LOD=5(32帧)</para>
    /// <para>同一 LOD 内的节点按深度升序排列，确保父节点先于子节点更新。</para>
    /// </summary>
    public class Updater
    {
        #region Constants

        /// <summary>最大 LOD 等级（含）。</summary>
        private const int MaxLOD = 5;

        /// <summary>LOD 等级总数。</summary>
        private const int LODCount = MaxLOD + 1;

        #endregion

        #region Private Types

        /// <summary>
        /// 更新条目，记录节点引用及其在树中的深度。
        /// </summary>
        private struct Entry
        {
            public IUpdateable Node;
            public int Depth;
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
        public void Register(IUpdateable node, int depth)
        {
            if (node == null) return;

            int lod = Mathf.Clamp(node.LOD, 0, MaxLOD);

            if (_isIterating)
            {
                // 迭代中：暂存到 pending 缓冲
                _pendingAdd[lod].Add(new Entry { Node = node, Depth = depth });
            }
            else
            {
                InsertSorted(_lodEntries[lod], node, depth);
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
        /// 执行一帧更新。按 LOD 时间切片算法分发更新。
        /// <para>每帧调用一次，建议在 MonoBehaviour.Update 中调用。</para>
        /// </summary>
        /// <param name="deltaTime">帧时间差。</param>
        public void Tick(float deltaTime)
        {
            _isIterating = true;

            // LOD=0: 每帧全量更新
            var lod0 = _lodEntries[0];
            for (int i = 0; i < lod0.Count; i++)
            {
                lod0[i].Node.OnUpdate(deltaTime);
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
                // 当前帧应处理的切片索引
                int sliceIndex = (_frameCount >> (lod - 1)) % sliceCount;

                int start = sliceIndex * sliceSize;
                int end = (sliceIndex + 1) * sliceSize;
                if (end > count) end = count;

                for (int i = start; i < end; i++)
                {
                    entries[i].Node.OnUpdate(deltaTime);
                }
            }

            _frameCount++;
            _isIterating = false;

            // 迭代结束后统一处理 pending 缓冲
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
        /// 获取指定 LOD 等级的节点数量。
        /// </summary>
        public int GetCount(int lod)
        {
            if (lod < 0 || lod > MaxLOD) return 0;
            return _lodEntries[lod].Count;
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

        #endregion

        #region Private Methods

        /// <summary>
        /// 按深度升序插入到指定 LOD 列表。
        /// </summary>
        private static void InsertSorted(List<Entry> entries, IUpdateable node, int depth)
        {
            // 二分查找插入位置（按 depth 升序）
            int lo = 0, hi = entries.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (entries[mid].Depth <= depth)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            entries.Insert(lo, new Entry { Node = node, Depth = depth });
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
                        return; // 一个节点只在一个 LOD 列表中
                    }
                }
            }
        }

        /// <summary>
        /// 刷新 pending 缓冲，将迭代期间暂存的注册/注销操作应用到主列表。
        /// </summary>
        private void FlushPending()
        {
            // 先处理移除
            for (int i = 0; i < _pendingRemove.Count; i++)
            {
                RemoveFromList(_lodEntries, _pendingRemove[i]);
            }
            _pendingRemove.Clear();

            // 再处理添加
            for (int lod = 0; lod < LODCount; lod++)
            {
                var pending = _pendingAdd[lod];
                for (int i = 0; i < pending.Count; i++)
                {
                    var entry = pending[i];
                    InsertSorted(_lodEntries[lod], entry.Node, entry.Depth);
                }
                pending.Clear();
            }
        }

        #endregion
    }
}
