using System;
using System.Collections.Generic;

namespace XFramework.XLocalization
{
    /// <summary>
    /// <see cref="ILocalizationManager"/> 的默认实现。
    /// <para>使用 <see cref="Dictionary{TKey, TValue}"/> 存储各语言键值对，支持运行时切换语言和回退。</para>
    /// </summary>
    internal sealed class LocalizationManagerImpl : ILocalizationManager
    {
        #region Fields

        /// <summary>
        /// 各语言的键值对数据。key: 语言标识(string), value: Dictionary{string, string}。
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, string>> _data
            = new Dictionary<string, Dictionary<string, string>>();

        private string _currentLanguage = "zh_Hans";
        private string _fallbackLanguage = "zh_Hans";

        #endregion

        #region Properties

        public bool IsInitialized { get; private set; }

        public string CurrentLanguage => _currentLanguage;

        public string FallbackLanguage
        {
            get => _fallbackLanguage;
            set => _fallbackLanguage = value;
        }

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

            _data[lang] = data;
            IsInitialized = true;
        }

        public void SetLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang))
                throw new ArgumentNullException(nameof(lang));
            if (_currentLanguage == lang) return;
            _currentLanguage = lang;
            OnLanguageChanged?.Invoke(lang);
        }

        public string Get(string key)
        {
            if (_data.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var value))
                return value;

            // 回退语言查找
            if (_currentLanguage != _fallbackLanguage
                && _data.TryGetValue(_fallbackLanguage, out var fallbackDict)
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
            if (_data.TryGetValue(_currentLanguage, out var dict) && dict.ContainsKey(key))
                return true;

            if (_currentLanguage != _fallbackLanguage
                && _data.TryGetValue(_fallbackLanguage, out var fallbackDict))
                return fallbackDict.ContainsKey(key);

            return false;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _data.Clear();
            IsInitialized = false;
            OnLanguageChanged = null;
        }

        #endregion
    }
}