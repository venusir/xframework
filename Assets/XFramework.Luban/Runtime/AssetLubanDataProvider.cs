using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// 默认数据加载后端：经 <see cref="AssetManager"/> 读取 Luban 数据文件。
    /// </summary>
    internal sealed class AssetLubanDataProvider : ILubanDataProvider
    {
        private readonly ILubanAssetSource _source;

        internal AssetLubanDataProvider()
            : this(new AssetManagerLubanAssetSource())
        {
        }

        internal AssetLubanDataProvider(ILubanAssetSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <inheritdoc />
        public async UniTask<Dictionary<string, byte[]>> LoadAsync(
            IReadOnlyList<LubanTableFile> files,
            LubanDataLoadMode loadMode,
            IProgress<LubanInitReport> progress,
            CancellationToken cancellationToken)
        {
            if (files == null)
                throw new ArgumentNullException(nameof(files));

            if (files.Count == 0)
                return new Dictionary<string, byte[]>(StringComparer.Ordinal);

            switch (loadMode)
            {
                case LubanDataLoadMode.TextAsset:
                    return await LoadTextAssetsAsync(files, progress, cancellationToken);
                case LubanDataLoadMode.RawFile:
                    return await LoadRawFilesAsync(files, progress, cancellationToken);
                default:
                    throw new ArgumentOutOfRangeException(nameof(loadMode), loadMode,
                        "[LubanConfig] Unsupported LubanDataLoadMode.");
            }
        }

        private async UniTask<Dictionary<string, byte[]>> LoadTextAssetsAsync(
            IReadOnlyList<LubanTableFile> files,
            IProgress<LubanInitReport> progress,
            CancellationToken cancellationToken)
        {
            var locations = new string[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                locations[i] = files[i].Location;
            }

            var handles = await _source.LoadTextAssetsAsync(locations, cancellationToken);
            var result = new Dictionary<string, byte[]>(files.Count, StringComparer.Ordinal);

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var handle = handles[i];
                    if (handle == null || !handle.IsValid || handle.Bytes == null)
                    {
                        throw new LubanConfigException(
                            "[LubanConfig] Failed to load table '" + files[i].TableName +
                            "' at '" + files[i].Location + "': " + DescribeError(handle?.LastError));
                    }

                    result[files[i].TableName] = handle.Bytes;
                    Report(progress, files, i);
                }
            }
            finally
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    handles[i]?.Dispose();
                }
            }

            return result;
        }

        private async UniTask<Dictionary<string, byte[]>> LoadRawFilesAsync(
            IReadOnlyList<LubanTableFile> files,
            IProgress<LubanInitReport> progress,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, byte[]>(files.Count, StringComparer.Ordinal);

            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var handle = await _source.LoadRawFileAsync(
                           files[i].Location, cancellationToken))
                {
                    if (handle == null || !handle.IsValid)
                    {
                        throw new LubanConfigException(
                            "[LubanConfig] Failed to load raw file for table '" + files[i].TableName +
                            "' at '" + files[i].Location + "': " + DescribeError(handle?.LastError));
                    }

                    var data = handle.GetData();
                    if (data == null)
                    {
                        throw new LubanConfigException(
                            "[LubanConfig] Raw file for table '" + files[i].TableName +
                            "' at '" + files[i].Location + "' returned no data.");
                    }

                    result[files[i].TableName] = data;
                }

                Report(progress, files, i);
            }

            return result;
        }

        private static void Report(
            IProgress<LubanInitReport> progress,
            IReadOnlyList<LubanTableFile> files,
            int index)
        {
            if (progress == null)
            {
                return;
            }

            float value = 0.05f + 0.7f * (index + 1) / files.Count;
            progress.Report(new LubanInitReport(value, "Loaded " + files[index].TableName));
        }

        private static string DescribeError(string error)
        {
            return string.IsNullOrEmpty(error) ? "not found or load failed" : error;
        }
    }
}
