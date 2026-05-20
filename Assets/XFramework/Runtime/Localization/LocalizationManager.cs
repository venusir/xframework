using System;
using System.Collections.Generic;

namespace XFramework.XLocalization
{
    /// <summary>
    /// 全局本地化管理器外观。提供静态方法直接访问多语言文本。
    /// <para>内部持有 <see cref="ILocalizationManager"/> 实例（<see cref="LocalizationManagerImpl"/>），所有调用委托到该实例。</para>
    /// <para>使用前需调用 <see cref="Initialize"/> 注入至少一个语言的数据。</para>
    /// <para>数据来源于外部（如 Luban 生成的表、JSON 文件等），本模块不关心数据来源。</para>
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
        /// <para>其他语言的数据通过 <see cref="SetLanguageData"/> 后续注入。</para>
        /// </summary>
        /// <param name="defaultLanguage">默认语言标识，如 <c>"zh_Hans"</c>, <c>"en"</c></param>
        /// <param name="data">键值对数据</param>
        public static void Initialize(string defaultLanguage, Dictionary<string, string> data)
        {
            if (_instanceInitialized)
            {
                UnityEngine.Debug.LogWarning("[LocalizationManager] Initialize was called more than once. Ignoring duplicate.");
                return;
            }

            var impl = new LocalizationManagerImpl();
            impl.SetLanguageData(defaultLanguage, data);
            impl.SetLanguage(defaultLanguage);

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