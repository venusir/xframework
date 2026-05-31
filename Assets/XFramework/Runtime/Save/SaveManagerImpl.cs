using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XData;
using XFramework.XFileManager;
using XFramework.XSerialize;

namespace XFramework.XSave
{
    /// <summary>
    /// <see cref="ISaveManager"/> 的默认实现。
    /// <para>使用 <see cref="FileManager"/> 作为存储后端，<see cref="Serializer"/> 作为序列化层，
    /// <see cref="DataManager"/> 作为数据快照来源。</para>
    /// <para>存档文件位于 <see cref="FileDomain.SaveData"/> 下，文件名格式为 <c>slot_{slot}.save</c>。</para>
    /// <para>第三方可通过实现 <see cref="ISaveManager"/> 并注册到
    /// <see cref="SaveManager.Initialize(SaveManagerFactory)"/> 来替换此实现。</para>
    /// </summary>
    public sealed class SaveManagerImpl : ISaveManager
    {
        #region Constants

        private const string SlotFilePrefix = "slot_";
        private const string SlotFileSuffix = ".save";
        private const FileDomain SaveDomain = FileDomain.SaveData;

        #endregion

        #region Fields

        private string _playerId;

        #endregion

        #region ISaveManager

        /// <inheritdoc/>
        public bool IsBusy { get; private set; }

        /// <inheritdoc/>
        public async UniTask<List<SaveMeta>> GetSlotMetas(CancellationToken cancellationToken = default)
        {
            var searchDir = _playerId ?? "";
            var files = await FileManager.GetFilesAsync(SaveDomain, searchDir, cancellationToken: cancellationToken);
            var metas = new List<SaveMeta>();

            if (files == null || files.Length == 0)
                return metas;

            for (int i = 0; i < files.Length; i++)
            {
                var path = files[i];
                if (!IsSlotFilePath(path))
                    continue;

                var bytes = await FileManager.ReadAllBytesAsync(SaveDomain, path, cancellationToken);
                if (bytes == null || bytes.Length == 0)
                    continue;

                try
                {
                    var saveData = (DataSnapshot)Serializer.Default.Deserialize(bytes, typeof(DataSnapshot));
                    if (saveData == null)
                        continue;

                    var meta = SaveMeta.Factory();
                    meta.playerId = _playerId;
                    meta.slot = ParseSlotFromPath(path);
                    meta.version = saveData.version;
                    meta.timestamp = saveData.timestamp;
                    meta.relativePath = path;
                    meta.fileSize = bytes.Length;
                    meta.OnPopulate(saveData);
                    metas.Add(meta);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Save] 解析存档元数据失败: {path}, {ex.Message}");
                }
            }

            return metas;
        }

        /// <inheritdoc/>
        public async UniTask<SaveMeta> GetSlotMeta(int slot, CancellationToken cancellationToken = default)
        {
            var path = BuildSlotPath(slot);
            if (!FileManager.Exists(SaveDomain, path))
                return null;

            var bytes = await FileManager.ReadAllBytesAsync(SaveDomain, path, cancellationToken);
            if (bytes == null || bytes.Length == 0)
                return null;

            var saveData = (DataSnapshot)Serializer.Default.Deserialize(bytes, typeof(DataSnapshot));
            if (saveData == null)
                return null;

            var meta = SaveMeta.Factory();
            meta.playerId = _playerId;
            meta.slot = slot;
            meta.version = saveData.version;
            meta.timestamp = saveData.timestamp;
            meta.relativePath = path;
            meta.fileSize = bytes.Length;
            meta.OnPopulate(saveData);
            return meta;
        }

