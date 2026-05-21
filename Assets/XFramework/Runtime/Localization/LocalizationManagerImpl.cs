using System;
using System.Collections.Generic;

namespace XFramework.XLocalization
{
    /// <summary>
    /// <see cref="ILocalizationManager"/> 的默认实现。
    /// <para>内存中维护一个小缓存（最多 4 种语言），当前语言和回退语言始终保留，其余按 LRU 淘汰。</para>
    /// <para>切换语言时优先从缓存命中，未命中时由 <see cref="LanguageSwitchNode"/> 通过 <see cref="LanguageAssetPath"/> 异步加载。</para>
    /// </summary>
    internal sealed class LocalizationManagerImpl : ILocalizationManager
    {
        #region Constants

        /// <summary>
        /// 缓存上限。当前语言和回退语言始终保留，不计入 LRU 淘汰范围。
        /// </summary>
        private const int MaxCachedLanguages = 4;

        #endregion

        #region Fields

        /// <summary>
        /// 语言数据缓存。key: 语言标识, value: 键值对数据。
        /// <para>最多缓存 <see cref="MaxCachedLanguages"/> 种语言，超出时按 LRU 淘汰。</para>
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, string>> _cache
            = new Dictionary<string, Dictionary<string, string>>(MaxCachedLanguages);

        /// <summary>
        /// 加载顺序列表，用于 LRU 淘汰。列表尾部为最近使用的语言。
        /// </summary>
        private readonly List<string> _loadOrder = new List<string>(MaxCachedLanguages);

        private string _currentLanguage;
        private string _fallbackLanguage;

        #endregion

        #region Properties

        public bool IsInitialized { get; private set; }

        public string CurrentLanguage => _currentLanguage;

        public string FallbackLanguage
        {
            get => _fallbackLanguage;
            set
            {
                if (_fallbackLanguage == value)
                    return;

                _fallbackLanguage = value;
            }
        }

        /// <summary>
        /// 语言数据文件的 YooAsset 地址模板。默认为 <c>"localization/lang_{0}"</c>。
        /// <para>拼接示例：<c>string.Format("localization/lang_{0}", "ja")</c> → <c>"localization/lang_ja"</c></para>
        /// </summary>
        public string LanguageAssetPath { get; set; } = "localization/lang_{0}";

        #endregion

        #region Events

        public event Action<string> OnLanguageChanged;

        #endregion

        #region I18n

        public void SetLanguageData(string lang, Dictionary<string, string> data)
        {
            if (string.IsNullOrEmpty(lang))
                throw new ArgumentNullException(nameof(lang));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (_cache.ContainsKey(lang))
            {
                _cache[lang] = data;
                TouchLanguage(lang);
            }
            else
            {
                _cache[lang] = data;
                _loadOrder.Add(lang);
                EvictIfNeeded();
            }

            IsInitialized = true;
        }

        public void SetLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang))
                throw new ArgumentNullException(nameof(lang));
            if (_currentLanguage == lang)
                return;

            if (!_cache.ContainsKey(lang))
                throw new InvalidOperationException(
                    $"[LocalizationManager] Language '{lang}' is not cached. Use LanguageSwitchNode for async loading instead of SetLanguage().");

            _currentLanguage = lang;
            TouchLanguage(lang); // 标记为最近使用
            OnLanguageChanged?.Invoke(lang);
        }

        /// <summary>
        /// 判断指定语言是否已在缓存中（可安全地通过 <see cref="SetLanguage"/> 同步切换）。
        /// </summary>
        /// <returns><c>true</c> 表示该语言数据已在缓存中，调用 <see cref="SetLanguage"/> 不会抛异常。</returns>
        public bool HasLanguage(string lang)
        {
            return _cache.ContainsKey(lang);
        }

        public string Get(string key)
        {
            // 先从当前语言查找
            if (_cache.TryGetValue(_currentLanguage, out var currentDict)
                && currentDict.TryGetValue(key, out var value))
                return value;

            // 回退语言查找（仅当回退与当前不同）
            if (_currentLanguage != _fallbackLanguage
                && _cache.TryGetValue(_fallbackLanguage, out var fallbackDict)
                && fallbackDict.TryGetValue(key, out var fallbackValue))
                return fallbackValue;

            return key; // 找不到返回键本身，方便调试
        }

        public string GetFormat(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        public bool ContainsKey(string key)
        {
            if (_cache.TryGetValue(_currentLanguage, out var currentDict) && currentDict.ContainsKey(key))
                return true;

            if (_currentLanguage != _fallbackLanguage
                && _cache.TryGetValue(_fallbackLanguage, out var fallbackDict)
                && fallbackDict.ContainsKey(key))
                return true;

            return false;
        }

        #endregion

        #region Internal — LRU Cache

        /// <summary>
        /// 将指定语言标记为最近使用（移到 <see cref="_loadOrder"/> 尾部）。
        /// </summary>
        private void TouchLanguage(string lang)
        {
            _loadOrder.Remove(lang);
            _loadOrder.Add(lang);
        }

        /// <summary>
        /// 缓存超出上限时，淘汰 <see cref="_loadOrder"/> 中最旧的、非当前语言、非回退语言的数据。
        /// </summary>
        private void EvictIfNeeded()
        {
            while (_cache.Count > MaxCachedLanguages)
            {
                for (int i = 0; i < _loadOrder.Count; i++)
                {
                    var lang = _loadOrder[i];
                    if (lang != _currentLanguage && lang != _fallbackLanguage)
                    {
                        _cache.Remove(lang);
                        _loadOrder.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        #endregion

        #region Internal — Init from Bootstrap

        /// <summary>
        /// 便捷初始化方法：设置默认语言及其数据，同时作为回退语言。
        /// <para>由 <see cref="LocalizationBootstrapNode"/> 等内部代码调用。</para>
        /// </summary>
        internal void InitWithDefault(string defaultLanguage, Dictionary<string, string> data)
        {
            if (string.IsNullOrEmpty(defaultLanguage))
                throw new ArgumentNullException(nameof(defaultLanguage));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            _currentLanguage = defaultLanguage;
            _fallbackLanguage = defaultLanguage;

            _cache[defaultLanguage] = data;
            _loadOrder.Add(defaultLanguage);

            IsInitialized = true;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _cache.Clear();
            _loadOrder.Clear();
            IsInitialized = false;
            OnLanguageChanged = null;
            LanguageAssetPath = null;
        }

        #endregion
    }
}