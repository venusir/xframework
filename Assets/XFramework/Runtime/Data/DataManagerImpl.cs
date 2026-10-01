using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XLog;
using XFramework.XSerialize;

namespace XFramework.XData
{
    /// <summary>
    /// <see cref="IDataManager"/> 的内部实现，由 <see cref="DataBootstrapStage"/> 实例化并注入到 <see cref="DataManager"/> 静态门面。
    /// <para>数据按 <see cref="IDataBlock"/>（游戏模块）组织，序列化委托给 <see cref="XSerialize.Serializer"/>。</para>
    /// </summary>
    public sealed class DataManagerImpl : IDataManager
    {
        #region Fields

        private readonly Dictionary<Type, IDataBlock> _blocks = new();
        private readonly Dictionary<string, IDataBlock> _blockNameIndex = new();
        private readonly HashSet<IDataBlock> _dirtyBlocks = new();
        private readonly Dictionary<string, Type> _saveTypeCache = new(); // saveType → 类型（反射解析结果缓存）

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
            IndexBlockByName(block);
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
            // 参数防御放在最前：原先 null 会先被写进 _blocks、再在取 BlockName 时抛 NRE，
            // 于是容器里留下一个 null 条目——此后 TryGetBlock 返回 true 而值为 null、遍历时炸
            if (block == null) throw new ArgumentNullException(nameof(block));

            // 覆盖同名 Block 时先清理旧实例：OnClear 释放其资源/订阅，并移除脏标记，避免泄漏与假脏
            if (_blocks.TryGetValue(typeof(T), out var old))
            {
                old.OnClear();
                _dirtyBlocks.Remove(old);

                // 旧实例的名称索引一并摘掉：它已不在 _blocks 里，索引留着会让按名恢复命中一个幽灵对象
                // （新旧名字不同时尤其隐蔽——那个旧名字会一直指向它）
                _blockNameIndex.Remove(old.BlockName);
            }

            _blocks[typeof(T)] = block;
            IndexBlockByName(block);
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
                _dirtyBlocks.Remove(block); // 联动清理脏标记
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
            if (action == null) throw new ArgumentNullException(nameof(action));

            // 先取快照再遍历：回调里增删块（第三方 OnClear 回收资源、甚至回头调 DataManager）
            // 会让活字典的枚举器失效并抛 InvalidOperationException。低频路径，一次分配可接受——
            // 与仓内「派发前先取快照」的取舍同向（DispatchListPool、Lock 的订阅快照）
            var snapshot = new List<IDataBlock>(_blocks.Values);
            for (int i = 0; i < snapshot.Count; i++)
                action(snapshot[i]);
        }

        /// <summary>
        /// 把块登进名称索引，并在**另一个实例**已占用同名时留痕。
        /// <para><b>为什么必须留痕：</b>快照恢复是按 <see cref="DataBlockSnapshot.blockName"/> 反查块的，
        /// 而索引一个名字只能指向一个实例。两个**不同类型**的块返回同一个名字时，先注册的那个
        /// <b>永远恢复不回来</b>——它会停在 <see cref="IDataBlock.OnClear"/> 之后的默认值上，全程没有
        /// 编译期提示、也没有运行期信号，是静默丢数据。</para>
        /// <para>刻意**只报错不阻止**：改名是使用方的事，且迁移期偶尔需要临时共用名字——
        /// 与仓内「回退都留痕」的惯例一致。</para>
        /// </summary>
        private void IndexBlockByName(IDataBlock block)
        {
            if (_blockNameIndex.TryGetValue(block.BlockName, out var occupant) && occupant != block)
            {
                LogManager.Error(LogCategories.Data,
                    "数据块名称冲突：\"{0}\" 已被 {1} 占用，新登入的 {2} 覆盖了索引项。" +
                    "快照恢复按 blockName 反查，因此 {1} 的数据将无法恢复——请让两者的 BlockName 不同。",
                    block.BlockName, occupant.GetType().Name, block.GetType().Name);
            }

            _blockNameIndex[block.BlockName] = block;
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
                var snap = BuildBlockSnapshot(pair.Value);
                if (snap != null)
                    data.blocks.Add(snap);
            }

            // 快照导出成功即视为已保存，清空全部脏标记；序列化异常会向上传播，此时脏标记保留
            _dirtyBlocks.Clear();

