using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XMessage;
using XFramework.XMessage.Internal;
using XFramework.XUpdate;

namespace XFramework.XSettings
{
    /// <summary>
    /// <see cref="ISettingsManager{T}"/> 的默认实现。
    /// <para>内部使用自研 <see cref="EventStream{T}"/> 驱动响应式通知，
    /// 并通过 <see cref="MessageManager"/> 发布 <see cref="SettingsChangedMessage"/>。</para>
    /// <para>不会自动保存——调用方需显式调用 <see cref="Save"/> 来持久化。</para>
    /// </summary>
    /// <typeparam name="T">设置对象类型。</typeparam>
    internal sealed class SettingsManagerImpl<T> : ISettingsManager<T> where T : class, new()
    {
        #region Private Fields

        private T _settings;
        private ISettingsStore _store;
        private readonly Func<T> _defaultFactory;
        private readonly SettingsOptions _options;
        private readonly EventStream<T> _changedStream = new();
        private bool _disposed;

        /// <summary>
        /// 变更计数。用计数而非布尔标志，是为了让「保存过程中又发生改动」不被吞掉：
        /// Save 在开始时取快照、结束后把 _savedCount 设为快照值，期间的改动会让两者重新不等。
        /// </summary>
        private int _changeCount;

        /// <summary><see cref="_changeCount"/> 中已提交给存储后端的那一档。</summary>
        private int _savedCount;

        /// <summary>自动保存的帧驱动器。仅 <see cref="SettingsOptions.AutoSave"/> 开启时非空。</summary>
        private SettingsAutoSaveTicker<T> _autoSaver;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建设置管理器实例。
        /// </summary>
        /// <param name="store">存储后端。</param>
        /// <param name="defaultFactory">
        /// 可选的默认值工厂。持久层无数据时（初始化、<see cref="Load"/>、<see cref="Reset"/>）
        /// 均用此工厂创建设置；如果为 <c>null</c>，则使用 <c>new T()</c>。</param>
        /// <param name="options">可选的选项。为 <c>null</c> 时使用默认值（不启用版本化）。</param>
        public SettingsManagerImpl(ISettingsStore store, Func<T> defaultFactory = null, SettingsOptions options = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _defaultFactory = defaultFactory;
            _options = options ?? new SettingsOptions();

            // 默认值的来源必须唯一:构造、Load、Reset 三条路径都走 CreateDefault,
            // 否则玩家点「恢复默认」会拿到与首次启动不同的默认值
            _settings = store.Exists() ? LoadExisting() : CreateDefault();

            // 自动保存是可选能力,关闭时不注册任何帧回调——默认路径零开销
            if (_options.AutoSave)
            {
                _autoSaver = new SettingsAutoSaveTicker<T>(this, _options.AutoSaveDelay);
                UpdateManager.Register(_autoSaver, depth: 0, UpdateLOD.Tier3);
            }

            if (_options.SaveOnQuit)
                UnityEngine.Application.quitting += OnApplicationQuitting;
        }

        #endregion

        #region Data Access

        /// <inheritdoc />
        public T Settings
        {
            get
            {
                ThrowIfDisposed();
                return _settings;
            }
        }

        /// <inheritdoc />
        public void Apply(T settings)
        {
            ThrowIfDisposed();

            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _settings = settings;

            // 整体替换后内存与持久层不再一致。无从判断新对象是否恰好等于磁盘内容,故一律置脏
            _changeCount++;

            Notify();
        }

        #endregion

        #region Dirty

        /// <inheritdoc />
        public bool IsDirty
        {
            get
            {
                ThrowIfDisposed();
                return _changeCount != _savedCount;
            }
        }

        /// <inheritdoc />
        public void MarkDirty()
        {
            ThrowIfDisposed();
            _changeCount++;
        }

        #endregion

        #region Persistence

        /// <inheritdoc />
        public void Save()
        {
            ThrowIfDisposed();

            // 先取快照:写入期间若有新改动(如用户在保存过程中继续拖动滑条),计数会继续增长,
            // 保存结束后两者不等、仍是脏的。用布尔标志就会把这次改动吞掉
            var snapshot = _changeCount;
            SaveToStore(_store);
            _savedCount = snapshot;
        }

        /// <inheritdoc />
        public void Load()
        {
            ThrowIfDisposed();

            _settings = LoadCore();
            _savedCount = _changeCount; // 内存此刻与持久层一致
            Notify();
        }

