using System;
using System.Collections.Generic;

namespace XFramework.XLocalization
{
    /// <summary>
    /// 本地化管理器公共接口。与节点树无关，可供任何对象直接使用。
    /// <para>通过 <see cref="LocalizationManager"/> 的静态方法直接调用，或注入 <see cref="ILocalizationManager"/> 实例使用。</para>
    /// <para>数据来源于 JSON 文件（如 Luban 生成的表），通过 <see cref="LocalizationManager.SwitchLanguageAsync"/> 按需异步加载。</para>
    /// <para>语言使用 <see cref="string"/> 标识，如 <c>"zh_Hans"</c>, <c>"en"</c>, <c>"ja"</c>，也可自定义任意标识。</para>
    /// <para>语言切换通知通过 <see cref="XReactive.MessageManager.Publish{TMessage}"/> 发送 <see cref="LanguageChangedMessage"/>，
    /// 可通过 <c>MessageManager.Subscribe<LanguageChangedMessage>(handler)</c> 监听。</para>
    /// </summary>
    public interface ILocalizationManager : IDisposable
    {
        /// <summary>
        /// 当前使用的语言代码。
        /// </summary>
        string CurrentLanguage { get; }

        /// <summary>
        /// 获取或设置回退语言。当目标键在当前语言中不存在时自动使用回退语言的值。
        /// </summary>
        string FallbackLanguage { get; set; }

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
        /// 同步切换到指定语言。切换后通过 <see cref="XReactive.MessageManager"/> 发送 <see cref="LanguageChangedMessage"/>。
        /// <para>仅当目标语言已在缓存中时可用，否则抛 <see cref="InvalidOperationException"/>。</para>
        /// <para>目标语言未缓存时，请使用 <see cref="LocalizationManager.SwitchLanguageAsync"/> 进行异步加载切换。</para>
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

        #region Placeholder

        /// <summary>
        /// 设置全局占位符。在 <see cref="Get"/> / <see cref="GetFormat"/> 时自动替换文本中的 <c>{Key}</c>。
        /// <para>示例：<c>SetPlaceholder("PlayerName", "张三")</c> 后，
        /// <c>Get("ui_welcome")</c> 中 <c>{PlayerName}</c> 将被替换为 <c>"张三"</c>。</para>
        /// <para>占位符替换在 <see cref="GetFormat"/> 的 <c>string.Format</c> 之前执行。</para>
        /// </summary>
        void SetPlaceholder(string key, string value);

        /// <summary>
        /// 移除指定全局占位符。
        /// </summary>
        void RemovePlaceholder(string key);

        /// <summary>
        /// 清空所有全局占位符。
        /// </summary>
        void ClearPlaceholders();

        /// <summary>
        /// 判断指定全局占位符是否存在。
        /// </summary>
        bool HasPlaceholder(string key);

        #endregion
    }
}