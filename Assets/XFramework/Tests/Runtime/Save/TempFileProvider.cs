using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XFileManager;

namespace XFramework.XSave.Tests
{
    /// <summary>
    /// 将全部 <see cref="FileDomain"/> 映射到唯一临时目录的 <see cref="IFileProvider"/> 测试替身。
    /// <para>必须使用真实文件系统目录：<see cref="SaveManagerImpl"/> 的双缓冲写入依赖
    /// <see cref="System.IO.File.Move"/> 物理路径操作，内存替身无法覆盖该路径。</para>
    /// <para>每次实例化创建独立目录（<c>Path.GetTempPath()/XFrameworkSaveTests/Guid</c>），
    /// 测试结束后经 <see cref="Cleanup"/> 递归删除。</para>
    /// </summary>
    internal sealed class TempFileProvider : IFileProvider
    {
        private readonly string _rootPath;

        public TempFileProvider()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), "XFrameworkSaveTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
        }

        /// <summary>递归删除整个临时根目录。</summary>
        public void Cleanup()
        {
            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, true);
        }

        #region IFileProvider

        /// <inheritdoc/>
        public bool Exists(FileDomain domain, string relativePath)
        {
            return File.Exists(GetPhysicalPath(domain, relativePath));
        }

        /// <inheritdoc/>
        public UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            return UniTask.RunOnThreadPool(
                () => File.Exists(fullPath),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public async UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            EnsureDirectoryExists(fullPath);

            await UniTask.RunOnThreadPool(
                () => File.WriteAllText(fullPath, content ?? string.Empty),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc/>
        public async UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            EnsureDirectoryExists(fullPath);

            await UniTask.RunOnThreadPool(
                () => File.WriteAllBytes(fullPath, data ?? Array.Empty<byte>()),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc/>
        public void Delete(FileDomain domain, string relativePath)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        /// <inheritdoc/>
        public async UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);

            return await UniTask.RunOnThreadPool(
                () =>
                {
                    if (!Directory.Exists(fullPath))
                        return Array.Empty<string>();

                    var files = Directory.GetFiles(fullPath, searchPattern);
                    for (int i = 0; i < files.Length; i++)
                        files[i] = FilePathUtility.ToRelativePath(_rootPath, files[i]);

                    return files;
                },
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc/>
        public void CreateDirectory(FileDomain domain, string relativePath)
        {
            var fullPath = GetPhysicalPath(domain, relativePath);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }

        /// <inheritdoc/>
        public string GetPhysicalPath(FileDomain domain, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return _rootPath;

            return Path.Combine(_rootPath, FilePathUtility.NormalizeRelativePath(relativePath));
        }

        #endregion

        #region Private Methods

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
