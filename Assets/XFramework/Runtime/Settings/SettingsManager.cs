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
    /// <item>为需要的字段声明 <see cref="SettingRef{T, TField}"/> 句柄。</item>
    /// <item>调用 <see cref="Initialize{T}(string, Func{T}, SettingsOptions)"/> 初始化。</item>
    /// <item>读直接读字段；写经句柄，或直改字段后调 <see cref="MarkDirty{T}"/>。</item>
    /// <item>修改后显式调用 <see cref="Save{T}"/> / <see cref="SaveAsync{T}"/> 持久化。</item>
    /// </list>
    /// <para><b>示例：</b></para>
    /// <code>
    /// // 字段句柄：调用一次并缓存
    /// private static readonly SettingRef&lt;GameSettings, float&gt; MasterVolume =
    ///     SettingsManager.Ref&lt;GameSettings, float&gt;(s =&gt; s.Audio.MasterVolume);
    ///
    /// // 初始化
    /// SettingsManager.Initialize&lt;GameSettings&gt;(Application.persistentDataPath + "/settings.json");
    ///
    /// // 读：直接读字段
    /// float v = SettingsManager.Settings&lt;GameSettings&gt;().Audio.MasterVolume;
    ///
    /// // 写：经句柄，会通知订阅者并置脏
    /// MasterVolume.Value = 0.5f;
    ///
    /// // 绑定 UI：句柄实现 IReactiveProperty&lt;T&gt;，现成的绑定扩展方法可直接用
    /// MasterVolume.BindToSlider(masterSlider);
    ///
    /// SettingsManager.Save&lt;GameSettings&gt;();
    /// </code>
    /// </remarks>
    public static class SettingsManager
    {
        #region Private Fields

        /// <summary>按类型缓存多个 ISettingsManager 实例。</summary>
        private static readonly Dictionary<Type, object> Managers = new();

        /// <summary>
        /// 默认路径的占用表：路径 → 占用它的类型。
        /// <para>默认路径取类型短名，不同命名空间下的同名类型会算出同一个路径；
        /// 若放任不管，两份设置会互相覆盖且毫无提示。此表把那种情况变成明确的报错。</para>
        /// <para><b>刻意不在 <see cref="Destroy"/> 里清空：</b>占用关系对应的是磁盘上的文件，
        /// 而文件不会随 Destroy 消失。若清空，则「A 初始化 → Destroy → B 初始化」会让 B 悄悄
        /// 接管 A 的路径，并在 A 的存档存在时把 A 的数据当作 B 解析——正是本表要防的那种事。</para>
        /// </summary>
        private static readonly Dictionary<string, Type> DefaultPathClaims = new();

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
        /// 用<b>默认路径</b>初始化指定类型的设置，无需传路径。
        /// <para>路径为 <c>{Application.persistentDataPath}/{类型短名}.json</c>，
        /// 便于零配置起步（如原型、示例、编辑器工具）。</para>
        /// <para><b>陷阱：</b>路径取类型短名，因此不同命名空间下的<b>同名类型</b>会算出同一路径。
        /// 框架会拦下这种情况并抛异常（否则两份设置会互相覆盖且毫无提示），
        /// 届时请改用显式路径的 <see cref="Initialize{T}(string, Func{T}, SettingsOptions)"/> 重载。</para>
        /// <para>参数顺序与另外两个重载一致（工厂在前、选项在后）；
        /// 只传选项时请用具名实参：<c>Initialize&lt;T&gt;(options: new SettingsOptions { AutoSave = true })</c>。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="defaultFactory">可选的默认值工厂。为 <c>null</c> 时使用 <c>new T()</c>。</param>
        /// <param name="options">可选的选项。为 <c>null</c> 时使用默认值（不启用版本化与自动保存）。</param>
        /// <returns>初始化后的 <see cref="ISettingsManager{T}"/> 实例。</returns>
        /// <exception cref="InvalidOperationException">该默认路径已被另一个同名类型占用时抛出。</exception>
        public static ISettingsManager<T> Initialize<T>(Func<T> defaultFactory = null, SettingsOptions options = null)
            where T : class, new()
        {
            var type = typeof(T);
            var filePath = BuildDefaultFilePath(type);

            if (DefaultPathClaims.TryGetValue(filePath, out var owner) && owner != type)
            {
                throw new InvalidOperationException(
                    $"[SettingsManager] 类型 '{type.FullName}' 与 '{owner.FullName}' 的默认路径相同" +
                    $"（'{filePath}'）。默认路径取类型短名，同名类型需改用显式路径的 Initialize 重载。");
            }

            DefaultPathClaims[filePath] = type;
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
        /// 尝试获取指定类型的当前设置对象，<b>未初始化时不抛异常</b>。
        /// <para>用于「可能已注册也可能没有」的探测场景（如可选功能、编辑器工具）——
        /// 那些地方本就不该假定初始化顺序，用异常表达正常分支会把调用方逼进 try/catch。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="settings">当前设置对象；未注册时为 <c>null</c>。</param>
        /// <returns>该类型已初始化返回 <c>true</c>。</returns>
        public static bool TrySettings<T>(out T settings) where T : class, new()
        {
            if (TryGetManager<T>(out var manager))
            {
                settings = manager.Settings;
                return true;
            }

            settings = null;
            return false;
        }

        /// <summary>
        /// 指定类型是否已初始化。语义等价于 <see cref="TrySettings{T}"/> 的返回值，
        /// 供只想问「有没有」而不需要取对象的场景使用。
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        public static bool IsRegistered<T>() where T : class, new()
        {
            return Managers.ContainsKey(typeof(T));
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
        /// 订阅设置对象<b>被整体替换</b>的变更。
        /// <para>订阅时立即同步回调当前对象，之后在 <see cref="Apply{T}"/>、<see cref="Load{T}"/>、
        /// <see cref="Reset{T}"/> 时回调。</para>
        /// <para><b>字段级变化不经此订阅</b>——请用 <see cref="Ref{T, TField}"/> 取得句柄后订阅。
        /// 本订阅用于「设置对象被换掉了」的场景：订阅者据此改读新的当前实例。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="callback">设置对象被替换时的回调，参数为新的当前对象。</param>
        /// <returns>取消订阅的 <see cref="IDisposable"/>。</returns>
        /// <exception cref="InvalidOperationException">未初始化该类型时抛出。</exception>
        public static IDisposable Observe<T>(Action<T> callback) where T : class, new()
        {
            return GetManager<T>().Observe(callback);
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
        /// 构造指定类型的默认设置文件路径：<c>{persistentDataPath}/{类型短名}.json</c>。
        /// </summary>
        private static string BuildDefaultFilePath(Type type)
        {
            return UnityEngine.Application.persistentDataPath + "/" + type.Name + ".json";
        }

        /// <summary>
        /// 尝试获取指定类型的 <see cref="ISettingsManager{T}"/> 实例，未注册时返回 <c>false</c> 而不抛异常。
        /// </summary>
        private static bool TryGetManager<T>(out ISettingsManager<T> manager) where T : class, new()
        {
            if (Managers.TryGetValue(typeof(T), out var found))
            {
                manager = (ISettingsManager<T>)found;
                return true;
            }

            manager = null;
            return false;
        }

        /// <summary>
        /// 获取指定类型的 <see cref="ISettingsManager{T}"/> 实例，未注册时抛异常并附修复提示。
        /// </summary>
        private static ISettingsManager<T> GetManager<T>() where T : class, new()
        {
            if (TryGetManager<T>(out var manager))
                return manager;

            var type = typeof(T);
            throw new InvalidOperationException(
                $"[SettingsManager] SettingsManager 尚未初始化类型 '{type.Name}'。" +
                $"请先调用 SettingsManager.Initialize<{type.Name}>() 完成初始化。");
        }

        #endregion
    }
}