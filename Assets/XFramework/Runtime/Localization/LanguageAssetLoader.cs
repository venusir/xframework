using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XLocalization
{
    /// <summary>
    /// 语言数据异步加载器(模块内部,非节点)。由 <see cref="LocalizationManager.SwitchLanguageAsync"/>
    /// 在缓存未命中时创建并执行:经 <see cref="AssetManager"/> 加载 JSON → 解析 → 注入缓存并切换。
    /// <para>取消抛 <see cref="OperationCanceledException"/>;加载/解析失败抛异常(不静默)。</para>
    /// </summary>
    internal sealed class LanguageAssetLoader
    {
        #region Fields

        private readonly string _targetLanguage;
        private readonly string _assetPathTemplate;

        /// <summary>
        /// 文本加载函数(测试缝):默认经 <see cref="AssetManager"/> 加载 <see cref="TextAsset"/> 并读取文本;
        /// EditMode 测试注入纯函数(带资源内容的句柄依赖 YooAsset 运行环境,测试中不可构造)。
        /// </summary>
        internal Func<string, CancellationToken, UniTask<string>> LoadTextFunc = LoadTextFromAssetAsync;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建语言数据加载器。
        /// </summary>
        /// <param name="targetLanguage">目标语言标识,如 <c>"ja"</c>, <c>"en"</c>。</param>
        /// <param name="assetPathTemplate">
        /// 资产路径模板,如 <c>"localization/lang_{0}"</c>,
        /// 实际加载地址为 <c>string.Format(assetPathTemplate, targetLanguage)</c>。
        /// </param>
        public LanguageAssetLoader(string targetLanguage, string assetPathTemplate)
        {
            _targetLanguage = targetLanguage ?? throw new ArgumentNullException(nameof(targetLanguage));
            _assetPathTemplate = assetPathTemplate ?? throw new ArgumentNullException(nameof(assetPathTemplate));
        }

        #endregion

        #region Public API

        /// <summary>
        /// 执行语言切换。目标语言已在缓存中则直接切换(跳过加载);否则经 <see cref="AssetManager"/>
        /// 加载目标语言 JSON → 解析 → 注入缓存并同步切换。
        /// <para>注:AssetManager 底层(YooAssetManagerImpl)已在加载失败时统一记录 Debug.LogError。</para>
        /// </summary>
        public async UniTask LoadAsync(CancellationToken cancellationToken)
        {
            // 已在缓存中则直接切换,跳过加载
            if (LocalizationManager.HasLanguage(_targetLanguage))
            {
                LocalizationManager.SetLanguage(_targetLanguage);
                return;
            }

            // 通过 AssetManager 加载 JSON 文件(实际地址 = 模板拼接目标语言)
            var assetLocation = string.Format(_assetPathTemplate, _targetLanguage);
            var json = await LoadTextFunc(assetLocation, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 解析 JSON
            var data = ParseJson(json);
            if (data == null || data.Count == 0)
            {
                throw new InvalidOperationException(
                    $"[LanguageAssetLoader] Parsed empty language data for '{_targetLanguage}'.");
            }

            // 注入缓存并同步切换
            LocalizationManager.SetLanguageData(_targetLanguage, data);
            LocalizationManager.SetLanguage(_targetLanguage);
        }

        #endregion

        #region Loading

        /// <summary>经 <see cref="AssetManager"/> 加载 <see cref="TextAsset"/> 并读取文本;资源为 null 抛
        /// <see cref="InvalidOperationException"/>。</summary>
        private static async UniTask<string> LoadTextFromAssetAsync(string location, CancellationToken cancellationToken)
        {
            using var handle = await AssetManager.LoadAsync<TextAsset>(location, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var textAsset = handle.Asset;
            if (textAsset == null)
            {
                throw new InvalidOperationException(
                    $"[LanguageAssetLoader] AssetManager returned null for '{location}'.");
            }

            return textAsset.text;
        }

        #endregion

        #region JSON Parsing

        /// <summary>
        /// 将 JSON 文本解析为键值对字典。期望格式为 <c>{"key": "value", ...}</c> 的扁平对象；
        /// 仅支持字符串键与字符串值，不支持嵌套结构与数组。
        /// <para><b>转义序列会解码</b>：<c>\" \\ \/ \b \f \n \r \t \uXXXX</c>（含代理对）。
        /// 这不是可选项——导出工具常把非 ASCII 转义成 <c>\uXXXX</c>（System.Text.Json 默认如此），
        /// 不解码就是把 <c>中文</c> 原样显示给玩家。</para>
        /// <para><b>宽容与严格的判定线</b>：只对 JSON 规范之外的两种常见手写痕迹保持宽容——
        /// <b>尾随逗号</b>与 <b>UTF-8 BOM</b>（两者都不影响数据完整性）；其余格式错误一律抛
        /// <see cref="InvalidOperationException"/>（带位置）。旧实现在这些位置是 <c>break</c> 掉循环、
        /// 返回已经读到的半张表——调用方拿到非空字典，静默装上一次「所有值都是空串」的语言。</para>
        /// </summary>
        /// <remarks>
        /// 为什么不用 <see cref="JsonUtility"/>：它要求顶层根对象匹配一个可序列化的类，而语言表的键是动态的。
        /// 为什么不用 Newtonsoft.Json / System.Text.Json：本模块自述「零外部依赖」是有意的取舍，见 README「设计取舍」。
        /// </remarks>
        internal static Dictionary<string, string> ParseJson(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            var span = json.AsSpan();
            var i = 0;

            SkipWhitespace(span, ref i);
            if (i >= span.Length)
                return result;
            if (span[i] != '{')
                throw Malformed(json, i, "顶层应为 JSON 对象（'{'）");
            i++;

            while (true)
            {
                SkipWhitespace(span, ref i);
                if (i >= span.Length)
                    throw Malformed(json, i, "对象未闭合（缺 '}'）");
                if (span[i] == '}')
                {
                    // 空对象，或 {"a":"1",} 这样的尾随逗号——都不丢数据，宽容
                    i++;
                    break;
                }

                var key = ReadJsonString(span, ref i, json);
                SkipWhitespace(span, ref i);
                if (i >= span.Length || span[i] != ':')
                    throw Malformed(json, i, "键之后缺 ':'");
                i++; // 跳过 ':'

                var value = ReadJsonString(span, ref i, json);
                result[key] = value;

                SkipWhitespace(span, ref i);
                if (i >= span.Length)
                    throw Malformed(json, i, "对象未闭合（缺 '}'）");
                if (span[i] == ',')
                {
                    i++;
                    continue;
                }
                if (span[i] == '}')
                {
                    i++;
                    break;
                }

                throw Malformed(json, i, "条目之后缺 ',' 或 '}'");
            }

            // 闭合之后只允许空白：把「多贴了一段」这种会让人误以为整份都读进去了的情况也变成硬失败
            SkipWhitespace(span, ref i);
            if (i < span.Length)
                throw Malformed(json, i, "对象闭合之后有多余内容");

            return result;
        }

        /// <summary>
        /// 跳过 JSON 空白。U+FEFF（UTF-8 BOM 解码后的字符）按空白处理——带 BOM 是 Windows 侧工具的常见默认。
        /// <para>Unity 的 <c>TextAsset.text</c> 是否已剥离 BOM 未经实测，这里是解析器自身的兜底。</para>
        /// </summary>
        private static void SkipWhitespace(ReadOnlySpan<char> span, ref int i)
        {
            while (i < span.Length && IsJsonWhitespace(span[i]))
                i++;
        }

        private static bool IsJsonWhitespace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿';
        }

        /// <summary>
        /// 读取一个 JSON 字符串（含引号）。无转义时走零拷贝切片快路径；含转义时解码
        /// <c>\" \\ \/ \b \f \n \r \t \uXXXX</c>。
        /// </summary>
        private static string ReadJsonString(ReadOnlySpan<char> span, ref int i, string json)
        {
            SkipWhitespace(span, ref i);
            if (i >= span.Length || span[i] != '"')
                throw Malformed(json, i, "此处应为双引号包裹的字符串");

            i++; // 跳过开引号
            var start = i;

            // 快路径：绝大多数条目没有转义，直接切到闭引号
            while (i < span.Length && span[i] != '"' && span[i] != '\\')
                i++;

            if (i < span.Length && span[i] == '"')
            {
                var plain = span.Slice(start, i - start).ToString();
                i++; // 跳过闭引号
                return plain;
            }

            return ReadEscapedJsonString(span, ref i, start, json);
        }

        /// <summary>读取含转义序列的 JSON 字符串（<paramref name="i"/> 停在首个 <c>'\'</c> 上）。</summary>
        private static string ReadEscapedJsonString(ReadOnlySpan<char> span, ref int i, int start, string json)
        {
            var sb = new StringBuilder(i - start + 16);
            sb.Append(span.Slice(start, i - start));

            while (i < span.Length)
            {
                var c = span[i];

                if (c == '"')
                {
                    i++; // 跳过闭引号
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                i++; // 跳过 '\'
                if (i >= span.Length)
                    break;

                var escapePosition = i;
                var escape = span[i++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > span.Length)
                            throw Malformed(json, escapePosition, "\\u 转义需要 4 位十六进制");
                        // 代理对由两次 \uXXXX 各追加一个 char 自然拼成
                        sb.Append((char)ParseHex4(span.Slice(i, 4), json, i));
                        i += 4;
                        break;
                    default:
                        throw Malformed(json, escapePosition, $"无法识别的转义序列 '\\{escape}'");
                }
            }

            throw Malformed(json, i, "字符串未闭合（缺 '\"'）");
        }

        /// <summary>解析 <c>\uXXXX</c> 的 4 位十六进制。</summary>
        private static int ParseHex4(ReadOnlySpan<char> hex, string json, int position)
        {
            var value = 0;
            for (var k = 0; k < 4; k++)
            {
                var c = hex[k];
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                else throw Malformed(json, position + k, "\\u 转义需要 4 位十六进制");

                value = (value << 4) | digit;
            }

            return value;
        }

        private static InvalidOperationException Malformed(string json, int position, string reason)
        {
            return new InvalidOperationException(
                $"[LanguageAssetLoader] Malformed language JSON at position {position}: {reason}.");
        }

        #endregion
    }
}
