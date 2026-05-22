using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XLocalization
{
    /// <summary>
    /// 全局本地化管理器外观。提供静态方法直接访问多语言文本。
    /// <para>内部持有 <see cref="ILocalizationManager"/> 实例（<see cref="LocalizationManagerImpl"/>），所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="Initialize"/> 注入至少一个语言的数据。</para>
    /// <para>内存中维护小缓存（最多 4 种语言），当前语言和回退语言始终保留，其余按 LRU 淘汰。切换语言时优先从缓存命中，未命中时通过 <see cref="LanguageAssetPath"/> 异步加载对应语言的 JSON 文件。</para>
    /// </summary>
    public static class LocalizationManager
    {
        #region Static — Global Singleton

        private static ILocalizationManager _instance;
        private static bool _instanceInitialized;

        /// <summary>
        /// 全局本地化管理器是否已初始化。
        /// </summary>
        public static bool IsInitialized => _instanceInitialized && _instance != null;

        /// <summary>
        /// 初始化全局本地化管理器。传入默认语言和该语言的数据。
        /// <para>其他语言数据通过 <see cref="SwitchLanguageAsync"/> 按需异步加载，无需预先全部注入。</para>
        /// </summary>
        /// <param name="defaultLanguage">默认语言标识，如 <c>"zh_Hans"</c>, <c>"en"</c></param>
        /// <param name="data">默认语言的键值对数据</param>
        public static void Initialize(string defaultLanguage, Dictionary<string, string> data)
        {
            if (_instanceInitialized)
            {
                UnityEngine.Debug.LogWarning("[LocalizationManager] Initialize was called more than once. Ignoring duplicate.");
                return;
            }

            var impl = new LocalizationManagerImpl();
            impl.InitWithDefault(defaultLanguage, data);

            _instance = impl;
            _instanceInitialized = true;
        }

        /// <summary>
        /// 设置外部已创建的实例作为全局管理器。
        /// <para>适用于依赖注入或单元测试场景。</para>
        /// </summary>
        public static void SetInstance(ILocalizationManager manager)
        {
            _instance = manager ?? throw new ArgumentNullException(nameof(manager));
            _instanceInitialized = true;
        }

        /// <summary>
        /// 销毁全局本地化管理器，释放所有资源。
        /// </summary>
        public static void Destroy()
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
            _instanceInitialized = false;
        }

        #endregion

        #region Public API — Language Asset Path

        /// <summary>
        /// 语言数据文件的 YooAsset 地址模板。
        /// <para>默认为 <c>"localization/lang_{0}"</c>。切换语言时，通过 <see cref="SwitchLanguageAsync"/> 使用此模板拼接地址。</para>
        /// <para>拼接示例：<c>string.Format(LanguageAssetPath, "ja")</c> → <c>"localization/lang_ja"</c></para>
        /// <para>数据文件需为 <see cref="TextAsset"/>，内容为 JSON 格式的键值对：<c>{"key": "value", ...}</c></para>
        /// </summary>
        public static string LanguageAssetPath
        {
            get
            {
                EnsureGlobalInitialized();
                return _instance.LanguageAssetPath;
            }
            set
            {
                EnsureGlobalInitialized();
                _instance.LanguageAssetPath = value;
            }
        }

        #endregion

        #region Public API — Language Data

        /// <inheritdoc cref="ILocalizationManager.SetLanguageData"/>
        public static void SetLanguageData(string lang, Dictionary<string, string> data)
        {
            EnsureGlobalInitialized();
            _instance.SetLanguageData(lang, data);
        }

        /// <inheritdoc cref="ILocalizationManager.SetLanguage"/>
        public static void SetLanguage(string lang)
        {
            EnsureGlobalInitialized();
            _instance.SetLanguage(lang);
        }

        /// <summary>
        /// 判断指定语言是否已在缓存中。
        /// <para>返回 <c>true</c> 时 <see cref="SetLanguage"/> 可安全同步调用。</para>
        /// </summary>
        public static bool HasLanguage(string lang)
        {
            EnsureGlobalInitialized();
            return _instance.HasLanguage(lang);
        }

        /// <summary>
        /// 异步切换到指定语言。通过 <see cref="LanguageSwitchNode"/> 加载目标语言数据。
        /// <para>内部自动使用 <see cref="LanguageAssetPath"/> 拼接资产地址，通过 <see cref="XAsset.AssetManager.LoadAsync{T}(string, CancellationToken)"/> 加载 JSON 文件。</para>
        /// <para>已在缓存中的语言会直接同步切换，无需异步加载。</para>
        /// <para>支持 <paramref name="cancellationToken"/> 取消正在进行的加载任务。</para>
        /// </summary>
        /// <param name="lang">目标语言标识，如 <c>"ja"</c>, <c>"en"</c></param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async UniTask SwitchLanguageAsync(string lang, CancellationToken cancellationToken = default)
        {
            EnsureGlobalInitialized();

            // 已在缓存中，直接同步切换
            if (_instance.HasLanguage(lang))
            {
                _instance.SetLanguage(lang);
                return;
            }

            var assetPath = _instance.LanguageAssetPath;
            if (string.IsNullOrEmpty(assetPath))
                throw new InvalidOperationException(
                    "[LocalizationManager] LanguageAssetPath is not set. Configure it before calling SwitchLanguageAsync.");

            var switchNode = new LanguageSwitchNode(lang, assetPath);
            await switchNode.LoadAsync(null, cancellationToken);
        }

        /// <summary>
        /// 当前语言标识。
        /// </summary>
        public static string CurrentLanguage
        {
            get
            {
                EnsureGlobalInitialized();
                return _instance.CurrentLanguage;
            }
        }

        /// <summary>
        /// 默认回退语言。当当前语言找不到对应键值时使用。
        /// </summary>
        public static string FallbackLanguage
        {
            get
            {
                EnsureGlobalInitialized();
                return _instance.FallbackLanguage;
            }
            set
            {
                EnsureGlobalInitialized();
                _instance.FallbackLanguage = value;
            }
        }

        #endregion

        #region Public API — Get

        /// <inheritdoc cref="ILocalizationManager.Get"/>
        public static string Get(string key)
        {
            EnsureGlobalInitialized();
            return _instance.Get(key);
        }

        /// <inheritdoc cref="ILocalizationManager.GetFormat"/>
        public static string GetFormat(string key, params object[] args)
        {
            EnsureGlobalInitialized();
            return _instance.GetFormat(key, args);
        }

        /// <inheritdoc cref="ILocalizationManager.ContainsKey"/>
        public static bool ContainsKey(string key)
        {
            EnsureGlobalInitialized();
            return _instance.ContainsKey(key);
        }

        #endregion

        #region Public API — Placeholder

        /// <inheritdoc cref="ILocalizationManager.SetPlaceholder"/>
        public static void SetPlaceholder(string key, string value)
        {
            EnsureGlobalInitialized();
            _instance.SetPlaceholder(key, value);
        }

        /// <inheritdoc cref="ILocalizationManager.RemovePlaceholder"/>
        public static void RemovePlaceholder(string key)
        {
            EnsureGlobalInitialized();
            _instance.RemovePlaceholder(key);
        }

        /// <inheritdoc cref="ILocalizationManager.ClearPlaceholders"/>
        public static void ClearPlaceholders()
        {
            EnsureGlobalInitialized();
            _instance.ClearPlaceholders();
        }

        /// <inheritdoc cref="ILocalizationManager.HasPlaceholder"/>
        public static bool HasPlaceholder(string key)
        {
            EnsureGlobalInitialized();
            return _instance.HasPlaceholder(key);
        }

        #endregion

        #region Public API — Event

        /// <summary>
        /// 语言切换事件。参数为新语言标识。
        /// </summary>
        public static event Action<string> OnLanguageChanged
        {
            add
            {
                EnsureGlobalInitialized();
                _instance.OnLanguageChanged += value;
            }
            remove
            {
                EnsureGlobalInitialized();
                _instance.OnLanguageChanged -= value;
            }
        }

        #endregion

        #region Internal

        private static void EnsureGlobalInitialized()
        {
            if (!_instanceInitialized || _instance == null)
                throw new InvalidOperationException(
                    "LocalizationManager is not initialized. Call LocalizationManager.Initialize() first.");
        }

        #endregion
    }
}