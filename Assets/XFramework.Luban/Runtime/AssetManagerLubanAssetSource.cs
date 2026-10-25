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
            var slices = new LubanTextSlice[handles.Length];
            try
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    var handle = handles[i];
                    slices[i] = new LubanTextSlice(
                        handle.IsValid,
                        handle.IsValid ? handle.Asset : null,
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

            return MapTextAssets(slices);
        }

        /// <inheritdoc />
        public async UniTask<ILubanRawFileHandle> LoadRawFileAsync(
            string location,
            CancellationToken cancellationToken)
        {
            EnsureInitialized();
            using (var handle = await AssetManager.LoadRawFileAsync(location, cancellationToken))
            {
                var slice = new LubanRawSlice(
                    handle.IsValid,
                    handle.IsValid ? handle.GetRawFileData() : null,
                    handle.LastError);
                return MapRawFile(slice);
            }
        }

        /// <summary>
        /// 纯映射：把 TextAsset 切片映射成 <see cref="ILubanTextAssetHandle"/>（R4-LBN-002 测试缝）。
        /// <para>真实 <see cref="AssetHandle{T}"/> 只能由 YooAsset 句柄构造，测试进程造不出「有效句柄 +
        /// 成功字节」；把映射逻辑抽到这里后，成功/无效/Asset=null 三态都能直接覆盖。句柄的获取与释放
        /// 仍由真实 YooAsset 探针背书。</para>
        /// </summary>
        internal static ILubanTextAssetHandle[] MapTextAssets(IReadOnlyList<LubanTextSlice> slices)
        {
            var results = new ILubanTextAssetHandle[slices.Count];
            for (int i = 0; i < slices.Count; i++)
            {
                var slice = slices[i];
                results[i] = new TextAssetResult(
                    slice.IsValid,
                    slice.IsValid && slice.Asset != null ? slice.Asset.bytes : null,
                    slice.LastError);
            }
            return results;
        }

        /// <summary>纯映射：RawFile 切片 → <see cref="ILubanRawFileHandle"/>（R4-LBN-002 测试缝）。</summary>
        internal static ILubanRawFileHandle MapRawFile(LubanRawSlice slice)
        {
            return new RawFileResult(
                slice.IsValid,
                slice.IsValid ? slice.Data : null,
                slice.LastError);
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

        /// <summary>TextAsset 句柄的纯映射输入（R4-LBN-002 测试缝）。</summary>
        internal readonly struct LubanTextSlice
        {
            internal readonly bool IsValid;
            internal readonly TextAsset Asset;
            internal readonly string LastError;

            internal LubanTextSlice(bool isValid, TextAsset asset, string lastError)
            {
                IsValid = isValid;
                Asset = asset;
                LastError = lastError;
            }
        }

        /// <summary>RawFile 句柄的纯映射输入（R4-LBN-002 测试缝）。</summary>
        internal readonly struct LubanRawSlice
        {
            internal readonly bool IsValid;
            internal readonly byte[] Data;
            internal readonly string LastError;

            internal LubanRawSlice(bool isValid, byte[] data, string lastError)
            {
                IsValid = isValid;
                Data = data;
                LastError = lastError;
            }
        }
    }
}
