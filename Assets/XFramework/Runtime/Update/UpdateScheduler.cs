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
                    _pendingRemove.Add(entry.Node);
                    continue;
                }

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
                        _pendingRemove.Add(entry.Node);
                        continue;
                    }

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
        /// </summary>
        /// <param name="node">要注销的节点。</param>
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
                RemoveFromDisabled(node);
            }
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
        /// 清空所有 LOD 列表、禁用列表和 pending 缓冲。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < LODCount; i++)
            {
                _lodEntries[i].Clear();
                _pendingAdd[i].Clear();
            }
            _pendingRemove.Clear();
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
        /// 刷新 pending 缓冲，将迭代期间暂存的注册/注销操作应用到主列表。
        /// </summary>
        private void FlushPending()
        {
            for (int i = 0; i < _pendingRemove.Count; i++)
            {
                RemoveFromList(_lodEntries, _pendingRemove[i]);
            }
            _pendingRemove.Clear();

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