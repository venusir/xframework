using System;
using System.Collections.Generic;
using XFramework.XMessage;

namespace XFramework.XLocalization
{
    /// <summary>
    /// <see cref="ILocalizationManager"/> 的默认实现。
    /// <para>内存中维护一个小缓存（最多 4 种语言），当前语言和回退语言始终保留，其余按 LRU 淘汰。</para>
    /// <para>切换语言时优先从缓存命中，未命中时经 <see cref="LocalizationManager.SwitchLanguageAsync"/> 异步加载。</para>
    /// <para>切换语言时通过 <see cref="MessageManager.Publish"/> 发送 <see cref="LanguageChangedMessage"/>。</para>
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

        /// <summary>
        /// 全局占位符表（字面量）。key: 占位符名称, value: 替换值。
        /// <para>与 <see cref="_placeholderKeys"/> <b>互斥</b>：一个占位符名只有一种含义，后注册的覆盖前者。</para>
        /// </summary>
        private Dictionary<string, string> _placeholders;

        /// <summary>
        /// 全局占位符表（指向语言表项）。key: 占位符名称, value: 语言表键。
        /// <para>替换发生的当下经 <see cref="GetRaw"/> 解析，因此天然跟随语言切换——这是它相对
        /// <see cref="_placeholders"/> 的全部意义。</para>
        /// </summary>
        private Dictionary<string, string> _placeholderKeys;

        private string _currentLanguage;
        private string _fallbackLanguage;

        #endregion

        #region Properties

        public string CurrentLanguage => _currentLanguage;

        public string FallbackLanguage
        {
            get => _fallbackLanguage;
            set => _fallbackLanguage = value;
        }

