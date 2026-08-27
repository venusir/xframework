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
    /// <para>实现 <see cref="IAtomicFileProvider"/>：原子写为「写 .tmp 临时文件 → 替换正式文件」，
    /// 写入中途崩溃不会损坏已有文件。</para>
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

            return Path.Combine(root, FilePathUtility.NormalizeRelativePath(relativePath));
        }

        #endregion

        #region IAtomicFileProvider

        /// <inheritdoc />
        public async UniTask WriteAllBytesAtomicAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            var tempPath = relativePath + ".tmp";
            var srcPhysical = GetPhysicalPath(domain, tempPath);
            var dstPhysical = GetPhysicalPath(domain, relativePath);

            // 先写 .tmp 临时文件：写入失败时正式文件保持完整
            await WriteAllBytesAsync(domain, tempPath, data, cancellationToken);

            // 替换流程为同步 IO，移出主线程；Unity API 面无 File.Move(overwrite) 重载，
            // 以「删正式 → Move」实现，语义与 Save 双缓冲一致
            await UniTask.RunOnThreadPool(
                () =>
                {
                    if (File.Exists(dstPhysical))
                        File.Delete(dstPhysical);
                    File.Move(srcPhysical, dstPhysical);
                },
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