            return data;
        }

        /// <summary>
        /// 构建单个 Block 的快照条目（全量导出与单块导出共用的序列化逻辑）。
        /// <para>序列化异常向上传播，由调用方决定处理（全量导出异常时脏标记保留）。</para>
        /// </summary>
        private DataBlockSnapshot BuildBlockSnapshot(IDataBlock block)
        {
            var saveObj = block.OnSave();
            if (saveObj == null)
                return null;

            var serializer = XSerialize.Serializer.Default;
            var rawData = serializer.Serialize(saveObj, saveObj.GetType());
            return new DataBlockSnapshot
            {
                blockName = block.BlockName,
                // 记录 OnSave 返回对象的真实类型，读档时按此类型反序列化后再传给 OnLoad
                saveType = saveObj.GetType().AssemblyQualifiedName,
                // 记录写档时的数据版本，读档时据此执行迁移链
                version = block.DataVersion,
                data = Convert.ToBase64String(rawData),
            };
        }

        /// <inheritdoc/>
        public int ApplySnapshot(DataSnapshot data)
        {
            // 参数防御必须在清空**之前**：原先的顺序是先清空全部块、再在解引用 data.blocks 处抛 NRE，
            // 于是「传了个 null」的代价是内存数据全被清掉之后才报错
            if (data == null) throw new ArgumentNullException(nameof(data));

            // 恢复到快照状态：先清空所有已注册 Block 的数据（仅清数据、保留注册，
            // 不能 ClearAll 否则名称索引被清空，后续无法匹配快照中的 block），
            // 快照中未出现的 Block 保持清空。
            ForEachBlock(b => b.OnClear());

            // 恢复即干净：无论快照是否为空，内存数据已重置/恢复，清空全部脏标记
            _dirtyBlocks.Clear();

            if (data.blocks == null || data.blocks.Count == 0)
                return 0;

            var defaultFormat = data.defaultFormat;
            if (string.IsNullOrEmpty(defaultFormat))
                defaultFormat = "json";

            // 恢复失败不中断整体恢复（单个坏块不该让其余数据也读不出来），但必须计数上报：
            // 调用方需要据此判断本次加载是否完整——「部分块没恢复」等价于内存里少了一半数据，
            // 静默成功是危险的
            var failedBlocks = 0;

            foreach (var snap in data.blocks)
            {
                if (string.IsNullOrEmpty(snap.blockName))
                {
                    LogManager.Warning(LogCategories.Data, "DataBlockSnapshot 缺少 blockName，跳过。");
                    failedBlocks++;
                    continue;
                }

                if (!_blockNameIndex.TryGetValue(snap.blockName, out var block))
                {
                    LogManager.Warning(LogCategories.Data, "未注册的数据块: {0}，跳过。", snap.blockName);
                    failedBlocks++;
                    continue;
                }

                if (!TryRestoreBlock(block, snap, defaultFormat))
                    failedBlocks++;
            }

            return failedBlocks;
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
                LogManager.Warning(LogCategories.Data, "不支持的序列化格式: {0}，跳过数据块 {1}。", format, snap.blockName);
                return false;
            }

            try
            {
                // 存档版本高于当前代码版本（如代码回滚）：跳过该块，防止旧代码被新结构数据污染内存
                if (snap.version > block.DataVersion)
                {
                    LogManager.Warning(LogCategories.Data,
                        "数据块 {0} 的存档版本({1})高于当前代码版本({2})，跳过该块。",
                        snap.blockName, snap.version, block.DataVersion);
                    return false;
                }

                var rawData = Convert.FromBase64String(snap.data);

                // 优先按快照记录的 OnSave 返回类型反序列化（反射解析结果缓存，满足「反射必须缓存」约定）；
                // 旧存档无 saveType 或类型已无法解析（如类型重命名）时回退 Block 自身类型。
                Type targetType = null;
                if (!string.IsNullOrEmpty(snap.saveType))
                {
                    // 仅成功解析的结果入缓存：失败路径罕见（类型重命名等）且已有回退警告，无需缓存负结果
                    if (!_saveTypeCache.TryGetValue(snap.saveType, out targetType))
                    {
                        targetType = Type.GetType(snap.saveType);
                        if (targetType != null)
                            _saveTypeCache[snap.saveType] = targetType;
                    }
                }

                if (targetType == null || targetType == typeof(object))
                {
                    LogManager.Warning(LogCategories.Data,
                        "数据块 {0} 的 saveType 无法解析（{1}），回退使用 Block 类型。",
                        snap.blockName, snap.saveType ?? "空");
                    targetType = block.GetType();
                }

                var saveObj = serializer.Deserialize(rawData, targetType);

                // 反序列化结果为空（空 data 条目或 JSON "null"）：不把 null 传进第三方 OnMigrate/OnLoad，
                // 避免强转型实现抛出 NRE 被 catch 吞掉造成静默失败
                if (saveObj == null)
                {
                    LogManager.Warning(LogCategories.Data, "数据块 {0} 反序列化结果为空，跳过。", snap.blockName);
                    return false;
                }

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
                LogManager.Exception(LogLevel.Warning, LogCategories.Data, ex,
                    string.Format("恢复数据块 {0} 失败", snap.blockName));
                return false;
            }
        }

        #endregion

        #region Dirty

        /// <inheritdoc/>
        public void MarkDirty<T>() where T : class, IDataBlock
        {
            if (!_blocks.TryGetValue(typeof(T), out var block))
            {
                LogManager.Warning(LogCategories.Data, "标记脏的数据块未注册: {0}，忽略。", typeof(T).Name);
                return;
            }

            _dirtyBlocks.Add(block);
        }

        /// <inheritdoc/>
        public bool IsDirty<T>() where T : class, IDataBlock
        {
            return _blocks.TryGetValue(typeof(T), out var block) && _dirtyBlocks.Contains(block);
        }

        /// <inheritdoc/>
        public bool HasDirtyBlocks => _dirtyBlocks.Count > 0;

        /// <inheritdoc/>
        public List<IDataBlock> GetDirtyBlocks()
        {
            // 脏查询为低频操作（存档时），直接新建列表，避免复用列表被外部持有导致下次调用串数据
            return new List<IDataBlock>(_dirtyBlocks);
        }

        #endregion

        #region BlockSnapshot

        /// <inheritdoc/>
        public DataBlockSnapshot CreateBlockSnapshot<T>() where T : class, IDataBlock
        {
            if (!_blocks.TryGetValue(typeof(T), out var block))
            {
                LogManager.Warning(LogCategories.Data, "创建快照的数据块未注册: {0}，返回 null。", typeof(T).Name);
                return null;
            }

            // 单块导出不清空脏标记：仅读取当前数据，不代表该块已保存
            return BuildBlockSnapshot(block);
        }

        /// <inheritdoc/>
        public bool ApplyBlockSnapshot(DataBlockSnapshot snap)
        {
            if (snap == null)
            {
                LogManager.Warning(LogCategories.Data, "ApplyBlockSnapshot 收到 null 快照，忽略。");
                return false;
            }

            if (string.IsNullOrEmpty(snap.blockName))
            {
                LogManager.Warning(LogCategories.Data, "DataBlockSnapshot 缺少 blockName，跳过。");
                return false;
            }

            if (!_blockNameIndex.TryGetValue(snap.blockName, out var block))
            {
                LogManager.Warning(LogCategories.Data, "未注册的数据块: {0}，跳过。", snap.blockName);
                return false;
            }

            // 恢复单块：先清空目标块数据，再走共享恢复管线（不触碰其他 Block）。
            // 清空与取默认序列化器都纳入受保护范围：两者都会抛（第三方 OnClear、序列化器未注册），
            // 而原先它们落在 try 之外——异常外泄时块已被清空、还不计入任何计数，
            // 与一墙之隔的 TryRestoreBlock（全捕获）形成两种态度
            try
            {
                block.OnClear();
                if (TryRestoreBlock(block, snap, XSerialize.Serializer.Default.Format))
                {
                    // 恢复成功即视为已同步，清除该块脏标记；失败保留（数据可能半恢复，保守标记）
                    _dirtyBlocks.Remove(block);
                    return true;
                }
            }
            catch (Exception e)
            {
                LogManager.Exception(LogCategories.Data, e,
                    string.Format("恢复数据块 {0} 时抛出异常", snap.blockName));
            }

            return false;
        }

        #endregion

        #region Clear

        /// <inheritdoc/>
        public void ClearAll()
        {
            // 逐个隔离：某个块的 OnClear 抛异常不该让其余块的资源留着不释放。
            // 状态复位另放在 finally 里，任何单个失败都拦不住它——否则会停在
            // 「部分块已清、注册表却还完整」的半状态上（与 SettingsManager.Destroy 同一取舍）
            try
            {
                ForEachBlock(b =>
                {
                    try
                    {
                        b.OnClear();
                    }
                    catch (Exception e)
                    {
                        LogManager.Exception(LogCategories.Data, e,
                            string.Format("清空数据块 {0} 时抛出异常，已跳过并继续清空其余块", b.GetType().Name));
                    }
                });
            }
            finally
            {
                _blocks.Clear();
                _blockNameIndex.Clear();
                _dirtyBlocks.Clear();
            }
        }

        #endregion
    }
}