        /// <summary>
        /// 语言数据文件的 YooAsset 地址模板。默认为 <c>"localization/lang_{0}"</c>。
        /// <para>拼接示例：<c>string.Format("localization/lang_{0}", "ja")</c> → <c>"localization/lang_ja"</c></para>
        /// </summary>
        public string LanguageAssetPath { get; set; } = "localization/lang_{0}";

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
        }

        public void SetLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang))
                throw new ArgumentNullException(nameof(lang));
            if (_currentLanguage == lang)
                return;

            if (!_cache.ContainsKey(lang))
                throw new InvalidOperationException(
                    $"[LocalizationManager] Language '{lang}' is not cached. Use SwitchLanguageAsync for async loading instead of SetLanguage().");

            _currentLanguage = lang;
            TouchLanguage(lang); // 标记为最近使用
            MessageManager.Publish(new LanguageChangedMessage(lang));
        }

        /// <summary>
        /// 判断指定语言是否已在缓存中（可安全地通过 <see cref="SetLanguage"/> 同步切换）。
        /// </summary>
        /// <returns><c>true</c> 表示该语言数据已在缓存中，调用 <see cref="SetLanguage"/> 不会抛异常。</returns>
        public bool HasLanguage(string lang)
        {
            // 问句对空值答「不能」而不是抛：本方法的文档承诺是「返回 true 时 SetLanguage 可安全调用」，
            // 而 SetLanguage(null) 会抛——答 false 正是对这条承诺的如实回答
            if (string.IsNullOrEmpty(lang))
                return false;

            return _cache.ContainsKey(lang);
        }

        public string Get(string key)
        {
            // 查找逻辑只有 GetRaw 一份，占位符替换是这条路径唯一的差别
            return ReplacePlaceholders(GetRaw(key));
        }

        public string GetFormat(string key, params object[] args)
        {
            var raw = ReplacePlaceholders(GetRaw(key));
            return string.Format(NormalizeBracesForFormat(raw), args);
        }

        public bool ContainsKey(string key)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(_currentLanguage))
                return false;

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
                var evicted = false;
                for (int i = 0; i < _loadOrder.Count; i++)
                {
                    var lang = _loadOrder[i];
                    if (lang != _currentLanguage && lang != _fallbackLanguage)
                    {
                        _cache.Remove(lang);
                        _loadOrder.RemoveAt(i);
                        evicted = true;
                        break;
                    }
                }

                // 找不到候选就退出，而不是继续空转。当前 + 回退至多两个不同值，Count > MaxCachedLanguages
                // 时正常必有候选，所以这一支今天不可达——但「必有候选」依赖 _loadOrder ⊆ _cache 这条
                // 只靠调用点自律的隐式不变式，一旦脱节，旧写法就是一个**主线程死循环**。
                // 宁可暂时超限，也不要把数据问题升级成挂死。
                if (!evicted)
                    return;
            }
        }

        #endregion

        #region Internal — Init from Bootstrap

        /// <summary>
        /// 便捷初始化方法：设置默认语言及其数据，同时作为回退语言。
        /// <para>由 <see cref="LocalizationBootstrapStage"/> 等内部代码调用。</para>
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
            TouchLanguage(defaultLanguage); // 已存在则移到尾部，不重复入列（_loadOrder 与 _cache 必须同步）
        }

        #endregion

        #region Placeholder

        public void SetPlaceholder(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            if (_placeholders == null)
                _placeholders = new Dictionary<string, string>();

            _placeholders[key] = value;
            _placeholderKeys?.Remove(key); // 一个名字只有一种含义
        }

        public void SetPlaceholderFromKey(string key, string localizationKey)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrEmpty(localizationKey))
                throw new ArgumentNullException(nameof(localizationKey));

            if (_placeholderKeys == null)
                _placeholderKeys = new Dictionary<string, string>();

            _placeholderKeys[key] = localizationKey;
            _placeholders?.Remove(key); // 一个名字只有一种含义
        }

        public void RemovePlaceholder(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            _placeholders?.Remove(key);
            _placeholderKeys?.Remove(key);
        }

        public void ClearPlaceholders()
        {
            _placeholders?.Clear();
            _placeholderKeys?.Clear();
        }

        public bool HasPlaceholder(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            return (_placeholders != null && _placeholders.ContainsKey(key))
                || (_placeholderKeys != null && _placeholderKeys.ContainsKey(key));
        }

        #endregion

        #region Internal — Placeholder Replacement

        /// <summary>
        /// 获取原始本地化文本（不做占位符替换），供 <see cref="GetFormat"/> 使用。
        /// </summary>
        private string GetRaw(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            // 没有当前语言 = 尚未初始化，或已被 Dispose 复位。此时缓存必然是空的，直接按「找不到」处理——
            // 否则下面会去查 null 键，抛出的是不带模块前缀的裸 ArgumentNullException(key)
            // （经 SetInstance 注入一个未初始化的实现即可触发）
            if (string.IsNullOrEmpty(_currentLanguage))
                return key;

            if (_cache.TryGetValue(_currentLanguage, out var currentDict)
                && currentDict.TryGetValue(key, out var value))
                return value;

            if (_currentLanguage != _fallbackLanguage
                && _cache.TryGetValue(_fallbackLanguage, out var fallbackDict)
                && fallbackDict.TryGetValue(key, out var fallbackValue))
                return fallbackValue;

            return key;
        }

        /// <summary>
        /// 将文本中的 <c>{Key}</c> 占位符替换为对应值。
        /// <para>使用一次扫描 + StringBuilder 实现，避免多次 string.Replace 的 GC 分配。</para>
        /// <para>两类占位符：字面量（<see cref="_placeholders"/>）与指向语言表项的
        /// （<see cref="_placeholderKeys"/>，替换发生的当下经 <see cref="GetRaw"/> 解析，故跟随语言切换）。
        /// 先查字面量，再查表项；都没有则保持原样。</para>
        /// <para><b>单趟、不递归</b>：替换进去的值不会被二次扫描，值里再含 <c>{Other}</c> 只按字面输出。</para>
        /// </summary>
        private string ReplacePlaceholders(string text)
        {
            // 两张表都空才走快路径——注册了键值绑定却不扫描，是这条早退最容易出的错
            var noLiterals = _placeholders == null || _placeholders.Count == 0;
            var noKeyBindings = _placeholderKeys == null || _placeholderKeys.Count == 0;
            if (noLiterals && noKeyBindings)
                return text;

            if (string.IsNullOrEmpty(text))
                return text;

            var span = text.AsSpan();
            // 快速检查是否包含 '{'，避免不必要的 StringBuilder 分配
            var firstBrace = span.IndexOf('{');
            if (firstBrace < 0)
                return text;

            var sb = new System.Text.StringBuilder(text.Length + 64);
            var pos = 0;

            while (pos < span.Length)
            {
                var braceIdx = span.Slice(pos).IndexOf('{');
                if (braceIdx < 0)
                {
                    // 没有更多 '{'，追加剩余部分
                    sb.Append(span.Slice(pos));
                    break;
                }

                // 追加 '{' 之前的文本
                sb.Append(span.Slice(pos, braceIdx));

                var scanPos = pos + braceIdx + 1; // 跳过 '{'
                var closeIdx = span.Slice(scanPos).IndexOf('}');
                if (closeIdx < 0)
                {
                    // 没有匹配的 '}'，原样输出剩余的 '{'
                    sb.Append('{');
                    pos = scanPos;
                    continue;
                }

                var placeholderKey = span.Slice(scanPos, closeIdx).ToString();
                if (_placeholders != null && _placeholders.TryGetValue(placeholderKey, out var replacement))
                {
                    sb.Append(replacement);
                }
                else if (_placeholderKeys != null && _placeholderKeys.TryGetValue(placeholderKey, out var localizationKey))
                {
                    // 在替换的当下解析，所以语言切了它也跟着变；GetRaw 的缺键行为（返回键本身）就是这里的规则
                    sb.Append(GetRaw(localizationKey));
                }
                else
                {
                    // 未注册的占位符保持原样
                    sb.Append('{');
                    sb.Append(placeholderKey);
                    sb.Append('}');
                }

                pos = scanPos + closeIdx + 1;
            }

            return sb.ToString();
        }

        /// <summary>
        /// 把「不是合法复合格式项」的花括号转义为字面量，供 <see cref="GetFormat"/> 交给
        /// <c>string.Format</c> 之前调用。
        /// <para><b>为什么需要它</b>：<see cref="ReplacePlaceholders"/> 对未注册的 <c>{Name}</c> 承诺
        /// 「保持原样输出」，而 <c>string.Format</c> 会把 <c>{Name}</c> 当格式项解析——两条承诺合起来
        /// 就是一个 <c>FormatException</c>。于是「命名占位符」与「位置参数」这两套<b>共用花括号语法</b>
        /// 的能力根本不能共存，README 里那个 <c>GetFormat("ui_guild_info", "5")</c> 的示例就是反例。</para>
        /// <para><b>规则</b>：放行合法复合格式项（<c>{0}</c> / <c>{0,-5}</c> / <c>{0:N2}</c>）与已转义的
        /// <c>{{</c> / <c>}}</c>，其余花括号一律转义。由这条规则可得：**当前能正常格式化的文本输出逐字不变**，
        /// 只把原先会抛 <c>FormatException</c> 的文本改为原样显示。</para>
        /// </summary>
        private static string NormalizeBracesForFormat(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var span = text.AsSpan();
            if (span.IndexOfAny('{', '}') < 0)
                return text; // 无花括号：原样返回同一个实例，不分配

            var sb = new System.Text.StringBuilder(text.Length + 16);
            var i = 0;
            while (i < span.Length)
            {
                var c = span[i];

                if (c == '{')
                {
                    if (i + 1 < span.Length && span[i + 1] == '{')
                    {
                        sb.Append("{{"); // 已是转义，原样放行
                        i += 2;
                        continue;
                    }

                    if (TryReadCompositeFormatItem(span, i, out var end))
                    {
                        sb.Append(span.Slice(i, end - i));
                        i = end;
                        continue;
                    }

                    sb.Append("{{"); // 非格式项（含未注册的 {Name}）→ 输出为字面量 '{'
                    i++;
                    continue;
                }

                if (c == '}')
                {
                    if (i + 1 < span.Length && span[i + 1] == '}')
                    {
                        sb.Append("}}");
                        i += 2;
                        continue;
                    }

                    sb.Append("}}"); // 游离 '}' 在 string.Format 里是硬错误，表文本里却可能真实存在
                    i++;
                    continue;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// 从 <paramref name="start"/>（指向 <c>'{'</c>）起尝试读一个合法的复合格式项
        /// <c>{index[,alignment][:formatString]}</c>；成功时 <paramref name="end"/> 指向其后的下标。
        /// <para>这里只做<b>语法</b>判定（能否安全交给 <c>string.Format</c>），不校验 index 是否落在
        /// <c>args</c> 范围内——那是调用方的实参错误，应当由 <c>string.Format</c> 照常报出来。</para>
        /// </summary>
        private static bool TryReadCompositeFormatItem(ReadOnlySpan<char> span, int start, out int end)
        {
            end = start;
            var i = start + 1; // 跳过 '{'

            // index：至少一位数字
            var digitStart = i;
            while (i < span.Length && span[i] >= '0' && span[i] <= '9')
                i++;
            if (i == digitStart)
                return false;

            // 可选的对齐：',' 后跟可选符号与至少一位数字
            if (i < span.Length && span[i] == ',')
            {
                i++;
                if (i < span.Length && (span[i] == '-' || span[i] == '+'))
                    i++;
                digitStart = i;
                while (i < span.Length && span[i] >= '0' && span[i] <= '9')
                    i++;
                if (i == digitStart)
                    return false;
            }

            // 可选的格式说明：':' 之后到第一个 '}' 为止
            if (i < span.Length && span[i] == ':')
            {
                i++;
                while (i < span.Length && span[i] != '}')
                    i++;
            }

            if (i >= span.Length || span[i] != '}')
                return false;

            end = i + 1;
            return true;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 复位到「未初始化」。各字段要一起复位：<see cref="LanguageAssetPath"/> 与缓存都清了，
        /// 却留着语言标识的话，持有本实例的调用方（<c>SetInstance</c> 复用、测试直连）会读到一个
        /// 「已销毁但当前语言还在」的半状态。
        /// </summary>
        public void Dispose()
        {
            _cache.Clear();
            _loadOrder.Clear();
            _placeholders?.Clear();
            _placeholders = null;
            _placeholderKeys?.Clear();
            _placeholderKeys = null;
            _currentLanguage = null;
            _fallbackLanguage = null;
            LanguageAssetPath = null;
        }

        #endregion
    }
}
