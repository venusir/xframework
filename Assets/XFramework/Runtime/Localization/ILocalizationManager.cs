using System;
using System.Collections.Generic;

namespace XFramework.XLocalization
{
    /// <summary>
    /// 本地化管理器公共接口。与节点树无关，可供任何对象直接使用。
    /// <para>通过 <see cref="LocalizationManager"/> 的静态方法直接调用，或注入 <see cref="ILocalizationManager"/> 实例使用。</para>
    /// <para>数据来源于 JSON 文件（如 Luban 生成的表），通过 <see cref="LocalizationManager.SwitchLanguageAsync"/> 按需异步加载。</para>
    /// <para>语言使用 <see cref="string"/> 标识，如 <c>"zh_Hans"</c>, <c>"en"</c>, <c>"ja"</c>，也可自定义任意标识。</para>
    /// </summary>
    public interface ILocalizationManager : IDisposable
    {
        /// <summary>
        /// 是否已初始化。
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 当前语言。
        /// </summary>
        string CurrentLanguage { get; }

        /// <summary>
        /// 默认回退语言。当当前语言找不到对应键值时使用。
        /// </summary>
        string FallbackLanguage { get; set; }

        /// <summary>
        /// 语言切换事件。参数为新语言标识。
        /// </summary>
        event Action<string> OnLanguageChanged;

        /// <summary>
        /// 语言数据文件的 YooAsset 地址模板。
        /// <para>使用 <c>string.Format(LanguageAssetPath, lang)</c> 拼接后通过 <see cref="XAsset.AssetManager"/> 加载。</para>
        /// <para>示例：<c>"localization/lang_{0}"</c> → 加载 <c>"localization/lang_ja"</c>, <c>"localization/lang_en"</c> 等。</para>
        /// <para>文件格式为 JSON，内容为 <c>{"key": "value", ...}</c> 的键值对。</para>
        /// </summary>
        string LanguageAssetPath { get; set; }

        /// <summary>
        /// 注入指定语言的全部键值对数据。
        /// <para>多次调用同一语言会覆盖已有数据。</para>
        /// <para>注意：内存中使用小缓存（最多 4 种语言），当前语言和回退语言始终保留。超出上限时按 LRU 淘汰最旧的普通缓存。</para>
        /// </summary>
        void SetLanguageData(string lang, Dictionary<string, string> data);

        /// <summary>
        /// 同步切换到指定语言。切换后触发 <see cref="OnLanguageChanged"/> 事件。
        /// <para>仅当目标语言已在缓存中时可用，否则抛 <see cref="InvalidOperationException"/>。</para>
        /// <para>目标语言未缓存时，请使用 <see cref="LanguageSwitchNode"/> 进行异步加载切换。</para>
        /// </summary>
        void SetLanguage(string lang);

        /// <summary>
        /// 判断指定语言是否已在缓存中。返回 <c>true</c> 时 <see cref="SetLanguage"/> 可安全调用。
        /// </summary>
        bool HasLanguage(string lang);

        /// <summary>
        /// 获取指定键的本地化文本。找不到时返回回退语言的值，回退也找不到时返回键本身。
        /// </summary>
        string Get(string key);

        /// <summary>
        /// 获取指定键的本地化文本，并用参数格式化。
        /// <para>内部使用 <c>string.Format</c>，参数装箱开销不可避免，如需极致性能请缓存格式化结果。</para>
        /// </summary>
        string GetFormat(string key, params object[] args);

        /// <summary>
        /// 判断指定键在当前语言或回退语言中是否存在。
        /// </summary>
        bool ContainsKey(string key);
    }
}