using System;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework.XData
{
    /// <summary>
    /// <see cref="IDataManager"/> 的内部实现，由 <see cref="GameDataNode"/> 实例化并注入到 <see cref="DataManager"/> 静态门面。
    /// <para>数据按 <see cref="IDataBlock"/>（游戏模块）组织，所有需要持久化的数据都应实现 IDataBlock。</para>
    /// </summary>
    public sealed class DataManagerImpl : IDataManager
    {
        #region Fields

        private readonly Dictionary<Type, IDataBlock> _blocks = new();

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
        }

        /// <inheritdoc/>
        public bool RemoveBlock<T>() where T : class, IDataBlock
        {
            var type = typeof(T);
            if (_blocks.TryGetValue(type, out var block))
            {
                block.OnClear();
                _blocks.Remove(type);
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
        public SaveData CreateSnapshot()
        {
            var data = new SaveData
            {
                version = "1.0",
                timestamp = DateTime.UtcNow.ToString("o")
            };

            foreach (var (type, block) in _blocks)
            {
                var saveObj = block.OnSave();
                if (saveObj == null)
                    continue;

                var json = JsonUtility.ToJson(saveObj, prettyPrint: false);
                data.blocks.Add(new BlockSnap
                {
                    blockType = type.AssemblyQualifiedName,
                    json = json
                });
            }

            return data;
        }

        /// <inheritdoc/>
        public void ApplySnapshot(SaveData data)
        {
            ClearAll();

            if (data.blocks == null || data.blocks.Count == 0)
                return;

            foreach (var snap in data.blocks)
            {
                var type = Type.GetType(snap.blockType);
                if (type == null)
                {
                    Debug.LogWarning($"[Data] Cannot resolve type '{snap.blockType}' during load, skipping.");
                    continue;
                }

                if (!typeof(IDataBlock).IsAssignableFrom(type))
                {
                    Debug.LogWarning($"[Data] Type '{type.Name}' does not implement IDataBlock, skipping.");
                    continue;
                }

                try
                {
                    var block = (IDataBlock)Activator.CreateInstance(type);
                    var saveObj = JsonUtility.FromJson(snap.json, type);
                    block.OnLoad(saveObj);
                    _blocks[type] = block;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Data] Failed to restore IDataBlock '{type.Name}': {ex.Message}");
                }
            }
        }

        #endregion

        #region Clear

        /// <inheritdoc/>
        public void ClearAll()
        {
            ForEachBlock(b => b.OnClear());
            _blocks.Clear();
        }

        #endregion

    }
}