        /// <inheritdoc />
        public UniTask SaveAsync(CancellationToken cancellationToken = default)
        {
            // 释放检查放在同步段:若写进 async 方法体,异常会被状态机包进返回的 UniTask,
            // 调用方拿不到同步失败(与 SavePathUtility 把校验做成同步纯函数是同一条理由)
            ThrowIfDisposed();
            return SaveAsyncCore(cancellationToken);
        }

        /// <inheritdoc />
        public UniTask LoadAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return LoadAsyncCore(cancellationToken);
        }

        /// <inheritdoc />
        public void Reset()
        {
            ThrowIfDisposed();

            _settings = CreateDefault();
            _store.Delete();

            // 持久层已清空、内存是默认值,重启后同样得到默认值,故不算脏
            _savedCount = _changeCount;
            Notify();
        }

        #endregion

        #region Reactive

        /// <inheritdoc />
        public IDisposable Observe(Action<T> callback)
        {
            ThrowIfDisposed();

            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            // 先注册再立即回调:与 ReactiveProperty / SettingRef 的顺序一致,
            // 确保回调中建立的订阅不会丢失后续消息
            var handle = _changedStream.Subscribe(callback);
            callback(_settings);
            return handle;
        }

        #endregion

        #region Migration

        /// <inheritdoc />
        public ISettingsMigrator<T> Migrator { get; set; }

        #endregion

        #region Store

