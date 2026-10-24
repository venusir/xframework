using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>可编程的 TextAsset 句柄替身。</summary>
    internal sealed class FakeTextAssetHandle : ILubanTextAssetHandle
    {
        public bool IsValid { get; set; } = true;

        public byte[] Bytes { get; set; }

        public string LastError { get; set; } = string.Empty;

        public int DisposeCalls { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
        }
    }

    /// <summary>可编程的 RawFile 句柄替身。</summary>
    internal sealed class FakeRawFileHandle : ILubanRawFileHandle
    {
        public bool IsValid { get; set; } = true;

        public byte[] Data { get; set; }

        public string LastError { get; set; } = string.Empty;

        public int DisposeCalls { get; private set; }

        public byte[] GetData() => Data;

        public void Dispose()
        {
            DisposeCalls++;
        }
    }

    /// <summary>可编程的加载后端替身。</summary>
    internal sealed class FakeLubanAssetSource : ILubanAssetSource
    {
        public ILubanTextAssetHandle[] TextHandles { get; set; } =
            Array.Empty<ILubanTextAssetHandle>();

        public ILubanRawFileHandle RawHandle { get; set; }

        public int LoadTextCalls { get; private set; }

        public int LoadRawCalls { get; private set; }

        public UniTask<ILubanTextAssetHandle[]> LoadTextAssetsAsync(
            IReadOnlyList<string> locations,
            CancellationToken cancellationToken)
        {
            LoadTextCalls++;
            return UniTask.FromResult(TextHandles);
        }

        public UniTask<ILubanRawFileHandle> LoadRawFileAsync(
            string location,
            CancellationToken cancellationToken)
        {
            LoadRawCalls++;
            return UniTask.FromResult(RawHandle);
        }
    }
}
