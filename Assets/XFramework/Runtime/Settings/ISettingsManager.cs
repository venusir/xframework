using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XSettings
{
    /// <summary>
    /// 强类型设置管理器接口。
    /// <para>管理一组设置对象（类型 T）的完整生命周期：加载、修改、保存、重置。</para>
    /// <para><b>两级订阅分工：</b>字段级变化经 <see cref="SettingRef{T,TField}"/> 订阅；
    /// 设置对象被整体替换（Apply / Load / Reset）经 <see cref="Observe"/> 订阅。</para>
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

        #region Dirty

        /// <summary>
        /// 内存中的设置自上次保存 / 加载 / 重置以来是否有改动。
        /// <para><b>语义边界：</b>它表示「内存改动是否已提交给存储后端」，而<b>不</b>保证已成功落盘
        /// ——<see cref="ISettingsStore.Save{T}"/> 返回 <c>void</c>，实现可能只告警不抛（如
        /// <see cref="JsonFileStore"/> 对 IO 失败的处理），框架无从得知。</para>
        /// <para>经 <see cref="ISettingsManager{T}"/> 的 Save/Load/Reset 会清除；整体
        /// <see cref="Apply"/> 与字段写入会置脏。</para>
        /// </summary>
        bool IsDirty { get; }

        /// <summary>
        /// 手动标记为「有改动」。
        /// <para>仅在<b>直接修改设置对象字段</b>后需要调用——经字段句柄写入会自动置脏，
        /// 直改字段则框架无从感知。这也是自动保存（若启用）唯一的感知来源。</para>
        /// </summary>
        void MarkDirty();

        #endregion

        #region Reactive

        /// <summary>
        /// 订阅设置对象<b>被整体替换</b>的变更。
        /// <para>订阅时立即同步回调当前对象（与 <c>ReactiveProperty&lt;T&gt;</c>、<see cref="SettingRef{T,TField}"/>
        /// 契约一致），之后在 <see cref="Apply"/>、<see cref="Load"/>、<see cref="Reset"/> 时回调。</para>
        /// <para><b>字段级变化不经此订阅</b>——那是 <see cref="SettingRef{T,TField}"/> 的职责。
        /// 本订阅解决的是「设置对象被换掉了」：订阅者需据此改读新的当前实例。
        /// 原先的 <c>ObserveField</c> 已移除：它对引用类型字段用引用相等去重，
        /// 子对象内容变化时会被静默吞掉，字段级订阅请改用句柄。</para>
        /// </summary>
        /// <param name="callback">设置对象被替换时的回调，参数为新的当前对象。</param>
        /// <returns>取消订阅的 <see cref="IDisposable"/>。</returns>
        IDisposable Observe(Action<T> callback);

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

        #region Migration

        /// <summary>
        /// 格式迁移钩子。仅当 <see cref="SettingsOptions.CurrentVersion"/> 大于持久化数据的版本时被调用。
        /// <para>为 <c>null</c>（默认）时，遇到需要迁移的数据会回退默认值并打 LogWarning——
        /// 宁可回到默认值，也不要按旧结构解析出静默错位的设置。</para>
        /// </summary>
        ISettingsMigrator<T> Migrator { get; set; }

        #endregion
    }
}