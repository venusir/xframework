using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XSettings
{
    /// <summary>
    /// 强类型设置管理器接口。
    /// <para>管理一组设置对象（类型 T）的完整生命周期：加载、修改、保存、重置。</para>
    /// <para>通过 <see cref="Observe"/> 和 <see cref="ObserveField{TField}"/> 提供响应式订阅。</para>
    /// <para>默认实现：<see cref="SettingsManagerImpl{T}"/>。</para>
    /// <para><b>释放语义：</b><see cref="IDisposable.Dispose"/> 可重复调用；释放后除 Dispose 外的
    /// 所有成员抛 <see cref="ObjectDisposedException"/>。</para>
    /// </summary>
    /// <typeparam name="T">设置对象类型。必须满足 <c>class, new()</c> 约束，并标记 <see cref="SerializableAttribute"/>。</typeparam>
    public interface ISettingsManager<T> : IDisposable where T : class, new()
    {
        #region Data Access

        /// <summary>
        /// 获取当前设置对象的引用。
        /// <para>修改字段后需显式调用 <see cref="Save"/> 才能持久化。</para>
        /// </summary>
        T Settings { get; }

        /// <summary>
        /// 替换整个设置对象并通知所有订阅者。
        /// <para>不会自动触发 <see cref="Save"/>。</para>
        /// </summary>
        /// <param name="settings">新的设置对象。</param>
        void Apply(T settings);

        #endregion

        #region Persistence

        /// <summary>
        /// 保存当前设置到持久层。
        /// </summary>
        void Save();

        /// <summary>
        /// 从持久层重新加载，覆盖当前设置，并通知所有订阅者。
        /// <para>如果持久层无数据，则使用构造时注入的 <c>defaultFactory</c>（未注入时为 <c>new T()</c>）。</para>
        /// </summary>
        void Load();

        /// <summary>
        /// 重置为默认值并删除持久化文件。
        /// <para>默认值来自构造时注入的 <c>defaultFactory</c>（未注入时为 <c>new T()</c>），
        /// 与首次初始化所得默认值一致。</para>
        /// </summary>
        void Reset();

        /// <summary>
        /// 异步保存当前设置到持久层，语义与 <see cref="Save"/> 相同。
        /// <para>IO 在线程池或存储后端自身的异步实现上执行，并在返回前切回主线程，
        /// 因此 <c>await</c> 之后可安全访问 Unity API。代价是依赖 PlayerLoop 泵，
        /// <b>禁止在主线程用 <c>.GetAwaiter().GetResult()</c> 同步阻塞等待</b>，否则会死锁。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask SaveAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步从持久层重新加载，覆盖当前设置，并通知所有订阅者。语义与 <see cref="Load"/> 相同。
        /// <para>线程约定同 <see cref="SaveAsync"/>。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask LoadAsync(CancellationToken cancellationToken = default);

        #endregion

        #region Reactive

        /// <summary>
        /// 订阅整个设置对象变更。
        /// <para>在 <see cref="Apply"/>、<see cref="Load"/>、<see cref="Reset"/> 时触发。</para>
        /// </summary>
        /// <param name="callback">设置变更回调。</param>
        /// <returns>取消订阅的 <see cref="IDisposable"/>。</returns>
        IDisposable Observe(Action<T> callback);

        /// <summary>
        /// 订阅设置对象中特定字段的变更。
        /// <para>仅当该字段的值与上次通知不同时触发，避免不必要的刷新。</para>
        /// </summary>
        /// <typeparam name="TField">字段类型。</typeparam>
        /// <param name="selector">字段选择器。例如 <c>s => s.audio.masterVolume</c>。</param>
        /// <param name="callback">字段变更回调。</param>
        /// <returns>取消订阅的 <see cref="IDisposable"/>。</returns>
        IDisposable ObserveField<TField>(Func<T, TField> selector, Action<TField> callback);

        #endregion

        #region Store

        /// <summary>
        /// 获取或设置存储后端。
        /// <para>可在运行时替换（如从 JSON 文件切换为加密存储）。</para>
        /// <para><b>替换只换后端、不迁移数据：</b>内存中的设置仍是旧后端加载的内容，
        /// 下一次 <see cref="Save"/> 会把它们写入新后端。如需读取新后端已有数据，
        /// 请在替换后调用 <see cref="Load"/>。实现会在替换时打 LogWarning 提醒。</para>
        /// </summary>
        ISettingsStore Store { get; set; }

        #endregion
    }
}