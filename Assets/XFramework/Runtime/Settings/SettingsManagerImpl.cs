using System;
using System.Collections.Generic;
using XFramework.XMessage;
using XFramework.XMessage.Internal;

namespace XFramework.XSettings
{
    /// <summary>
    /// <see cref="ISettingsManager{T}"/> 的默认实现。
    /// <para>内部使用自研 <see cref="EventStream{T}"/> 驱动响应式通知，
    /// 并通过 <see cref="MessageManager"/> 发布 <see cref="SettingsChangedMessage"/>。</para>
    /// <para>不会自动保存——调用方需显式调用 <see cref="Save"/> 来持久化。</para>
    /// </summary>
    /// <typeparam name="T">设置对象类型。</typeparam>
    public class SettingsManagerImpl<T> : ISettingsManager<T> where T : class, new()
    {
        #region Private Fields

        private T _settings;
        private ISettingsStore _store;
        private readonly Func<T> _defaultFactory;
        private readonly EventStream<T> _changedStream = new();
        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建设置管理器实例。
        /// </summary>
        /// <param name="store">存储后端。</param>
        /// <param name="defaultFactory">
        /// 可选的默认值工厂。持久层无数据时（初始化、<see cref="Load"/>、<see cref="Reset"/>）
        /// 均用此工厂创建设置；如果为 <c>null</c>，则使用 <c>new T()</c>。</param>
        public SettingsManagerImpl(ISettingsStore store, Func<T> defaultFactory = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _defaultFactory = defaultFactory;

            // 默认值的来源必须唯一:构造、Load、Reset 三条路径都走 CreateDefault,
            // 否则玩家点「恢复默认」会拿到与首次启动不同的默认值
            _settings = store.Exists() ? LoadFromStore() : CreateDefault();
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
            Notify();
        }

        #endregion

        #region Persistence

        /// <inheritdoc />
        public void Save()
        {
            ThrowIfDisposed();
            _store.Save(_settings);
        }

        /// <inheritdoc />
        public void Load()
        {
            ThrowIfDisposed();

            // 先问 Exists 而不是直接取 Load 的返回值:ISettingsStore.Load 的契约是
            // 「无数据时返回 new T()」,这里无法区分「读到了持久化数据」与「根本没有数据」,
            // 直接赋值会让 defaultFactory 在存档被删后形同虚设
            _settings = _store.Exists() ? LoadFromStore() : CreateDefault();
            Notify();
        }

        /// <inheritdoc />
        public void Reset()
        {
            ThrowIfDisposed();

            _settings = CreateDefault();
            _store.Delete();
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

            return _changedStream.Subscribe(callback);
        }

        /// <inheritdoc />
        public IDisposable ObserveField<TField>(Func<T, TField> selector, Action<TField> callback)
        {
            ThrowIfDisposed();

            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            // 内联闭包状态机实现去重:首次必过(hasLast=false),之后相同值去重(EqualityComparer 默认比较器)
            // 闭包分配仅在订阅建立时一次性,非热路径
            var hasLast = false;
            var lastValue = default(TField);
            return _changedStream.Subscribe(settings =>
            {
                var fieldValue = selector(settings);
                if (hasLast && EqualityComparer<TField>.Default.Equals(lastValue, fieldValue))
                    return;
                hasLast = true;
                lastValue = fieldValue;
                callback(fieldValue);
            });
        }

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
            _changedStream.OnCompleted();
            _changedStream.Dispose();
        }

        #endregion

        #region Internal

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