using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XSettings
{
    /// <summary>
    /// 全局设置管理器（静态外观）。
    /// <para>提供统一的静态 API 来管理多套强类型设置对象。</para>
    /// <para>内部维护 <see cref="Dictionary{Type, object}"/> 缓存多个 <see cref="ISettingsManager{T}"/> 实例，
    /// 支持同时管理 <c>GameSettings</c>、<c>EditorSettings</c> 等不同类型的设置。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>使用流程：</b></para>
    /// <list type="number">
    /// <item>定义设置结构体（<see cref="SerializableAttribute"/>，<c>class, new()</c>）。</item>
    /// <item>调用 <see cref="Initialize{T}(string, Func{T})"/> 初始化。</item>
    /// <item>通过 <see cref="Settings{T}"/> 读写设置。</item>
    /// <item>修改后显式调用 <see cref="Save{T}"/> 持久化。</item>
    /// </list>
    /// <para><b>示例：</b></para>
    /// <code>
    /// // 初始化
    /// SettingsManager.Initialize<GameSettings>(Application.persistentDataPath + "/settings.json");
    ///
    /// // 读取/修改
    /// var settings = SettingsManager.Settings<GameSettings>();
    /// settings.audio.masterVolume = 0.5f;
    /// SettingsManager.Save<GameSettings>();
    ///
    /// // 响应式订阅
    /// SettingsManager.ObserveField<GameSettings, float>(
    ///     s => s.audio.masterVolume,
    ///     v => audioMixer.SetFloat("Master", v)
    /// );
    /// </code>
    /// </remarks>
    public static class SettingsManager
    {
        #region Private Fields

        /// <summary>按类型缓存多个 ISettingsManager 实例。</summary>
        private static readonly Dictionary<Type, object> Managers = new();

        #endregion

        #region Lifecycle

        /// <summary>
        /// 是否已初始化至少一个设置类型。
        /// </summary>
        public static bool IsInitialized => Managers.Count > 0;

        /// <summary>
        /// 初始化指定类型的设置管理器，使用指定 JSON 文件路径作为存储后端。
        /// <para>仅首次调用有效：重复调用同一类型会打 LogWarning 并返回已存在的实例。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="filePath">JSON 文件完整路径。</param>
        /// <param name="defaultFactory">
        /// 可选的默认值工厂。持久层无数据时（初始化、重新加载、重置）均用此工厂创建设置；
        /// 如果为 <c>null</c>，则使用 <c>new T()</c>。</param>
        /// <param name="options">可选的选项。为 <c>null</c> 时使用默认值（不启用版本化）。</param>
        /// <returns>初始化后的 <see cref="ISettingsManager{T}"/> 实例。</returns>
        public static ISettingsManager<T> Initialize<T>(
            string filePath, Func<T> defaultFactory = null, SettingsOptions options = null)
            where T : class, new()
        {
            return Initialize<T>(new JsonFileStore(filePath), defaultFactory, options);
        }

        /// <summary>
        /// 初始化指定类型的设置管理器，使用自定义 <see cref="ISettingsStore"/>。
        /// <para>仅首次调用有效：重复调用同一类型会打 LogWarning 并返回已存在的实例。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="store">自定义存储后端。例如 <see cref="JsonFileStore"/> 或加密存储等。</param>
        /// <param name="defaultFactory">
        /// 可选的默认值工厂。持久层无数据时（初始化、重新加载、重置）均用此工厂创建设置；
        /// 如果为 <c>null</c>，则使用 <c>new T()</c>。</param>
        /// <param name="options">可选的选项。为 <c>null</c> 时使用默认值（不启用版本化）。</param>
        /// <returns>初始化后的 <see cref="ISettingsManager{T}"/> 实例。</returns>
        public static ISettingsManager<T> Initialize<T>(
            ISettingsStore store, Func<T> defaultFactory = null, SettingsOptions options = null)
            where T : class, new()
        {
            var type = typeof(T);
            if (Managers.TryGetValue(type, out var existing))
            {
                UnityEngine.Debug.LogWarning(
                    $"[SettingsManager] Initialize<{type.Name}> was called more than once. Ignoring duplicate.");
                return (ISettingsManager<T>)existing;
            }

            var manager = new SettingsManagerImpl<T>(store, defaultFactory, options);
            Managers[type] = manager;
            return manager;
        }

        /// <summary>
        /// 释放所有设置管理器并清空缓存。
        /// <para>通常在应用退出时调用。</para>
        /// <para>销毁后可重新 <see cref="Initialize{T}(ISettingsStore, Func{T})"/>，与 Config / Localization 的门面一致。
        /// 销毁到重新初始化之间访问任意类型会抛「尚未初始化」异常并附修复提示。</para>
        /// </summary>
        public static void Destroy()
        {
            foreach (var manager in Managers.Values)
            {
                ((IDisposable)manager).Dispose();
            }

            Managers.Clear();
        }

        #endregion

        #region Data Access

        /// <summary>
        /// 获取指定类型当前设置对象的引用。
        /// <para>修改字段后需显式调用 <see cref="Save{T}"/> 才能持久化。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <returns>当前设置对象。</returns>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static T Settings<T>() where T : class, new()
        {
            return GetManager<T>().Settings;
        }

        /// <summary>
        /// 替换整个设置对象并通知所有订阅者。
        /// <para>不会自动触发 <see cref="Save{T}"/>。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="settings">新的设置对象。</param>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static void Apply<T>(T settings) where T : class, new()
        {
            GetManager<T>().Apply(settings);
        }

        #endregion

        #region Field Ref

        /// <summary>
        /// 为设置对象中的一个字段创建响应式句柄，供 UI 绑定与「改值即通知」使用。
        /// <para><b>本方法必须调用一次并缓存返回值</b>（典型做法是 <c>static readonly</c> 字段）：
        /// 它需要解析并编译表达式，且每次调用都会新建句柄与事件流。</para>
        /// <para>可在 <see cref="Initialize{T}(ISettingsStore, Func{T}, SettingsOptions)"/> 之前调用——
        /// 解析表达式不需要设置实例，句柄在真正读写时才去找当前实例。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <typeparam name="TField">字段类型。</typeparam>
        /// <param name="selector">字段选择器，如 <c>s =&gt; s.Audio.MasterVolume</c>。路径必须以字段结尾。</param>
        /// <returns>与设置对象解耦的字段句柄，每次读写都作用于当前的设置实例。</returns>
        /// <exception cref="ArgumentException">选择器不以字段结尾，或路径含不支持的结构时抛出。</exception>
        /// <example>
        /// <code>
        /// private static readonly SettingRef&lt;GameSettings, float&gt; MasterVolume =
        ///     SettingsManager.Ref&lt;GameSettings, float&gt;(s =&gt; s.Audio.MasterVolume);
        ///
        /// MasterVolume.Value = 0.5f;              // 回写 + 通知
        /// MasterVolume.BindToSlider(masterSlider); // 复用 UI 模块的现成绑定
        /// </code>
        /// </example>
        public static SettingRef<T, TField> Ref<T, TField>(Expression<Func<T, TField>> selector)
            where T : class, new()
        {
            return SettingRef<T, TField>.Create(selector);
        }

        #endregion

        #region Persistence

        /// <summary>
        /// 保存当前设置到持久层。
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static void Save<T>() where T : class, new()
        {
            GetManager<T>().Save();
        }

        /// <summary>
        /// 从持久层重新加载，覆盖当前设置，并通知所有订阅者。
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static void Load<T>() where T : class, new()
        {
            GetManager<T>().Load();
        }

        /// <summary>
        /// 重置为默认值并删除持久化文件。
        /// <para>默认值来自 <see cref="Initialize{T}(string, Func{T})"/> 时注入的工厂，与首次初始化所得默认值一致。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static void Reset<T>() where T : class, new()
        {
            GetManager<T>().Reset();
        }

        /// <summary>
        /// 异步保存当前设置到持久层，语义与 <see cref="Save{T}"/> 相同。
        /// <para>IO 在线程池或存储后端自身的异步实现上执行，并在返回前切回主线程，
        /// 因此 <c>await</c> 之后可安全访问 Unity API。禁止在主线程用
        /// <c>.GetAwaiter().GetResult()</c> 同步阻塞等待，否则会死锁。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static UniTask SaveAsync<T>(CancellationToken cancellationToken = default) where T : class, new()
        {
            return GetManager<T>().SaveAsync(cancellationToken);
        }

        /// <summary>
        /// 异步从持久层重新加载，覆盖当前设置，并通知所有订阅者。语义与 <see cref="Load{T}"/> 相同。
        /// <para>线程约定同 <see cref="SaveAsync{T}"/>。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static UniTask LoadAsync<T>(CancellationToken cancellationToken = default) where T : class, new()
        {
            return GetManager<T>().LoadAsync(cancellationToken);
        }

        #endregion

        #region Dirty

        /// <summary>
        /// 内存中的设置自上次保存 / 加载 / 重置以来是否有改动。
        /// <para>经字段句柄写入会自动置脏；直接改 POCO 字段后需自行调用 <see cref="MarkDirty{T}"/>。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static bool IsDirty<T>() where T : class, new()
        {
            return GetManager<T>().IsDirty;
        }

        /// <summary>
        /// 手动标记为「有改动」。
        /// <para>仅在<b>直接修改设置对象字段</b>后需要调用——句柄写入会自动置脏，直改字段框架无从感知。
        /// 这也是启用自动保存后，框架感知改动的唯一来源。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static void MarkDirty<T>() where T : class, new()
        {
            GetManager<T>().MarkDirty();
        }

        #endregion

        #region Reactive

        /// <summary>
        /// 订阅整个设置对象变更。
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="callback">设置变更回调。</param>
        /// <returns>取消订阅的 <see cref="IDisposable"/>。</returns>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static IDisposable Observe<T>(Action<T> callback) where T : class, new()
        {
            return GetManager<T>().Observe(callback);
        }

        /// <summary>
        /// 订阅设置对象中特定字段的变更。
        /// <para>仅当该字段的值与上次通知不同时触发。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <typeparam name="TField">字段类型。</typeparam>
        /// <param name="selector">字段选择器。</param>
        /// <param name="callback">字段变更回调。</param>
        /// <returns>取消订阅的 <see cref="IDisposable"/>。</returns>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static IDisposable ObserveField<T, TField>(Func<T, TField> selector, Action<TField> callback)
            where T : class, new()
        {
            return GetManager<T>().ObserveField(selector, callback);
        }

        #endregion

        #region Store

        /// <summary>
        /// 获取指定类型当前使用的存储后端。
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <returns>当前存储后端。</returns>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static ISettingsStore GetStore<T>() where T : class, new()
        {
            return GetManager<T>().Store;
        }

        /// <summary>
        /// 替换指定类型的存储后端。
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="store">新的存储后端。</param>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static void SetStore<T>(ISettingsStore store) where T : class, new()
        {
            GetManager<T>().Store = store;
        }

        #endregion

        #region Internal

        /// <summary>
        /// 获取指定类型的 <see cref="ISettingsManager{T}"/> 实例。
        /// </summary>
        private static ISettingsManager<T> GetManager<T>() where T : class, new()
        {
            var type = typeof(T);
            if (Managers.TryGetValue(type, out var manager))
                return (ISettingsManager<T>)manager;

            throw new InvalidOperationException(
                $"[SettingsManager] SettingsManager 尚未初始化类型 '{type.Name}'。" +
                $"请先调用 SettingsManager.Initialize<{type.Name}>() 完成初始化。");
        }

        #endregion
    }
}