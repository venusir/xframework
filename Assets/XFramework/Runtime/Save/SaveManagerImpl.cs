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
    /// <para><b>线程约定：</b>文件 IO 由 Provider 在线程池上执行，本类每个公开异步方法都会在返回前切回主线程，
    /// 因此调用方在 <c>await</c> 之后可以安全地访问 Unity API 与 <see cref="DataManager"/>。
    /// 代价是这些方法依赖 PlayerLoop 泵，<b>禁止在主线程用 <c>.GetAwaiter().GetResult()</c> 同步阻塞等待</b>，
    /// 否则会死锁。</para>
    /// </summary>
    public sealed class SaveManagerImpl : ISaveManager
    {
        #region Constants

        private const string SlotFilePrefix = "slot_";
        private const string SlotFileSuffix = ".save";
        private const FileDomain SaveDomain = FileDomain.SaveData;

        #endregion

        #region Fields

        // 防御性兜底:FileManager.GetFilesAsync 已保证返回正斜杠路径,
        // 此处兼容反斜杠是为了防御第三方 Provider 违反契约的情况
        private static readonly char[] PathSeparators = { '/', '\\' };

        private string _playerId;

        #endregion

        #region ISaveManager

        /// <inheritdoc/>
        public bool IsBusy { get; private set; }

        /// <inheritdoc/>
        public string CurrentPlayerId => _playerId;

        /// <inheritdoc/>
        public async UniTask<List<SaveMeta>> GetSlotMetasAsync(CancellationToken cancellationToken = default)
        {
            var searchDir = _playerId ?? "";
            var files = await FileManager.GetFilesAsync(SaveDomain, searchDir, cancellationToken: cancellationToken);
            await ReturnToMainThread(cancellationToken);

            var metas = new List<SaveMeta>();

            if (files == null || files.Length == 0)
                return metas;

            for (int i = 0; i < files.Length; i++)
            {
                var path = files[i];
                if (!IsSlotFilePath(path))
                    continue;

                var bytes = await FileManager.ReadAllBytesAsync(SaveDomain, path, cancellationToken);
                await ReturnToMainThread(cancellationToken);

                if (bytes == null || bytes.Length == 0)
                    continue;

                try
                {
                    var saveData = (DataSnapshot)Serializer.Default.Deserialize(bytes, DataSnapshot.Factory().GetType());
                    if (saveData == null)
                        continue;

                    var meta = saveData.CreateMeta();
                    meta.playerId = _playerId;
                    meta.slot = ParseSlotFromPath(path);
                    meta.relativePath = path;
                    meta.fileSize = bytes.Length;
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
        public async UniTask<SaveMeta> GetSlotMetaAsync(int slot, CancellationToken cancellationToken = default)
        {
            var path = BuildSlotPath(slot);
            if (!FileManager.Exists(SaveDomain, path))
                return null;

            var bytes = await FileManager.ReadAllBytesAsync(SaveDomain, path, cancellationToken);
            await ReturnToMainThread(cancellationToken);

            if (bytes == null || bytes.Length == 0)
                return null;

            var saveData = (DataSnapshot)Serializer.Default.Deserialize(bytes, DataSnapshot.Factory().GetType());
            if (saveData == null)
                return null;

            var meta = saveData.CreateMeta();
            meta.playerId = _playerId;
            meta.slot = slot;
            meta.relativePath = path;
            meta.fileSize = bytes.Length;
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
                var bytes = Serializer.Default.Serialize(saveData, saveData.GetType());

                // 3. 原子写入：Provider 层先写 .tmp 再替换正式文件（IAtomicFileProvider 契约），
                //    写入中途崩溃不会损坏已有存档；Provider 不支持时门面自动降级普通写
                var slotPath = BuildSlotPath(slot);

                await FileManager.WriteAllBytesAtomicAsync(SaveDomain, slotPath, bytes, cancellationToken);
                await ReturnToMainThread(cancellationToken);

                var meta = saveData.CreateMeta();
                meta.playerId = _playerId;
                meta.slot = slot;
                meta.relativePath = slotPath;
                meta.fileSize = bytes.Length;
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
                await ReturnToMainThread(cancellationToken);

                if (bytes == null || bytes.Length == 0)
                    throw new InvalidOperationException($"[Save] 存档槽位 {slot} 为空文件。");

                // 反序列化 bytes → DataSnapshot
                var saveData = (DataSnapshot)Serializer.Default.Deserialize(bytes, DataSnapshot.Factory().GetType());

                // 应用快照到内存
                DataManager.ApplySnapshot(saveData);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <inheritdoc/>
        public async UniTask<bool> DeleteSlotAsync(int slot, CancellationToken cancellationToken = default)
        {
            var slotPath = BuildSlotPath(slot);

            var exists = await FileManager.ExistsAsync(SaveDomain, slotPath, cancellationToken);
            await ReturnToMainThread(cancellationToken);

            if (!exists)
                return false;

            FileManager.Delete(SaveDomain, slotPath);
            return true;
        }

        /// <inheritdoc/>
        public async UniTask<int> DeleteAllSlotsAsync(CancellationToken cancellationToken = default)
        {
            var searchDir = _playerId ?? "";
            var files = await FileManager.GetFilesAsync(SaveDomain, searchDir, cancellationToken: cancellationToken);
            await ReturnToMainThread(cancellationToken);

            if (files == null)
                return 0;

            var deleted = 0;
            for (int i = 0; i < files.Length; i++)
            {
                if (IsSlotFilePath(files[i]))
                {
                    FileManager.Delete(SaveDomain, files[i]);
                    deleted++;
                }
            }

            // 同时清理可能残留的 .tmp 文件（不计入返回的槽位数量）
            var tmpFiles = await FileManager.GetFilesAsync(SaveDomain, searchDir, "*.tmp", cancellationToken);
            await ReturnToMainThread(cancellationToken);

            if (tmpFiles != null)
            {
                for (int i = 0; i < tmpFiles.Length; i++)
                    FileManager.Delete(SaveDomain, tmpFiles[i]);
            }

            return deleted;
        }

        /// <inheritdoc/>
        public async UniTask<bool> SlotExistsAsync(int slot, CancellationToken cancellationToken = default)
        {
            var exists = await FileManager.ExistsAsync(SaveDomain, BuildSlotPath(slot), cancellationToken);
            await ReturnToMainThread(cancellationToken);

            return exists;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// 把续体切回主线程。
        /// <para>Provider 的文件 IO 统一走 <c>UniTask.RunOnThreadPool(..., configureAwait: false, ct)</c>，
        /// 该重载完成时<b>停留在子线程</b>——UniTask 中只有 <c>configureAwait</c> 为 <c>true</c> 的分支
        /// 才会执行 <c>await UniTask.Yield()</c> 回主线程。</para>
        /// <para>而 <see cref="DataManager"/>、第三方 <c>IDataBlock</c> 回调与 UnityEngine 日志都只能在主线程访问，
        /// 故每处 IO <c>await</c> 之后、触碰这些对象之前必须显式切回。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private static async UniTask ReturnToMainThread(CancellationToken cancellationToken)
        {
            await UniTask.SwitchToMainThread(cancellationToken);
        }

        private string BuildSlotPath(int slot)
        {
            var fileName = $"{SlotFilePrefix}{slot}{SlotFileSuffix}";
            if (_playerId == null)
                return fileName;

            // playerId 作为单段目录名：严格拒绝分隔符与 .. 穿越（路径沙箱第二道防线），
            // 防止 SetCurrentPlayer("../../") 注入导致存档写到域根之外
            if (_playerId.IndexOfAny(PathSeparators) >= 0 || _playerId == "..")
                throw new ArgumentException(
                    $"[Save] 非法 playerId '{_playerId}':不允许包含路径分隔符或 '..'。");

            return $"{_playerId}/{fileName}";
        }

        private static bool IsSlotFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // 取文件名部分（去掉可能的 playerId 子目录前缀）
            var fileName = FilePathUtility.GetFileNameFromPath(path);

            return fileName.StartsWith(SlotFilePrefix, StringComparison.Ordinal)
                && fileName.EndsWith(SlotFileSuffix, StringComparison.Ordinal);
        }

        private static int ParseSlotFromPath(string path)
        {
            // 取文件名部分，格式: "slot_{index}.save"（可能包含 playerId/ 前缀）
            var fileName = FilePathUtility.GetFileNameFromPath(path);

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

        #region Player Context

        /// <inheritdoc/>
        public void SetCurrentPlayer(string playerId)
        {
            _playerId = playerId;
        }

        /// <inheritdoc/>
        public void ClearCurrentPlayer()
        {
            _playerId = null;
        }

        /// <inheritdoc/>
        public async UniTask<string[]> GetAllPlayerIdsAsync(CancellationToken cancellationToken = default)
        {
            // 通过扫描 SaveData 目录下直接包含 .save 文件的子目录来识别玩家
            var rootFiles = await FileManager.GetFilesAsync(SaveDomain, "", cancellationToken: cancellationToken);
            await ReturnToMainThread(cancellationToken);

            var playerIdSet = new HashSet<string>();

            // 1. 收集根目录下以 playerId 子目录形式存在的玩家
            if (rootFiles != null)
            {
                for (int i = 0; i < rootFiles.Length; i++)
                {
                    var path = rootFiles[i];
                    var slashIndex = path.IndexOfAny(PathSeparators);
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

        /// <inheritdoc/>
        public async UniTask<int> DeletePlayerAsync(string playerId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(playerId))
                return 0;

            // 删除该玩家子目录下的所有 .save 文件
            var files = await FileManager.GetFilesAsync(SaveDomain, playerId, cancellationToken: cancellationToken);
            await ReturnToMainThread(cancellationToken);

            var deleted = 0;
            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (IsSlotFilePath(files[i]))
                    {
                        FileManager.Delete(SaveDomain, files[i]);
                        deleted++;
                    }
                }
            }

            // 同时删除可能残留的 .tmp 文件（不计入返回的槽位数量）
            var tmpFiles = await FileManager.GetFilesAsync(SaveDomain, playerId, "*.tmp", cancellationToken);
            await ReturnToMainThread(cancellationToken);

            if (tmpFiles != null)
            {
                for (int i = 0; i < tmpFiles.Length; i++)
                    FileManager.Delete(SaveDomain, tmpFiles[i]);
            }

            return deleted;
        }

        #endregion
    }
}