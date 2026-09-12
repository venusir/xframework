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

        /// <summary>
        /// 删除路径使用的文件搜索模式：一次覆盖槽位载荷（<c>slot_N.save</c>）与其全部配套文件
        /// （<c>.meta</c> 侧车 / <c>.bak</c> 备份 / <c>.tmp</c> 残留）。
        /// </summary>
        private const string SlotFileSearchPattern = "slot_*";

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
                    // 侧车优先：小文件，避免为了拿元数据而读入并反序列化整个载荷。
                    // 侧车路径刻意不校验校验和——校验需要读载荷，那正是这里要避免的开销；
                    // 校验发生在 LoadAsync（那时载荷字节已在手上，几乎免费）
                    var sidecar = await TryReadSidecarAsync(playerId, slot, path, cancellationToken);
                    if (sidecar != null)
                    {
                        metas.Add(sidecar);
                        continue;
                    }

                    // 无侧车则回退全量解析（旧版本写出的存档、或侧车被删）
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

            // 与列表同一策略：侧车优先，缺失才回退全量解析
            var sidecar = await TryReadSidecarAsync(playerId, slot, path, cancellationToken);
            if (sidecar != null)
                return sidecar;

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

                var meta = BuildMeta(saveData, playerId, slot, slotPath, bytes.Length);
                meta.checksum = SaveIntegrity.Compute(bytes);

                // 侧车后写：先保证载荷落盘——侧车缺失只让枚举退化为全量解析，
                // 而侧车先于载荷存在会让校验和指向一份并不存在的内容
                await WriteSidecarAsync(meta, cancellationToken);

                return meta;
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

                // 校验和在此刻校验：载荷字节已在手上，计算几乎免费。
                // checksum 为 0 表示侧车未记录（或没有侧车），此时跳过——
                // 侧车只是加速层，不该因为它缺失就让存档不可读
                var sidecar = await TryReadSidecarAsync(playerId, slot, slotPath, cancellationToken);
                if (sidecar != null && sidecar.checksum != 0 && sidecar.checksum != SaveIntegrity.Compute(bytes))
                    throw new InvalidOperationException(
                        $"[Save] 存档槽位 {slot} 校验和不符，内容可能已损坏（侧车记录值与载荷实际内容不匹配）。");

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

                DeleteSlotFiles(slotPath);
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
                // 一次枚举覆盖槽位载荷与其全部配套文件（.meta / .bak / .tmp）
                var files = await FileManager.GetFilesAsync(SaveDomain, searchDir, SlotFileSearchPattern, cancellationToken);
                await ReturnToMainThread(cancellationToken);

                if (files == null)
                    return 0;

                var deleted = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    // 只把合法槽位载荷计入返回值；配套文件与解析不出槽位号的残留一并清理但不计数
                    if (SavePathUtility.TryParseSlot(files[i], out _))
                        deleted++;

                    FileManager.Delete(SaveDomain, files[i]);
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
        /// 写出元数据侧车（原子写）。
        /// </summary>
        /// <param name="meta">已补齐运行时字段的元数据，其 <see cref="SaveMeta.relativePath"/> 指向载荷。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async UniTask WriteSidecarAsync(SaveMeta meta, CancellationToken cancellationToken)
        {
            var metaPath = meta.relativePath + SavePathUtility.MetaFileSuffix;
            var metaBytes = Serializer.Default.Serialize(meta, meta.GetType());

            await FileManager.WriteAllBytesAtomicAsync(SaveDomain, metaPath, metaBytes, cancellationToken);
            await ReturnToMainThread(cancellationToken);
        }

        /// <summary>
        /// 尝试读取槽位文件的元数据侧车。
        /// <para>失败一律返回 <c>null</c> 交由调用方回退到全量反序列化，而不是抛异常：
        /// 侧车是可选加速层，它的缺失或损坏不该让存档变得不可读。</para>
        /// </summary>
        /// <param name="playerId">所属玩家。</param>
        /// <param name="slot">槽位号（以文件名为准，覆盖侧车里的值）。</param>
        /// <param name="savePath">载荷文件相对路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>侧车元数据；缺失、损坏或内容为空时返回 <c>null</c>。</returns>
        private async UniTask<SaveMeta> TryReadSidecarAsync(string playerId, int slot, string savePath, CancellationToken cancellationToken)
        {
            var bytes = await FileManager.ReadAllBytesAsync(SaveDomain, savePath + SavePathUtility.MetaFileSuffix, cancellationToken);
            await ReturnToMainThread(cancellationToken);

            if (bytes == null || bytes.Length == 0)
                return null;

            try
            {
                // 类型取自 Factory 生成的配对元数据，第三方扩展的 SaveMeta 子类字段才能还原
                var prototype = DataSnapshot.Factory().CreateMeta();
                var meta = (SaveMeta)Serializer.Default.Deserialize(bytes, prototype.GetType());
                if (meta == null)
                    return null;

                // 槽位号与归属一律以文件名/参数为准：侧车可能陈旧（如被外部工具改过载荷），
                // 文件名永远权威——这样即便侧车过期也绝不会把槽位列错
                meta.slot = slot;
                meta.playerId = playerId;
                meta.relativePath = savePath;
                return meta;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Save] 元数据侧车解析失败，回退全量解析: {savePath}, {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 删除槽位文件及其全部配套文件（侧车 / 备份 / 临时文件）。
        /// </summary>
        /// <param name="slotPath">槽位载荷的相对路径。</param>
        private static void DeleteSlotFiles(string slotPath)
        {
            FileManager.Delete(SaveDomain, slotPath);
            FileManager.Delete(SaveDomain, slotPath + SavePathUtility.MetaFileSuffix);
            FileManager.Delete(SaveDomain, slotPath + FilePathUtility.BackupFileSuffix);
            FileManager.Delete(SaveDomain, slotPath + FilePathUtility.TempFileSuffix);
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
            // 枚举域根下的直接子目录，逐个确认是否真有存档。
            // 不能靠 GetFilesAsync 的返回路径来识别玩家：它是非递归的，根目录返回的路径
            // 永远不含分隔符，靠切前缀判断的分支永不成立——这正是本接口长期恒返回空数组的原因
            var directories = await FileManager.GetDirectoriesAsync(SaveDomain, "", cancellationToken);
            await ReturnToMainThread(cancellationToken);

            if (directories == null || directories.Length == 0)
                return Array.Empty<string>();

            // 目录列表不存在重复项，无需去重集合
            var playerIds = new List<string>(directories.Length);
            for (int i = 0; i < directories.Length; i++)
            {
                // Provider 契约返回的是相对路径；防御性去掉可能的分隔符尾巴，
                // 并跳过更深的路径（本层枚举只应得到直接子目录）
                var playerId = directories[i].TrimEnd(PathSeparators);
                if (string.IsNullOrEmpty(playerId) || playerId.IndexOfAny(PathSeparators) >= 0)
                    continue;

                // 只认真正含存档文件的目录：删除玩家后残留的空目录不应产生幽灵条目
                if (await HasAnySlotFileAsync(playerId, cancellationToken))
                    playerIds.Add(playerId);
            }

            // 排序：目录枚举顺序跨平台不确定
            playerIds.Sort(StringComparer.Ordinal);

            return playerIds.ToArray();
        }

        /// <summary>
        /// 判断指定玩家目录下是否至少存在一个可解析的槽位文件。
        /// </summary>
        /// <param name="playerId">玩家 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存在可解析的槽位文件返回 <c>true</c>。</returns>
        private async UniTask<bool> HasAnySlotFileAsync(string playerId, CancellationToken cancellationToken)
        {
            var files = await FileManager.GetFilesAsync(SaveDomain, playerId, cancellationToken: cancellationToken);
            await ReturnToMainThread(cancellationToken);

            if (files == null)
                return false;

            for (int i = 0; i < files.Length; i++)
            {
                if (SavePathUtility.TryParseSlot(files[i], out _))
                    return true;
            }

            return false;
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
                // 一次枚举覆盖槽位载荷与其全部配套文件（.meta / .bak / .tmp）
                var files = await FileManager.GetFilesAsync(SaveDomain, playerId, SlotFileSearchPattern, cancellationToken);
                await ReturnToMainThread(cancellationToken);

                var deleted = 0;
                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        // 只把合法槽位载荷计入返回值；配套文件与解析不出槽位号的残留一并清理但不计数
                        if (SavePathUtility.TryParseSlot(files[i], out _))
                            deleted++;

                        FileManager.Delete(SaveDomain, files[i]);
                    }
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