using System;
using R3;
using XFramework.XReactive;

namespace XFramework.XSettings
{
    /// <summary>
    /// <see cref="ISettingsManager{T}"/> 的默认实现。
    /// <para>内部使用 <see cref="Subject{T}"/>（R3）驱动响应式通知，
    /// 并通过 <see cref="MessageManager"/> 发布 <see cref="SettingsChangedMessage"/>。</para>
    /// <para>不会自动保存——调用方需显式调用 <see cref="Save"/> 来持久化。</para>
    /// </summary>
    /// <typeparam name="T">设置对象类型。</typeparam>
    public class SettingsManagerImpl<T> : ISettingsManager<T> where T : class, new()
    {
        #region Private Fields

        private T _settings;
        private ISettingsStore _store;
        private readonly Subject<T> _changedSubject = new();

        #endregion

        #region Constructors

        /// <summary>
        /// 创建设置管理器实例。
        /// </summary>
        /// <param name="store">存储后端。</param>
        /// <param name="defaultFactory">
        /// 可选的默认值工厂。如果持久层无数据，使用此工厂创建初始设置；
        /// 如果为 <c>null</c>，则使用 <c>new T()</c>。</param>
        public SettingsManagerImpl(ISettingsStore store, Func<T> defaultFactory = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));

            if (store.Exists())
            {
                _settings = store.Load<T>();
            }
            else
            {
                _settings = defaultFactory != null ? defaultFactory() : new T();
            }
        }

        #endregion

        #region Data Access

        /// <inheritdoc />
        public T Settings => _settings;

        /// <inheritdoc />
        public void Apply(T settings)
        {
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
            _store.Save(_settings);
        }

        /// <inheritdoc />
        public void Load()
        {
            _settings = _store.Load<T>();
            Notify();
        }

        /// <inheritdoc />
        public void Reset()
        {
            _settings = new T();
            _store.Delete();
            Notify();
        }

        #endregion

        #region Reactive

        /// <inheritdoc />
        public IDisposable Observe(Action<T> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            return _changedSubject.Subscribe(callback);
        }

        /// <inheritdoc />
        public IDisposable ObserveField<TField>(Func<T, TField> selector, Action<TField> callback)
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            return _changedSubject
                .Select(selector)
                .DistinctUntilChanged()
                .Subscribe(callback);
        }

        #endregion

        #region Store

        /// <inheritdoc />
        public ISettingsStore Store
        {
            get => _store;
            set => _store = value ?? throw new ArgumentNullException(nameof(value));
        }

        #endregion

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            _changedSubject.OnCompleted();
            _changedSubject.Dispose();
        }

        #endregion

        #region Internal

        /// <summary>
        /// 通知所有订阅者：设置已变更。
        /// </summary>
        private void Notify()
        {
            _changedSubject.OnNext(_settings);
            MessageManager.Publish(new SettingsChangedMessage(typeof(T)));
        }

        #endregion
    }
}