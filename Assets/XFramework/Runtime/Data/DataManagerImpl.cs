using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XData
{
    /// <summary>
    /// <see cref="IDataManager"/> 的内部实现，由 <see cref="GameDataNode"/> 实例化并注入到 <see cref="DataManager"/> 静态门面。
    /// <para>Table 数据按主键索引，Global 数据为单一实例。</para>
    /// </summary>
    public sealed class DataManagerImpl : IDataManager
    {
        #region Fields

        private readonly Dictionary<Type, IDataTable> _tables = new();
        private readonly Dictionary<Type, object> _globals = new();
        private IDataStore _store;
        private string _saveVersion = "1.0";

        #endregion

        #region Constructor

        /// <summary>
        /// 构造时自动设置默认 JSON 存储。
        /// </summary>
        public DataManagerImpl()
        {
            _store = new JsonFileDataStore();
        }

        #endregion

        #region Table

        /// <inheritdoc/>
        public DataTable<T> GetOrCreateTable<T>() where T : IDataRow, new()
        {
            var type = typeof(T);
            if (_tables.TryGetValue(type, out var existing))
                return (DataTable<T>)existing;

            var table = new DataTable<T>();
            _tables[type] = table;
            return table;
        }

        /// <inheritdoc/>
        public bool TryGetTable<T>(out DataTable<T> table) where T : IDataRow
        {
            if (_tables.TryGetValue(typeof(T), out var existing))
            {
                table = (DataTable<T>)existing;
                return true;
            }
            table = null;
            return false;
        }

        /// <inheritdoc/>
        public void RegisterTable<T>(DataTable<T> table) where T : IDataRow
        {
            _tables[typeof(T)] = table;
        }

        /// <inheritdoc/>
        public bool RemoveTable<T>() where T : IDataRow
        {
            var type = typeof(T);
            if (_tables.TryGetValue(type, out var table))
            {
                table.Clear();
                _tables.Remove(type);
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool HasTable<T>()
        {
            return _tables.ContainsKey(typeof(T));
        }

        #endregion

        #region Global

        /// <inheritdoc/>
        public T GetOrCreateGlobal<T>() where T : class, new()
        {
            var type = typeof(T);
            if (_globals.TryGetValue(type, out var existing))
                return (T)existing;

            var instance = new T();
            _globals[type] = instance;
            return instance;
        }

        /// <inheritdoc/>
        public bool TryGetGlobal<T>(out T global) where T : class
        {
            if (_globals.TryGetValue(typeof(T), out var existing))
            {
                global = (T)existing;
                return true;
            }
            global = null;
            return false;
        }

        /// <inheritdoc/>
        public void RegisterGlobal<T>(T global) where T : class
        {
            _globals[typeof(T)] = global;
        }

        /// <inheritdoc/>
        public bool RemoveGlobal<T>() where T : class
        {
            return _globals.Remove(typeof(T));
        }

        /// <inheritdoc/>
        public bool HasGlobal<T>()
        {
            return _globals.ContainsKey(typeof(T));
        }

        #endregion

        #region Save / Load

        /// <inheritdoc/>
        public void SetStore(IDataStore store)
        {
            _store = store ?? throw new DataException("IDataStore cannot be null.");
        }

        /// <inheritdoc/>
        public async UniTask SaveAsync(string name, CancellationToken ct = default)
        {
            if (_store == null)
                throw new DataException("Save failed: IDataStore not set. Call SetStore() first.");

            var data = CreateSnapshot();
            await _store.SaveAsync(name, data, ct);
        }

        /// <inheritdoc/>
        public async UniTask LoadAsync(string name, CancellationToken ct = default)
        {
            if (_store == null)
                throw new DataException("Load failed: IDataStore not set. Call SetStore() first.");

            var data = await _store.LoadAsync(name, ct);
            if (data == null)
                throw new DataException($"Save '{name}' not found.");

            ApplySnapshot(data);
        }

        /// <inheritdoc/>
        public void DeleteSave(string name)
        {
            _store?.Delete(name);
        }

        /// <inheritdoc/>
        public bool HasSave(string name)
        {
            return _store != null && _store.Exists(name);
        }

        #endregion

        #region Clear

        /// <inheritdoc/>
        public void ClearAll()
        {
            foreach (var table in _tables.Values)
                table.Clear();
            _tables.Clear();
            _globals.Clear();
        }

        #endregion

        #region Snapshot (SaveData 构建与应用)

        /// <summary>
        /// 从当前内存中的 Table 和 Global 构建 <see cref="SaveData"/> 快照。
        /// </summary>
        private SaveData CreateSnapshot()
        {
            var data = new SaveData
            {
                version = _saveVersion,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            // ------- 序列化 Table -------
            foreach (var (type, table) in _tables)
            {
                var dict = table.Data;
                if (dict == null || dict.Count == 0)
                    continue;

                // 构建一个封装数组的 [Serializable] 辅助对象
                var rowsArray = BuildRowArray(dict);
                if (rowsArray == null)
                    continue;

                var tableJson = JsonUtility.ToJson(rowsArray, prettyPrint: false);
                data.tables.Add(new TableSnap
                {
                    tableName = type.AssemblyQualifiedName,
                    json = tableJson
                });
            }

            // ------- 序列化 Global -------
            foreach (var (type, obj) in _globals)
            {
                var globalJson = JsonUtility.ToJson(obj, prettyPrint: false);
                data.globals.Add(new GlobalSnap
                {
                    globalName = type.AssemblyQualifiedName,
                    json = globalJson
                });
            }

            return data;
        }

        /// <summary>
        /// 将 <see cref="SaveData"/> 快照恢复到当前内存数据中。
        /// </summary>
        private void ApplySnapshot(SaveData data)
        {
            ClearAll();

            // ------- 恢复 Table -------
            if (data.tables != null)
            {
                foreach (var snap in data.tables)
                {
                    var type = System.Type.GetType(snap.tableName);
                    if (type == null)
                    {
                        Debug.LogWarning($"[Data] Cannot resolve type '{snap.tableName}' during load, skipping.");
                        continue;
                    }

                    // 用 JsonUtility 解析 JSON 数组格式
                    var arrayType = type.MakeArrayType();
                    var array = JsonUtility.FromJson(snap.json, arrayType);
                    if (array is IList list)
                    {
                        var table = CreateTableForType(type);
                        foreach (var item in list)
                        {
                            if (item is IDataRow row)
                                table.UpsertRow(row);
                        }
                        _tables[type] = table;
                    }
                }
            }

            // ------- 恢复 Global -------
            if (data.globals != null)
            {
                foreach (var snap in data.globals)
                {
                    var type = System.Type.GetType(snap.globalName);
                    if (type == null)
                    {
                        Debug.LogWarning($"[Data] Cannot resolve type '{snap.globalName}' during load, skipping.");
                        continue;
                    }

                    var obj = JsonUtility.FromJson(snap.json, type);
                    _globals[type] = obj;
                }
            }
        }

        /// <summary>
        /// 将 IDictionary 封装为 [Serializable] 的数组包装对象，供 JsonUtility 序列化。
        /// </summary>
        private static object BuildRowArray(IDictionary dict)
        {
            if (dict == null || dict.Count == 0)
                return null;

            var elementType = dict.Values.GetType().GenericTypeArguments?[0]
                ?? throw new DataException("Cannot determine DataTable element type.");

            var array = Array.CreateInstance(elementType, dict.Count);
            dict.Values.CopyTo(array, 0);
            return array;
        }

        /// <summary>
        /// 为指定类型创建 DataTable（通过反射）。
        /// <para>仅在 ApplySnapshot 中调用，属于低频路径。</para>
        /// </summary>
        private static IDataTable CreateTableForType(Type type)
        {
            var dataTableType = typeof(DataTable<>).MakeGenericType(type);
            return (IDataTable)Activator.CreateInstance(dataTableType);
        }

        #endregion
    }
}