using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XSerialize;

namespace XFramework.XData
{
    /// <summary>
    /// <see cref="IDataManager"/> 的内部实现，由 <see cref="GameDataNode"/> 实例化并注入到 <see cref="DataManager"/> 静态门面。
    /// <para>数据按 <see cref="IDataBlock"/>（游戏模块）组织，序列化委托给 <see cref="XSerialize.Serializer"/>。</para>
    /// </summary>
    public sealed class DataManagerImpl : IDataManager
    {
        #region Fields

        private readonly Dictionary<Type, IDataBlock> _blocks = new();
        private readonly Dictionary<string, IDataBlock> _blockNameIndex = new();

        #endregion

        #region Block

        /// <inheritdoc/>
        public T GetOrCreateBlock<T>() where T : class, IDataBlock, new()
        {
            var type = typeof(T);
            if (_blocks.TryGetValue(type, out var existing))
                return (T)existing;

            var block = new T();
            _blocks[type] = block;
            _blockNameIndex[block.BlockName] = block;
            return block;
        }

        /// <inheritdoc/>
        public bool TryGetBlock<T>(out T block) where T : class, IDataBlock
        {
            if (_blocks.TryGetValue(typeof(T), out var existing))
            {
                block = (T)existing;
                return true;
            }
            block = null;
            return false;
        }

        /// <inheritdoc/>
        public void RegisterBlock<T>(T block) where T : class, IDataBlock
        {
            _blocks[typeof(T)] = block;
            _blockNameIndex[block.BlockName] = block;
        }

        /// <inheritdoc/>
        public bool RemoveBlock<T>() where T : class, IDataBlock
        {
            var type = typeof(T);
            if (_blocks.TryGetValue(type, out var block))
            {
                block.OnClear();
                _blocks.Remove(type);
                _blockNameIndex.Remove(block.BlockName);
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool HasBlock<T>() where T : class, IDataBlock
        {
            return _blocks.ContainsKey(typeof(T));
        }

        /// <inheritdoc/>
        public void ForEachBlock(Action<IDataBlock> action)
        {
            foreach (var block in _blocks.Values)
                action(block);
        }

        #endregion

        #region Snapshot

        /// <inheritdoc/>
        public DataSnapshot CreateSnapshot()
        {
            var data = DataSnapshot.Factory();
            if (data.version == 0)
                data.version = 1;
            data.timestamp = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrEmpty(data.defaultFormat))
                data.defaultFormat = "json";

            foreach (var pair in _blocks)
            {
                var block = pair.Value;
                var saveObj = block.OnSave();
                if (saveObj == null)
                    continue;

                var serializer = XSerialize.Serializer.Default;
                var rawData = serializer.Serialize(saveObj, saveObj.GetType());
                data.blocks.Add(new DataBlockSnapshot
                {
                    blockName = block.BlockName,
                    // 记录 OnSave 返回对象的真实类型，读档时按此类型反序列化后再传给 OnLoad
                    saveType = saveObj.GetType().AssemblyQualifiedName,
                    // 记录写档时的数据版本，读档时据此执行迁移链
                    version = block.DataVersion,
                    data = Convert.ToBase64String(rawData),
                });
            }

            return data;
        }

        /// <inheritdoc/>
        public void ApplySnapshot(DataSnapshot data)
        {
            // 恢复到快照状态：先清空所有已注册 Block 的数据（仅清数据、保留注册，
            // 不能 ClearAll 否则名称索引被清空，后续无法匹配快照中的 block），
            // 快照中未出现的 Block 保持清空。
            ForEachBlock(b => b.OnClear());

            if (data.blocks == null || data.blocks.Count == 0)
                return;

            var defaultFormat = data.defaultFormat;
            if (string.IsNullOrEmpty(defaultFormat))
                defaultFormat = "json";

            foreach (var snap in data.blocks)
            {
                if (string.IsNullOrEmpty(snap.blockName))
                {
                    Debug.LogWarning("[Data] DataBlockSnapshot 缺少 blockName，跳过。");
                    continue;
                }

                if (!_blockNameIndex.TryGetValue(snap.blockName, out var block))
                {
                    Debug.LogWarning($"[Data] 未注册的数据块: {snap.blockName}，跳过。");
                    continue;
                }

                TryRestoreBlock(block, snap, defaultFormat);
            }
        }

        /// <summary>
        /// 恢复单个数据块（全恢复与单块恢复共用的唯一管线）：
        /// 格式解析 → Base64 解码 → saveType 解析（失败回退 Block 类型）→ 反序列化 → 版本迁移链 → <see cref="IDataBlock.OnLoad"/>。
        /// </summary>
        /// <param name="block">目标数据块。</param>
        /// <param name="snap">该块在快照中的条目。</param>
        /// <param name="defaultFormat">快照级默认序列化格式。</param>
        /// <returns>是否成功恢复，供调用方联动处理（如清理脏标记）。</returns>
        private bool TryRestoreBlock(IDataBlock block, DataBlockSnapshot snap, string defaultFormat)
        {
            var format = string.IsNullOrEmpty(snap.format) ? defaultFormat : snap.format;
            if (!XSerialize.Serializer.TryGet(format, out var serializer))
            {
                Debug.LogWarning($"[Data] 不支持的序列化格式: {format}，跳过数据块 {snap.blockName}。");
                return false;
            }

            try
            {
                // 存档版本高于当前代码版本（如代码回滚）：跳过该块，防止旧代码被新结构数据污染内存
                if (snap.version > block.DataVersion)
                {
                    Debug.LogWarning(
                        $"[Data] 数据块 {snap.blockName} 的存档版本({snap.version})高于当前代码版本({block.DataVersion})，跳过该块。");
                    return false;
                }

                var rawData = Convert.FromBase64String(snap.data);

                // 优先按快照记录的 OnSave 返回类型反序列化；
                // 旧存档无 saveType 或类型已无法解析（如类型重命名）时回退 Block 自身类型。
                var targetType = string.IsNullOrEmpty(snap.saveType) ? null : Type.GetType(snap.saveType);
                if (targetType == null || targetType == typeof(object))
                {
                    Debug.LogWarning(
                        $"[Data] 数据块 {snap.blockName} 的 saveType 无法解析（{snap.saveType ?? "空"}），回退使用 Block 类型。");
                    targetType = block.GetType();
                }

                var saveObj = serializer.Deserialize(rawData, targetType);

                // 版本迁移链：在反序列化之后、OnLoad 之前逐版本迁移。
                // 快照 saveType 记录的是写档那一刻（旧版本）的类型，只有旧类型能正确反序列化旧字节；
                // 迁移入参为已反序列化的旧结构对象，每次迁移推进一个版本。
                for (int v = snap.version; v < block.DataVersion; v++)
                    saveObj = block.OnMigrate(saveObj, v);

                block.OnLoad(saveObj);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Data] 恢复数据块 {snap.blockName} 失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Clear

        /// <inheritdoc/>
        public void ClearAll()
        {
            ForEachBlock(b => b.OnClear());
            _blocks.Clear();
            _blockNameIndex.Clear();
        }

        #endregion
    }
}