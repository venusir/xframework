using System;
using System.Collections.Generic;
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
        /// 将 JSON 文本解析为键值对字典。
        /// <para>使用 <see cref="JsonUtility"/> 的轻量封装，
        /// 期望 JSON 格式为 <c>{"key": "value", ...}</c> 的扁平键值对对象。</para>
        /// </summary>
        /// <remarks>
        /// 注意：Unity 的 <see cref="JsonUtility"/> 要求顶层根对象匹配一个可序列化的类。
        /// 由于多语言数据是动态 key，改用最小化分配的手动解析——仅支持 <c>"string": "string"</c> 的简单格式。
        /// 如需完整 JSON 支持，可替换为 <c>Newtonsoft.Json</c> 或 <c>System.Text.Json</c>。
        /// </remarks>
        private static Dictionary<string, string> ParseJson(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            // 简单状态机解析，避免 JsonUtility 无法处理动态 key 的问题
            var span = json.AsSpan();
            var i = 0;
            // 跳过开头的 '{' 和空白
            SkipWhitespace(span, ref i);
            if (i < span.Length && span[i] == '{')
                i++;

            while (i < span.Length)
            {
                SkipWhitespace(span, ref i);
                if (i >= span.Length || span[i] == '}')
                    break;

                // 读取 key（string）
                var key = ReadJsonString(span, ref i);
                SkipWhitespace(span, ref i);
                if (i >= span.Length || span[i] != ':')
                    break;
                i++; // skip ':'
                SkipWhitespace(span, ref i);

                // 读取 value（string）
                var value = ReadJsonString(span, ref i);

                result[key] = value;

                SkipWhitespace(span, ref i);
                if (i < span.Length && span[i] == ',')
                    i++;
            }

            return result;
        }

        private static void SkipWhitespace(ReadOnlySpan<char> span, ref int i)
        {
            while (i < span.Length && (span[i] == ' ' || span[i] == '\t' || span[i] == '\n' || span[i] == '\r'))
                i++;
        }

        private static string ReadJsonString(ReadOnlySpan<char> span, ref int i)
        {
            SkipWhitespace(span, ref i);
            if (i >= span.Length || span[i] != '"')
                return string.Empty;

            i++; // skip opening '"'
            var start = i;
            while (i < span.Length && span[i] != '"')
            {
                // 处理转义字符
                if (span[i] == '\\' && i + 1 < span.Length)
                    i += 2;
                else
                    i++;
            }

            var result = span.Slice(start, i - start).ToString();
            if (i < span.Length && span[i] == '"')
                i++; // skip closing '"'
            return result;
        }

        #endregion
    }
}
