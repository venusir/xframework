using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// 默认加载后端的最小接缝：把 AssetManager 的句柄形状收敛为两个可替换的接口。
    /// <para>仅 internal——生产只是 AssetManager 的薄包装，测试用它覆盖成功/失败映射。</para>
    /// </summary>
    internal interface ILubanAssetSource
    {
        /// <summary>批量加载 TextAsset；返回顺序与 <paramref name="locations"/> 一致。</summary>
        UniTask<ILubanTextAssetHandle[]> LoadTextAssetsAsync(
            IReadOnlyList<string> locations,
            CancellationToken cancellationToken);

        /// <summary>加载单个 RawFile。</summary>
        UniTask<ILubanRawFileHandle> LoadRawFileAsync(
            string location,
            CancellationToken cancellationToken);
    }

    /// <summary>TextAsset 句柄的最小面。</summary>
    internal interface ILubanTextAssetHandle : IDisposable
    {
        bool IsValid { get; }

        byte[] Bytes { get; }

        string LastError { get; }
    }

    /// <summary>RawFile 句柄的最小面。</summary>
    internal interface ILubanRawFileHandle : IDisposable
    {
        bool IsValid { get; }

        byte[] GetData();

        string LastError { get; }
    }
}
