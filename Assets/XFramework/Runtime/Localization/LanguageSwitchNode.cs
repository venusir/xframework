using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XNode;
using XFramework.XPipeline;

namespace XFramework.XLocalization
{
    /// <summary>
    /// 异步语言切换节点。利用节点树的 <see cref="ILoadable"/> 机制实现异步加载 + 可取消。
    /// <para>通过 <see cref="AssetManager"/> 加载 <see cref="TextAsset"/>，解析 JSON 后注入缓存并切换。</para>
    /// <para>可挂载到节点树由加载管线统一调度，支持 <see cref="LoadProgress"/> 进度报告。</para>
    /// </summary>
    internal sealed class LanguageSwitchNode : LeafNode, ILoadable
    {
        #region Fields

        private readonly string _targetLanguage;
        private readonly string _assetPathTemplate;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建语言切换节点。
        /// </summary>
        /// <param name="targetLanguage">目标语言标识，如 <c>"ja"</c>, <c>"en"</c></param>
        /// <param name="assetPathTemplate">
        /// 资产路径模板，如 <c>"localization/lang_{0}"</c>，
        /// 实际加载地址为 <c>string.Format(assetPathTemplate, targetLanguage)</c>
        /// </param>
        public LanguageSwitchNode(string targetLanguage, string assetPathTemplate)
        {
            _targetLanguage = targetLanguage ?? throw new ArgumentNullException(nameof(targetLanguage));
            _assetPathTemplate = assetPathTemplate ?? throw new ArgumentNullException(nameof(assetPathTemplate));
        }

        #endregion

        #region ILoadable Implementation

        public int Phase => 0;

        public async UniTask LoadAsync(LoadProgress progress, CancellationToken cancellationToken)
        {
            if (progress != null)
            {
                progress.SetState(LoadState.Loading);
                progress.SetDescription($"Switching language to {_targetLanguage}...");
                progress.SetProgress(0f);
            }

            // 已在缓存中则直接切换，跳过加载
            if (LocalizationManager.HasLanguage(_targetLanguage))
            {
                LocalizationManager.SetLanguage(_targetLanguage);
                if (progress != null)
                {
                    progress.SetProgress(1f);
                    progress.SetState(LoadState.Completed);
                }
                return;
            }

            // 通过 AssetManager 加载 JSON 文件
            // 注：AssetManager 底层（YooAssetManagerImpl）已在加载失败时统一记录 Debug.LogError
            var assetLocation = string.Format(_assetPathTemplate, _targetLanguage);

            using var handle = await AssetManager.LoadAsync<TextAsset>(assetLocation, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var textAsset = handle.Asset;
            if (textAsset == null)
            {
                throw new InvalidOperationException(
                    $"[LanguageSwitchNode] AssetManager returned null for '{assetLocation}'.");
            }

            // 解析 JSON
            var data = ParseJson(textAsset.text);
            if (data == null || data.Count == 0)
            {
                throw new InvalidOperationException(
                    $"[LanguageSwitchNode] Parsed empty language data for '{_targetLanguage}'.");
            }

            // 注入缓存并同步切换
            LocalizationManager.SetLanguageData(_targetLanguage, data);
            LocalizationManager.SetLanguage(_targetLanguage);

            progress?.SetProgress(1f);
            progress?.SetState(LoadState.Completed);
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