        /// <inheritdoc />
        public ISettingsStore Store
        {
            get
            {
                ThrowIfDisposed();
                return _store;
            }
            set
            {
                ThrowIfDisposed();
                _store = value ?? throw new ArgumentNullException(nameof(value));

                // 只换后端、不迁移数据:内存里的设置仍是旧后端加载的内容,下一次 Save 会把
                // 它们写进新后端。刻意保留这个简单语义——隐式重新加载会静默丢掉内存中
                // 尚未 Save 的修改,那是更难查的故障,故只告警、由调用方决定后续动作
                UnityEngine.Debug.LogWarning(
                    $"[SettingsManager] Store 已替换为 {value.GetType().Name}，但内存中的设置未重新加载；" +
                    "下一次 Save 会把当前内存数据写入新后端。如需读取新后端已有数据，请在替换后调用 Load。");
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放管理器并终止通知流。
        /// <para>可重复调用。释放后除本方法外的所有公开成员抛 <see cref="ObjectDisposedException"/>，
        /// 避免「已释放却仍在写盘」或「订阅返回一个永不回调的空句柄」这类静默失效。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_autoSaver != null)
            {
                UpdateManager.Unregister(_autoSaver);
                _autoSaver = null;
            }

            if (_options.SaveOnQuit)
                UnityEngine.Application.quitting -= OnApplicationQuitting;

            _changedStream.OnCompleted();
            _changedStream.Dispose();
        }

        #endregion

        #region Internal

        /// <summary>
        /// <see cref="SaveAsync"/> 的实现体（释放检查已在同步段完成）。
        /// </summary>
        private async UniTask SaveAsyncCore(CancellationToken cancellationToken)
        {
            // 序列化留在主线程:设置对象通常只有几百字节,线程池往返的调度成本高于序列化本身
            // (取舍与 SaveManagerImpl 处理侧车一致),且 JsonUtility 非线程安全。
            // 快照 store 与变更计数:await 期间 Store setter 可能改 store,用户也可能继续改动设置
            var store = _store;
            var snapshot = _changeCount;

            if (IsVersioned)
                await WriteAsync(store, BuildEnvelope(), cancellationToken);
            else
                await WriteAsync(store, _settings, cancellationToken);

            // 与 SaveManagerImpl 的线程约定一致:公开异步方法在返回前切回主线程,
            // 使调用方 await 之后可以安全访问 Unity API
            await UniTask.SwitchToMainThread(cancellationToken);

            // 提交成功的是快照那一刻的内容;期间若有新改动,_changeCount 已增长,仍是脏的
            _savedCount = snapshot;
        }

        /// <summary>
        /// <see cref="LoadAsync"/> 的实现体（释放检查已在同步段完成）。
        /// </summary>
        private async UniTask LoadAsyncCore(CancellationToken cancellationToken)
        {
            T loaded;
            if (_store is IAsyncSettingsStore asyncStore)
            {
                loaded = await asyncStore.ExistsAsync(cancellationToken)
                    ? await ReadAsync(asyncStore, cancellationToken)
                    : CreateDefault();
            }
            else
            {
                // 能力降级:store 只实现同步接口,把整段同步读挪到线程池,语义与同步 Load 完全一致
                loaded = await UniTask.RunOnThreadPool(LoadCore, configureAwait: true, cancellationToken);
            }

            // 替换与通知必须在主线程:Notify 会经 MessageManager 广播,订阅者通常随即访问 Unity API
            await UniTask.SwitchToMainThread(cancellationToken);
            _settings = loaded;
            _savedCount = _changeCount; // 内存此刻与持久层一致
            Notify();
        }

        /// <summary>
        /// 同步加载的核心逻辑（不含通知），供同步 <see cref="Load"/> 与异步降级路径共用。
        /// </summary>
        private T LoadCore()
        {
            // 先问 Exists 而不是直接取 Load 的返回值:ISettingsStore.Load 的契约是
            // 「无数据时返回 new T()」,这里无法区分「读到了持久化数据」与「根本没有数据」,
            // 直接赋值会让 defaultFactory 在存档被删后形同虚设
            return _store.Exists() ? LoadExisting() : CreateDefault();
        }

        /// <summary>持久层已有数据时的同步读取入口：按是否启用版本化分流。</summary>
        private T LoadExisting()
        {
            return IsVersioned ? DecodeEnvelope(_store.Load<SettingsEnvelope<T>>()) : LoadFromStore();
        }

        /// <summary>是否启用版本化落盘（<see cref="SettingsOptions.CurrentVersion"/> 大于 0）。</summary>
        private bool IsVersioned => _options.CurrentVersion > 0;

        /// <summary>构造当前版本的落盘信封。</summary>
        private SettingsEnvelope<T> BuildEnvelope()
        {
            return new SettingsEnvelope<T> { Version = _options.CurrentVersion, Data = _settings };
        }

        /// <summary>同步写入（供 <see cref="Save"/> 使用）。</summary>
        private void SaveToStore(ISettingsStore store)
        {
            if (IsVersioned)
                store.Save(BuildEnvelope());
            else
                store.Save(_settings);
        }

        /// <summary>
        /// 异步写入非版本化载荷：store 实现 <see cref="IAsyncSettingsStore"/> 时用其异步成员，
        /// 否则整段下线程池。与版本化重载分开是因为 <c>ISettingsStore.Save{T}</c> 是泛型的，
        /// 调用点的类型参数必须在编译期确定。
        /// </summary>
        private static async UniTask WriteAsync(ISettingsStore store, T settings, CancellationToken cancellationToken)
        {
            if (store is IAsyncSettingsStore asyncStore)
                await asyncStore.SaveAsync(settings, cancellationToken);
            else
                await UniTask.RunOnThreadPool(
                    () => store.Save(settings), configureAwait: false, cancellationToken);
        }

        /// <summary>异步写入版本化信封，分流规则同非版本化重载。</summary>
        private static async UniTask WriteAsync(ISettingsStore store, SettingsEnvelope<T> envelope, CancellationToken cancellationToken)
        {
            if (store is IAsyncSettingsStore asyncStore)
                await asyncStore.SaveAsync(envelope, cancellationToken);
            else
                await UniTask.RunOnThreadPool(
                    () => store.Save(envelope), configureAwait: false, cancellationToken);
        }

        /// <summary>异步读取入口：按是否启用版本化分流。</summary>
        private async UniTask<T> ReadAsync(IAsyncSettingsStore store, CancellationToken cancellationToken)
        {
            return IsVersioned
                ? DecodeEnvelope(await store.LoadAsync<SettingsEnvelope<T>>(cancellationToken))
                : await LoadFromStoreAsync(store, cancellationToken);
        }

        /// <summary>
        /// 解开版本信封：版本判定 + 迁移。信封或其载荷为空视为不可读。
        /// <para>迁移在返回给调用方之前、订阅者被通知之前执行，故迁移过程中写值不会产生多余通知。</para>
        /// </summary>
        private T DecodeEnvelope(SettingsEnvelope<T> envelope)
        {
            if (envelope?.Data == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[SettingsManager] 设置数据缺少版本信封或载荷为空，已回退默认值。" +
                    $"（当前 CurrentVersion={_options.CurrentVersion}，期望信封格式 {{Version, Data}}；" +
                    "若此前按无版本格式落盘，启用版本化后旧文件将无法识别）");
                return CreateDefault();
            }

            if (envelope.Version > _options.CurrentVersion)
            {
                // 高于本版本:整份拒绝。数据可能由更新版游戏写入,按旧结构解析会静默错位
                UnityEngine.Debug.LogWarning(
                    $"[SettingsManager] 设置格式版本 {envelope.Version} 高于本版本支持的 " +
                    $"{_options.CurrentVersion}，已整份拒绝并回退默认值。");
                return CreateDefault();
            }

            if (envelope.Version < _options.CurrentVersion)
            {
                if (Migrator == null)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[SettingsManager] 设置格式版本 {envelope.Version} 需要迁移到 {_options.CurrentVersion}，" +
                        $"但未注册 ISettingsMigrator<{typeof(T).Name}>，已回退默认值。");
                    return CreateDefault();
                }

