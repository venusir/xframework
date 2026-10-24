using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XAsset;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// 默认加载后端：把 <see cref="AssetManager"/> 的 TextAsset / RawFile 句柄读成字节后
    /// 立即释放，包成 <see cref="ILubanAssetSource"/> 的最小面。
    /// <para>不在字段/参数里显式命名核心句柄类型——它们的私有 YooAsset 字段会让本程序集
    /// 额外要求 YooAsset 引用；用 <c>var</c> + 立即读取保持零额外依赖。</para>
    /// </summary>
    internal sealed class AssetManagerLubanAssetSource : ILubanAssetSource
    {
        /// <inheritdoc />
        public async UniTask<ILubanTextAssetHandle[]> LoadTextAssetsAsync(
            IReadOnlyList<string> locations,
            CancellationToken cancellationToken)
        {
            EnsureInitialized();
            var handles = await AssetManager.LoadAllAsync<TextAsset>(locations, cancellationToken);
            var results = new ILubanTextAssetHandle[handles.Length];
            try
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    var handle = handles[i];
                    results[i] = new TextAssetResult(
                        handle.IsValid,
                        handle.IsValid && handle.Asset != null ? handle.Asset.bytes : null,
                        handle.LastError);
                }
            }
            finally
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    if (handles[i].IsValid)
                    {
                        handles[i].Dispose();
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async UniTask<ILubanRawFileHandle> LoadRawFileAsync(
            string location,
            CancellationToken cancellationToken)
        {
            EnsureInitialized();
            using (var handle = await AssetManager.LoadRawFileAsync(location, cancellationToken))
            {
                return new RawFileResult(
                    handle.IsValid,
                    handle.IsValid ? handle.GetRawFileData() : null,
                    handle.LastError);
            }
        }

        private static void EnsureInitialized()
        {
            if (!AssetManager.IsInitialized)
            {
                throw new InvalidOperationException(
                    "[LubanConfig] AssetManager 尚未初始化。请先调用 AssetManager.InitializeAsync() " +
                    "（或登记 AssetBootstrapStage）后再初始化 Luban 配置。");
            }
        }

        private sealed class TextAssetResult : ILubanTextAssetHandle
        {
            private readonly bool _isValid;
            private readonly byte[] _bytes;
            private readonly string _lastError;

            internal TextAssetResult(bool isValid, byte[] bytes, string lastError)
            {
                _isValid = isValid;
                _bytes = bytes;
                _lastError = lastError;
            }

            public bool IsValid => _isValid;

            public byte[] Bytes => _bytes;

            public string LastError => _lastError;

            public void Dispose()
            {
            }
        }

        private sealed class RawFileResult : ILubanRawFileHandle
        {
            private readonly bool _isValid;
            private readonly byte[] _data;
            private readonly string _lastError;

            internal RawFileResult(bool isValid, byte[] data, string lastError)
            {
                _isValid = isValid;
                _data = data;
                _lastError = lastError;
            }

            public bool IsValid => _isValid;

            public byte[] GetData() => _data;

            public string LastError => _lastError;

            public void Dispose()
            {
            }
        }
    }
}