        /// <inheritdoc/>
        public async UniTask<SaveMeta> SaveAsync(int slot, CancellationToken cancellationToken = default)
        {
            if (IsBusy)
                throw new InvalidOperationException("[Save] 上一次保存/加载操作尚未完成。");

            IsBusy = true;
            try
            {
                // 1. 收集数据快照
                var saveData = DataManager.CreateSnapshot();

                // 2. 序列化 DataSnapshot → bytes
                var bytes = Serializer.Default.Serialize(saveData, typeof(DataSnapshot));

                // 3. 双缓冲写入：先写 .tmp，再删除正式文件，最后重命名 .tmp → 正式文件
                //    避免写入中途崩溃导致存档损坏。
                var slotPath = BuildSlotPath(slot);
                var tempPath = slotPath + ".tmp";

                await FileManager.WriteAllBytesAsync(SaveDomain, tempPath, bytes, cancellationToken);

                if (FileManager.Exists(SaveDomain, slotPath))
                    FileManager.Delete(SaveDomain, slotPath);

                var srcPhysical = FileManager.GetPhysicalPath(SaveDomain, tempPath);
                var dstPhysical = FileManager.GetPhysicalPath(SaveDomain, slotPath);
                System.IO.File.Move(srcPhysical, dstPhysical);

                var meta = SaveMeta.Factory();
                meta.playerId = _playerId;
                meta.slot = slot;
                meta.version = saveData.version;
                meta.timestamp = saveData.timestamp;
                meta.relativePath = slotPath;
                meta.fileSize = bytes.Length;
                meta.OnPopulate(saveData);
                return meta;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <inheritdoc/>
        public async UniTask LoadAsync(int slot, CancellationToken cancellationToken = default)
        {
            if (IsBusy)
                throw new InvalidOperationException("[Save] 上一次保存/加载操作尚未完成。");

            var slotPath = BuildSlotPath(slot);
            if (!FileManager.Exists(SaveDomain, slotPath))
                throw new InvalidOperationException($"[Save] 存档槽位 {slot} 不存在，无法加载。");

            IsBusy = true;
            try
            {
                var bytes = await FileManager.ReadAllBytesAsync(SaveDomain, slotPath, cancellationToken);
                if (bytes == null || bytes.Length == 0)
                    throw new InvalidOperationException($"[Save] 存档槽位 {slot} 为空文件。");

                // 反序列化 bytes → DataSnapshot
                var saveData = (DataSnapshot)Serializer.Default.Deserialize(bytes, typeof(DataSnapshot));

                // 应用快照到内存
                DataManager.ApplySnapshot(saveData);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <inheritdoc/>
        public void DeleteSlot(int slot)
        {
            var slotPath = BuildSlotPath(slot);
            if (FileManager.Exists(SaveDomain, slotPath))
                FileManager.Delete(SaveDomain, slotPath);
        }

        /// <inheritdoc/>
        public async UniTask DeleteAllSlotsAsync(CancellationToken cancellationToken = default)
        {
            var searchDir = _playerId ?? "";
            var files = await FileManager.GetFilesAsync(SaveDomain, searchDir, cancellationToken: cancellationToken);
            if (files == null)
                return;

            for (int i = 0; i < files.Length; i++)
            {
                if (IsSlotFilePath(files[i]))
                    FileManager.Delete(SaveDomain, files[i]);
            }

            // 同时清理可能残留的 .tmp 文件
            var tmpFiles = await FileManager.GetFilesAsync(SaveDomain, searchDir, "*.tmp", cancellationToken);
            if (tmpFiles != null)
            {
                for (int i = 0; i < tmpFiles.Length; i++)
                    FileManager.Delete(SaveDomain, tmpFiles[i]);
            }
        }

        /// <inheritdoc/>
        public bool SlotExists(int slot)
        {
            return FileManager.Exists(SaveDomain, BuildSlotPath(slot));
        }

        #endregion

        #region Private Helpers

        private string BuildSlotPath(int slot)
        {
            var fileName = $"{SlotFilePrefix}{slot}{SlotFileSuffix}";
            return _playerId != null ? $"{_playerId}/{fileName}" : fileName;
        }

        private static bool IsSlotFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // 取文件名部分（去掉可能的 playerId 子目录前缀）
            var fileName = path;
            var slashIndex = path.LastIndexOf('/');
            if (slashIndex >= 0)
                fileName = path.Substring(slashIndex + 1);

            return fileName.StartsWith(SlotFilePrefix, StringComparison.Ordinal)
                && fileName.EndsWith(SlotFileSuffix, StringComparison.Ordinal);
        }

        private static int ParseSlotFromPath(string path)
        {
            // 取文件名部分，格式: "slot_{index}.save"（可能包含 playerId/ 前缀）
            var fileName = path;
            var slashIndex = path.LastIndexOf('/');
            if (slashIndex >= 0)
                fileName = path.Substring(slashIndex + 1);

            var start = SlotFilePrefix.Length;
            var end = fileName.LastIndexOf(SlotFileSuffix, StringComparison.Ordinal);
            if (end < start)
                return -1;

            var numStr = fileName.Substring(start, end - start);
            if (int.TryParse(numStr, out var slotIndex))
                return slotIndex;

            return -1;
        }

        #endregion

        #region User Management

        /// <summary>
        /// 设置当前操作玩家 ID。传入 <c>null</c> 等同于清除玩家上下文。
        /// </summary>
        internal void SetPlayerId(string playerId)
        {
            _playerId = playerId;
        }

        /// <summary>
        /// 清除玩家上下文。
        /// </summary>
        internal void ClearPlayerId()
        {
            _playerId = null;
        }

        /// <summary>
        /// 获取所有存在存档数据的玩家 ID 列表。
        /// </summary>
        internal async UniTask<string[]> GetAllPlayerIdsAsync(CancellationToken cancellationToken = default)
        {
            // 通过扫描 SaveData 目录下直接包含 .save 文件的子目录来识别玩家
            var rootFiles = await FileManager.GetFilesAsync(SaveDomain, "", cancellationToken: cancellationToken);
            var playerIdSet = new HashSet<string>();

            // 1. 收集根目录下以 playerId 子目录形式存在的玩家
            if (rootFiles != null)
            {
                for (int i = 0; i < rootFiles.Length; i++)
                {
                    var path = rootFiles[i];
                    var slashIndex = path.IndexOf('/');
                    if (slashIndex > 0)
                    {
                        var playerId = path.Substring(0, slashIndex);
                        if (IsSlotFilePath(path))
                            playerIdSet.Add(playerId);
                    }
                }
            }

            // 2. 同时也检查根目录下直接存在的存档（无玩家上下文的遗留存档）
            // 这些没有 playerId，但 GetAllPlayerIds 只返回有明确 playerId 的玩家

            var result = new string[playerIdSet.Count];
            playerIdSet.CopyTo(result);
            return result;
        }

        /// <summary>
        /// 删除指定玩家的所有存档数据（包括子目录）。
        /// </summary>
        internal async UniTask DeletePlayerAsync(string playerId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(playerId))
                return;

            // 删除该玩家子目录下的所有 .save 文件
            var files = await FileManager.GetFilesAsync(SaveDomain, playerId, cancellationToken: cancellationToken);
            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (IsSlotFilePath(files[i]))
                        FileManager.Delete(SaveDomain, files[i]);
                }
            }

            // 同时删除可能残留的 .tmp 文件
            var tmpFiles = await FileManager.GetFilesAsync(SaveDomain, playerId, "*.tmp", cancellationToken);
            if (tmpFiles != null)
            {
                for (int i = 0; i < tmpFiles.Length; i++)
                    FileManager.Delete(SaveDomain, tmpFiles[i]);
            }
        }

        #endregion
    }
}
