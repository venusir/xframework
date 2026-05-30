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

        #region ISaveManager

        /// <inheritdoc/>
        public bool IsBusy { get; private set; }

        /// <inheritdoc/>
        public async UniTask<List<SaveMeta>> GetSlotMetas(CancellationToken cancellationToken = default)
        {
            var files = await FileManager.GetFilesAsync(SaveDomain, "", cancellationToken: cancellationToken);
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

                    metas.Add(new SaveMeta
                    {
                        slot = ParseSlotFromPath(path),
                        version = saveData.version,
                        timestamp = saveData.timestamp,
                        relativePath = path,
                        fileSize = bytes.Length
                    });
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

            return new SaveMeta
            {
                slot = slot,
                version = saveData.version,
                timestamp = saveData.timestamp,
                relativePath = path,
                fileSize = bytes.Length
            };
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

                return new SaveMeta
                {
                    slot = slot,
                    version = saveData.version,
                    timestamp = saveData.timestamp,
                    relativePath = slotPath,
                    fileSize = bytes.Length
                };
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
            var files = await FileManager.GetFilesAsync(SaveDomain, "", cancellationToken: cancellationToken);
            if (files == null)
                return;

            for (int i = 0; i < files.Length; i++)
            {
                if (IsSlotFilePath(files[i]))
                    FileManager.Delete(SaveDomain, files[i]);
            }
        }

        /// <inheritdoc/>
        public bool SlotExists(int slot)
        {
            return FileManager.Exists(SaveDomain, BuildSlotPath(slot));
        }

        #endregion

        #region Private Helpers

        private static string BuildSlotPath(int slot)
        {
            return $"{SlotFilePrefix}{slot}{SlotFileSuffix}";
        }

        private static bool IsSlotFilePath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(SlotFilePrefix, StringComparison.Ordinal)
                && path.EndsWith(SlotFileSuffix, StringComparison.Ordinal);
        }

        private static int ParseSlotFromPath(string path)
        {
            // 格式: "slot_{index}.save"
            var start = SlotFilePrefix.Length;
            var end = path.LastIndexOf(SlotFileSuffix, StringComparison.Ordinal);
            if (end < start)
                return -1;

            var numStr = path.Substring(start, end - start);
            if (int.TryParse(numStr, out var slotIndex))
                return slotIndex;

            return -1;
        }

        #endregion
    }
}