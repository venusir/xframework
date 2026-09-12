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

        private const FileDomain SaveDomain = FileDomain.SaveData;

        #endregion

        #region Fields

        // 防御性兜底:FileManager.GetFilesAsync 已保证返回正斜杠路径,
        // 此处兼容反斜杠是为了防御第三方 Provider 违反契约的情况
        private static readonly char[] PathSeparators = { '/', '\\' };

        /// <summary>
        /// 当前玩家上下文。仅由 <see cref="SetCurrentPlayer"/>/<see cref="ClearCurrentPlayer"/> 写入。
        /// <para><b>不变式：</b>每个异步方法在入口处把本字段复制到局部变量，<c>await</c> 之后一律使用局部变量。
        /// 直接读字段会与并发切换玩家上下文产生竞态——表现为存档写进错误的玩家目录、或元数据归属错误。</para>
        /// </summary>
        private string _playerId;

        /// <summary>
        /// 写操作占位标志：0 = 空闲，1 = 占用。
        /// <para>用 <see cref="Interlocked"/> 而非普通 <c>bool</c>：本类每个 IO <c>await</c> 之后的续体
        /// 可能落在池线程上，<c>if (IsBusy) throw; IsBusy = true;</c> 这类 check-then-act 存在竞态，
        /// 且普通字段跨线程读取无可见性保证。</para>
        /// </summary>
        private int _busyFlag;

        #endregion

        #region ISaveManager

        /// <inheritdoc/>
        public bool IsBusy => Volatile.Read(ref _busyFlag) != 0;

        /// <inheritdoc/>
        public string CurrentPlayerId => _playerId;

        /// <inheritdoc/>
        public UniTask<List<SaveMeta>> GetSlotMetasAsync(CancellationToken cancellationToken = default)
        {
            // 只把玩家上下文当作默认值：实际查询由参数驱动，不读也不写共享状态，
            // 因此并发调用互不干扰，也不会在 await 处被其他操作观察到中间态
            return GetPlayerSlotMetasAsync(_playerId, cancellationToken);
        }

        /// <inheritdoc/>
        public async UniTask<List<SaveMeta>> GetPlayerSlotMetasAsync(string playerId, CancellationToken cancellationToken = default)
        {
            // null/空表示「无玩家隔离的域根目录」，是契约允许的取值；
            // 只有非空值才需要校验（挡住路径穿越与跨平台非法字符）
            if (!string.IsNullOrEmpty(playerId))
                SavePathUtility.ValidatePlayerId(playerId);

            var searchDir = playerId ?? "";
            var files = await FileManager.GetFilesAsync(SaveDomain, searchDir, cancellationToken: cancellationToken);
            await ReturnToMainThread(cancellationToken);

            var metas = new List<SaveMeta>();

            if (files == null || files.Length == 0)
                return metas;

            // 快照类型在循环外取一次：Factory 是可变静态字段，不能缓存到字段/静态里
            // （第三方随时可替换），但也不必每个文件都构造一个快照实例
            var snapshotType = DataSnapshot.Factory().GetType();

            for (int i = 0; i < files.Length; i++)
            {
                var path = files[i];

                // 严格解析：非 slot_<非负整数>.save 的文件直接不认，不产生 slot = -1 的脏元数据
                if (!SavePathUtility.TryParseSlot(path, out var slot))
                    continue;

                byte[] bytes = null;
                try
                {
                    // 读取也在 try 内：读失败与解析失败对调用方是同一种故障（该槽位不可用）
                    bytes = await FileManager.ReadAllBytesAsync(SaveDomain, path, cancellationToken);
                    await ReturnToMainThread(cancellationToken);

                    if (bytes == null)
                        continue;   // 列举与读取之间文件消失，按不存在处理

                    if (bytes.Length == 0)
                    {
                        Debug.LogWarning($"[Save] 跳过空存档文件: {path}");
                        continue;
                    }

                    if (!TryDeserializeSnapshot(bytes, snapshotType, out var saveData, out var error))
                    {
                        Debug.LogWarning($"[Save] 解析存档元数据失败: {path}, {error}");
                        metas.Add(BuildCorruptedMeta(playerId, slot, path, bytes.Length));
                        continue;
                    }

                    metas.Add(BuildMeta(saveData, playerId, slot, path, bytes.Length));
                }
                catch (OperationCanceledException)
                {
                    // 取消不是「文件损坏」，必须向上传播：被下面的裸 catch 吞掉会让调用方
                    // 拿到一份「看起来正常」的部分列表
                    throw;
                }
                catch (Exception ex)
                {
                    // CreateMeta 是第三方扩展点，其异常不应打崩整份列表
                    Debug.LogWarning($"[Save] 解析存档元数据失败: {path}, {ex.Message}");
                    metas.Add(BuildCorruptedMeta(playerId, slot, path, bytes?.Length ?? 0));
                }
            }

            // 排序：文件系统返回顺序跨平台不确定，升序让 UI 与断言都有稳定预期
            metas.Sort(SlotAscendingComparer);
            return metas;
        }

        /// <inheritdoc/>
        public async UniTask<SaveMeta> GetSlotMetaAsync(int slot, CancellationToken cancellationToken = default)
        {
            SavePathUtility.ValidateSlot(slot);

            // 入口捕获玩家上下文：await 之后不再读字段，避免与并发切换玩家产生竞态
            var playerId = _playerId;
            var path = SavePathUtility.BuildSlotPath(playerId, slot);

            if (!FileManager.Exists(SaveDomain, path))
                return null;

            var bytes = await FileManager.ReadAllBytesAsync(SaveDomain, path, cancellationToken);
            await ReturnToMainThread(cancellationToken);

            if (bytes == null)
                return null;

            if (bytes.Length == 0)
            {
                Debug.LogWarning($"[Save] 跳过空存档文件: {path}");
                return null;
            }

            // 与 GetSlotMetasAsync 同一错误处置：损坏即告警并返回带标记的元数据。
            // 返回带标记条目而非 null，是为了让两个元数据 API 结论一致——
            // null 严格表示「该槽位不存在」，而不是「存在但读不了」
            if (!TryDeserializeSnapshot(bytes, DataSnapshot.Factory().GetType(), out var saveData, out var error))
            {
                Debug.LogWarning($"[Save] 解析存档元数据失败: {path}, {error}");
                return BuildCorruptedMeta(playerId, slot, path, bytes.Length);
            }

            return BuildMeta(saveData, playerId, slot, path, bytes.Length);
        }

        /// <inheritdoc/>
        public async UniTask<SaveMeta> SaveAsync(int slot, CancellationToken cancellationToken = default)
        {
            SavePathUtility.ValidateSlot(slot);

            // 入口捕获玩家上下文：await 之后不再读字段，避免与并发切换玩家产生竞态
            var playerId = _playerId;

            EnterBusy();
            try
            {
                // 1. 收集数据快照
                var saveData = DataManager.CreateSnapshot();

                // 2. 序列化 DataSnapshot → bytes
                var bytes = Serializer.Default.Serialize(saveData, saveData.GetType());

                // 3. 原子写入：Provider 层先写 .tmp 再替换正式文件（IAtomicFileProvider 契约），
                //    写入中途崩溃不会损坏已有存档；Provider 不支持时门面自动降级普通写
                var slotPath = SavePathUtility.BuildSlotPath(playerId, slot);

                await FileManager.WriteAllBytesAtomicAsync(SaveDomain, slotPath, bytes, cancellationToken);
                await ReturnToMainThread(cancellationToken);

                return BuildMeta(saveData, playerId, slot, slotPath, bytes.Length);
            }
            finally
            {
                ExitBusy();
            }
        }

        /// <inheritdoc/>
        public async UniTask LoadAsync(int slot, CancellationToken cancellationToken = default)
        {
            SavePathUtility.ValidateSlot(slot);

            var playerId = _playerId;
            var slotPath = SavePathUtility.BuildSlotPath(playerId, slot);
            if (!FileManager.Exists(SaveDomain, slotPath))
                throw new InvalidOperationException($"[Save] 存档槽位 {slot} 不存在，无法加载。");

            EnterBusy();
            try
            {
                var bytes = await FileManager.ReadAllBytesAsync(SaveDomain, slotPath, cancellationToken);
                await ReturnToMainThread(cancellationToken);

                // 读失败（null）与空文件是两种不同故障：前者多为解密失败或文件被并发删除，
                // 后者说明文件确实存在但无内容——分开报错才好定位
                if (bytes == null)
                    throw new InvalidOperationException($"[Save] 存档槽位 {slot} 读取失败（文件不存在或解密失败）。");

                if (bytes.Length == 0)
                    throw new InvalidOperationException($"[Save] 存档槽位 {slot} 为空文件。");

                // 结构校验不通过即拒绝应用：绝不能让「合法 JSON 但不是存档」的内容走到 ApplySnapshot，
                // 它会先清空全部数据块、再因数据块列表为空直接返回，等于静默清空玩家的内存数据
                if (!TryDeserializeSnapshot(bytes, DataSnapshot.Factory().GetType(), out var saveData, out var error))
                    throw new InvalidOperationException($"[Save] 存档槽位 {slot} 已损坏或不是有效存档：{error}");

                // 应用快照到内存
                DataManager.ApplySnapshot(saveData);
            }
            finally
            {
                ExitBusy();
            }
        }

        /// <inheritdoc/>
        public async UniTask<bool> DeleteSlotAsync(int slot, CancellationToken cancellationToken = default)
        {
            SavePathUtility.ValidateSlot(slot);

            var playerId = _playerId;
            var slotPath = SavePathUtility.BuildSlotPath(playerId, slot);

            EnterBusy();
            try
            {
                var exists = await FileManager.ExistsAsync(SaveDomain, slotPath, cancellationToken);
                await ReturnToMainThread(cancellationToken);

                if (!exists)
                    return false;

                FileManager.Delete(SaveDomain, slotPath);
                return true;
            }
            finally
            {
                ExitBusy();
            }
        }

        /// <inheritdoc/>
        public async UniTask<int> DeleteAllSlotsAsync(CancellationToken cancellationToken = default)
        {
            var searchDir = _playerId ?? "";

            EnterBusy();
            try
            {
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
            finally
            {
                ExitBusy();
            }
        }

        /// <inheritdoc/>
        public async UniTask<bool> SlotExistsAsync(int slot, CancellationToken cancellationToken = default)
        {
            // 谓词语义：非法槽位按「不存在」返回 false 而非抛异常——UI 轮询槽位时不该被迫到处包 try。
            // 这是与其它入口刻意保留的不对称，已写入接口注释
            if (slot < 0)
                return false;

            var playerId = _playerId;

            var exists = await FileManager.ExistsAsync(SaveDomain, SavePathUtility.BuildSlotPath(playerId, slot), cancellationToken);
            await ReturnToMainThread(cancellationToken);

            return exists;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// 原子占用写操作名额。
        /// <para>写操作（保存/加载/删除）共用一道门禁：存档文件替换期间被并发删除会破坏原子写语义，
        /// 而删槽位恰好会在「已写 <c>.tmp</c>、尚未替换」的窗口里把目标文件抽走，导致存档丢失。</para>
        /// <para><b>读操作刻意不进保护</b>（GetSlotMetas / GetSlotMeta / SlotExists / GetAllPlayerIds）：
        /// 替换是原子的，读只会看到「旧的完整文件」或「新的完整文件」，永远不会读到半截；
        /// 给读加门禁只会让存档 UI 在保存进行中莫名抛异常。</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">已有写操作未完成时抛出。</exception>
        private void EnterBusy()
        {
            if (Interlocked.CompareExchange(ref _busyFlag, 1, 0) != 0)
                throw new InvalidOperationException("[Save] 上一次保存/加载操作尚未完成。");
        }

        /// <summary>
        /// 释放写操作名额。
        /// </summary>
        private void ExitBusy()
        {
            Interlocked.Exchange(ref _busyFlag, 0);
        }

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

        /// <summary>
        /// 反序列化存档字节并做最小结构校验：结果非 <c>null</c>、含数据块列表、版本号 <c>&gt;= 1</c>。
        /// <para><b>为什么必须校验：</b>内容为合法 JSON 但不是存档的文件（如 <c>{"foo":1}</c>）也能
        /// 反序列化出一个 <see cref="DataSnapshot"/>，而 <see cref="DataSnapshot.blocks"/> 有字段初始化器
        /// <c>= new()</c>，于是得到的是<b>空列表而非 <c>null</c></b>。
        /// <see cref="DataManager.ApplySnapshot"/> 会先 <c>OnClear</c> 掉全部数据块、再因列表为空直接返回，
        /// 结果是零警告地清空内存中的全部游戏数据。校验失败即拒绝应用，由调用方决定跳过还是抛出。</para>
        /// <para>版本号下界取 1 的依据：<see cref="DataManager.CreateSnapshot"/> 在版本为 0 时必然写成 1，
        /// 框架产出的存档必然满足；不满足即说明内容不是本框架写出的存档。</para>
        /// </summary>
        /// <param name="bytes">存档文件字节。</param>
        /// <param name="snapshotType">快照类型，取自 <c>DataSnapshot.Factory().GetType()</c>。</param>
        /// <param name="saveData">反序列化并校验通过的快照；失败时为 <c>null</c>。</param>
        /// <param name="error">失败原因；成功时为 <c>null</c>。</param>
        /// <returns>校验通过返回 <c>true</c>。</returns>
        private static bool TryDeserializeSnapshot(byte[] bytes, Type snapshotType, out DataSnapshot saveData, out string error)
        {
            saveData = null;
            error = null;

            DataSnapshot data;
            try
            {
                data = (DataSnapshot)Serializer.Default.Deserialize(bytes, snapshotType);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (data == null)
            {
                error = "反序列化结果为空";
                return false;
            }

            if (data.blocks == null)
            {
                error = "缺少数据块列表";
                return false;
            }

            if (data.version < 1)
            {
                error = $"版本号非法({data.version})";
                return false;
            }

            saveData = data;
            return true;
        }

        /// <summary>
        /// 由快照构造槽位元数据并补齐运行时字段。
        /// <para><paramref name="playerId"/> 以参数传入而非读取字段：本方法在 IO <c>await</c> 之后调用，
        /// 读字段会与并发切换玩家上下文产生竞态（表现为元数据归属错误）。</para>
        /// </summary>
        /// <param name="saveData">已反序列化的快照。</param>
        /// <param name="playerId">该存档所属玩家。</param>
        /// <param name="slot">槽位号。</param>
        /// <param name="relativePath">存档文件相对路径。</param>
        /// <param name="fileSize">文件字节数。</param>
        /// <returns>补齐后的元数据。</returns>
        private static SaveMeta BuildMeta(DataSnapshot saveData, string playerId, int slot, string relativePath, long fileSize)
        {
            return FillMeta(saveData.CreateMeta(), playerId, slot, relativePath, fileSize);
        }

        /// <summary>
        /// 为无法解析的存档构造带损坏标记的元数据。
        /// <para>经由 <c>Factory().CreateMeta()</c> 而非 <c>new SaveMeta()</c>：第三方扩展的
        /// <see cref="SaveMeta"/> 子类字段需要与配对快照一起构造，损坏文件没有可用快照，
        /// 只能给一个未填充的实例（其扩展字段为默认值）。</para>
        /// </summary>
        /// <param name="playerId">所属玩家。</param>
        /// <param name="slot">槽位号（取自文件名）。</param>
        /// <param name="relativePath">存档文件相对路径。</param>
        /// <param name="fileSize">文件字节数；读取失败时为 0。</param>
        /// <returns>带 <see cref="SaveMeta.isCorrupted"/> 标记的元数据。</returns>
        private static SaveMeta BuildCorruptedMeta(string playerId, int slot, string relativePath, long fileSize)
        {
            var meta = FillMeta(DataSnapshot.Factory().CreateMeta(), playerId, slot, relativePath, fileSize);
            meta.isCorrupted = true;
            return meta;
        }

        /// <summary>
        /// 补齐元数据的运行时字段。
        /// <para><paramref name="playerId"/> 以参数传入而非读取字段：本方法在 IO <c>await</c> 之后调用，
        /// 读字段会与并发切换玩家上下文产生竞态（表现为元数据归属错误）。</para>
        /// </summary>
        private static SaveMeta FillMeta(SaveMeta meta, string playerId, int slot, string relativePath, long fileSize)
        {
            meta.playerId = playerId;
            meta.slot = slot;
            meta.relativePath = relativePath;
            meta.fileSize = fileSize;
            return meta;
        }

        /// <summary>按槽位号升序比较。静态单例，避免每次排序都分配比较器。</summary>
        private static readonly IComparer<SaveMeta> SlotAscendingComparer = new SlotAscending();

        private sealed class SlotAscending : IComparer<SaveMeta>
        {
            public int Compare(SaveMeta x, SaveMeta y)
            {
                return x.slot.CompareTo(y.slot);
            }
        }

        /// <summary>
        /// 判断路径是否为槽位文件——<b>宽松判断，只用于删除路径</b>。
        /// <para>删除语义是「清掉所有 <c>slot_</c> 前缀残留」，因此 <c>slot_abc.save</c> 这类
        /// 解析不出槽位号的文件也应被清理；而枚举只列合法槽位，用的是
        /// <see cref="SavePathUtility.TryParseSlot"/>。两者语义不同，刻意不做统一。</para>
        /// </summary>
        /// <param name="path">文件相对路径。</param>
        /// <returns>文件名以前缀开头、以后缀结尾返回 <c>true</c>。</returns>
        private static bool IsSlotFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // 取文件名部分（去掉可能的 playerId 子目录前缀）
            var fileName = FilePathUtility.GetFileNameFromPath(path);

            return fileName.StartsWith(SavePathUtility.SlotFilePrefix, StringComparison.Ordinal)
                && fileName.EndsWith(SavePathUtility.SlotFileSuffix, StringComparison.Ordinal);
        }

        #endregion

        #region Player Context

        /// <inheritdoc/>
        public void SetCurrentPlayer(string playerId)
        {
            // 契约允许 null/空表示清除上下文；非空值先校验再落地——
            // 校验失败时 _playerId 保持原值，不会留下「半切换」的玩家上下文
            if (string.IsNullOrEmpty(playerId))
            {
                _playerId = null;
                return;
            }

            SavePathUtility.ValidatePlayerId(playerId);
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
            // 空 playerId 原先是静默 no-op，调用方无法区分「删掉了 0 个」与「参数给错了」。
            // 要删除无玩家隔离的域根目录存档，应在无玩家上下文时调用 DeleteAllSlotsAsync
            if (string.IsNullOrEmpty(playerId))
                throw new ArgumentException("[Save] playerId 不能为空。", nameof(playerId));

            SavePathUtility.ValidatePlayerId(playerId);

            EnterBusy();
            try
            {
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
            finally
            {
                ExitBusy();
            }
        }

        #endregion
    }
}