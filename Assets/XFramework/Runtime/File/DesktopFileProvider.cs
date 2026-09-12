using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 桌面平台（Windows/Linux/macOS Standalone）文件提供者实现。
    /// <para>直接使用 <see cref="System.IO"/> API，性能最优。</para>
    /// <para>实现 <see cref="IAtomicFileProvider"/>：原子写为「写 .tmp 临时文件 → 一步替换正式文件并保留
    /// 一代 .bak 备份」，写入中途崩溃不会损坏已有文件，也不会出现「零副本」窗口。</para>
    /// </summary>
    public class DesktopFileProvider : IFileProvider, IAtomicFileProvider
    {
        #region IFileProvider

        /// <inheritdoc />
        public bool Exists(FileDomain domain, string relativePath)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            return File.Exists(fullPath);
        }

        /// <inheritdoc />
        public UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            return UniTask.RunOnThreadPool(
                () => File.Exists(fullPath),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (!File.Exists(fullPath))
                return null;

            return await UniTask.RunOnThreadPool(
                () => File.ReadAllText(fullPath),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (!File.Exists(fullPath))
                return null;

            return await UniTask.RunOnThreadPool(
                () => File.ReadAllBytes(fullPath),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            EnsureDirectoryExists(fullPath);

            await UniTask.RunOnThreadPool(
                () => File.WriteAllText(fullPath, content ?? string.Empty),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public async UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            EnsureDirectoryExists(fullPath);

            await UniTask.RunOnThreadPool(
                () => File.WriteAllBytes(fullPath, data ?? Array.Empty<byte>()),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public void Delete(FileDomain domain, string relativePath)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        /// <inheritdoc />
        public async UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);

            return await UniTask.RunOnThreadPool(
                () =>
                {
                    if (!Directory.Exists(fullPath))
                        return Array.Empty<string>();

                    var files = Directory.GetFiles(fullPath, searchPattern);
                    string rootDir = GetPhysicalPath(domain, null);

                    // 转换为相对路径（统一正斜杠分隔）
                    for (int i = 0; i < files.Length; i++)
                    {
                        files[i] = FilePathUtility.ToRelativePath(rootDir, files[i]);
                    }

                    return files;
                },
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public void CreateDirectory(FileDomain domain, string relativePath)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }

        /// <inheritdoc />
        public string GetPhysicalPath(FileDomain domain, string relativePath)
        {
            var root = GetDomainRoot(domain);
            if (string.IsNullOrEmpty(relativePath))
                return root;

            // 路径沙箱收敛点：所有域的相对路径在此统一校验，拒绝 .. 穿越、盘符与 UNC，
            // 防止第三方或业务层注入非法路径读写到域根之外（门面不接触物理路径，必须在此拦截）
            if (!FilePathUtility.TryNormalizeRelativePath(relativePath, out var normalized))
                throw new ArgumentException(
                    $"[FileManager] 非法相对路径 '{relativePath}':不允许盘符、UNC 或 '..' 段穿越。",
                    nameof(relativePath));

            return Path.Combine(root, normalized);
        }

        #endregion

        #region IAtomicFileProvider

        /// <inheritdoc />
        public async UniTask WriteAllBytesAtomicAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            var tempPath = relativePath + FilePathUtility.TempFileSuffix;
            var backupPath = relativePath + FilePathUtility.BackupFileSuffix;
            var srcPhysical = GetPhysicalPath(domain, tempPath);
            var dstPhysical = GetPhysicalPath(domain, relativePath);
            var bakPhysical = GetPhysicalPath(domain, backupPath);

            // 先写 .tmp 临时文件：写入失败时正式文件保持完整
            await WriteAllBytesAsync(domain, tempPath, data, cancellationToken);

            // 替换流程为同步 IO，移出主线程。不再用「删正式 → Move」(Unity API 面无 File.Move(overwrite)
            // 重载)，改为一步替换并保留一代备份——原写法在删除与重命名之间崩溃会让存档彻底消失，
            // 详见 FilePathUtility.ReplaceFileAtomically 的不变式说明
            await UniTask.RunOnThreadPool(
                () => FilePathUtility.ReplaceFileAtomically(srcPhysical, dstPhysical, bakPhysical),
                configureAwait: false,
                cancellationToken);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 获取域对应的物理根目录。
        /// </summary>
        private static string GetDomainRoot(FileDomain domain)
        {
            switch (domain)
            {
                case FileDomain.AppData:
                    return Application.persistentDataPath;
                case FileDomain.Streaming:
                    return Application.streamingAssetsPath;
                case FileDomain.Cache:
                    return Application.temporaryCachePath;
                case FileDomain.SaveData:
                    // 桌面平台 SaveData 等同于 AppData
                    return Application.persistentDataPath;
                default:
                    throw new ArgumentOutOfRangeException(nameof(domain), domain, null);
            }
        }

        /// <summary>
        /// 确保文件所在目录存在。
        /// </summary>
        private static void EnsureDirectoryExists(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        #endregion
    }
}