                Migrator.Migrate(envelope.Version, _options.CurrentVersion, envelope.Data);
            }

            return envelope.Data;
        }

        /// <summary>
        /// 创建默认设置对象：优先用构造时注入的 <c>defaultFactory</c>，未注入则为 <c>new T()</c>。
        /// <para>构造、<see cref="Load"/>、<see cref="Reset"/> 三条路径共用此方法，保证默认值来源唯一。</para>
        /// </summary>
        private T CreateDefault()
        {
            return _defaultFactory != null ? _defaultFactory() : new T();
        }

        /// <summary>
        /// 从存储后端读取设置，并防御 store 返回 <c>null</c>。
        /// <para><see cref="ISettingsStore.Load{T}"/> 的契约要求无数据时返回 <c>new T()</c>，
        /// 但契约不被编译器强制。返回 <c>null</c> 会让 <see cref="Settings"/> 变 <c>null</c>，
        /// 并把 NRE 推迟到调用方各处爆发，故此处回退默认值并告警。</para>
        /// </summary>
        private T LoadFromStore()
        {
            var loaded = _store.Load<T>();
            if (loaded != null)
                return loaded;

            UnityEngine.Debug.LogWarning(
                $"[SettingsManager] ISettingsStore.Load<{typeof(T).Name}> 返回了 null，已回退到默认值。" +
                "存储实现应在无数据时返回 new T()。");
            return CreateDefault();
        }

        /// <summary>
        /// 异步加载的核心逻辑，与同步版 <see cref="LoadFromStore"/> 对称，同样防御 store 返回 <c>null</c>。
        /// </summary>
        private async UniTask<T> LoadFromStoreAsync(IAsyncSettingsStore store, CancellationToken cancellationToken)
        {
            var loaded = await store.LoadAsync<T>(cancellationToken);
            if (loaded != null)
                return loaded;

            UnityEngine.Debug.LogWarning(
                $"[SettingsManager] ISettingsStore.Load<{typeof(T).Name}> 返回了 null，已回退到默认值。" +
                "存储实现应在无数据时返回 new T()。");
            return CreateDefault();
        }

        /// <summary>是否已释放。供自动保存驱动器在「注销」与「当帧已调度」的竞态窗口内安全退出。</summary>
        internal bool IsDisposed => _disposed;

        /// <summary>
        /// 变更计数。驱动器靠它区分「刚刚改过」与「改动已久」——<see cref="IsDirty"/> 在整个
        /// 去抖窗口内恒为真，区分不出这两者。
        /// </summary>
        internal int ChangeCount => _changeCount;

        /// <summary>
        /// 应用退出兜底：仅在确有未提交改动时写盘，因此显式保存过的场景不会产生额外 IO。
        /// <para>编辑器不触发 <c>Application.quitting</c>，故无法在 Test Runner 中覆盖。</para>
        /// </summary>
        private void OnApplicationQuitting()
        {
            if (_disposed || !IsDirty)
                return;

            Save();
        }

        /// <summary>
        /// 已释放状态下调用任何公开成员均抛出异常。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(
                    $"{nameof(SettingsManagerImpl<T>)}<{typeof(T).Name}>",
                    $"[SettingsManager] SettingsManager<{typeof(T).Name}> 已释放，请勿再访问。");
        }

        /// <summary>
        /// 通知所有订阅者：设置已变更。
        /// </summary>
        private void Notify()
        {
            _changedStream.OnNext(_settings);
            MessageManager.Publish(new SettingsChangedMessage(typeof(T)));
        }

        #endregion
    }
}