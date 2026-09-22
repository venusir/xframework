using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XFileManager.Tests
{
    /// <summary>
    /// 只实现 <see cref="IFileProvider"/> 的测试替身：把全部调用转给内层 Provider，但**不实现**
    /// <see cref="IAtomicFileProvider"/> 与 <see cref="IDirectoryProvider"/>。
    /// <para>用途：验证门面对「可选能力缺失」的处置——原子写降级为普通写、目录枚举返回空数组，
    /// 且两者都告警。启用加解密时调用路径会经过 <see cref="CryptoFileProvider"/> 装饰器，
    /// 故它同时是「装饰器不得改变能力语义」这条不变量的回归锁。</para>
    /// </summary>
    internal sealed class NonAtomicFileProvider : IFileProvider
    {
        private readonly IFileProvider _inner;

        public NonAtomicFileProvider(IFileProvider inner)
        {
            _inner = inner;
        }

        /// <inheritdoc/>
        public bool Exists(FileDomain domain, string relativePath) => _inner.Exists(domain, relativePath);

        /// <inheritdoc/>
        public UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default) =>
            _inner.ExistsAsync(domain, relativePath, cancellationToken);

        /// <inheritdoc/>
        public UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default) =>
            _inner.ReadAllTextAsync(domain, relativePath, cancellationToken);

        /// <inheritdoc/>
        public UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default) =>
            _inner.ReadAllBytesAsync(domain, relativePath, cancellationToken);

        /// <inheritdoc/>
        public UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default) =>
            _inner.WriteAllTextAsync(domain, relativePath, content, cancellationToken);

        /// <inheritdoc/>
        public UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default) =>
            _inner.WriteAllBytesAsync(domain, relativePath, data, cancellationToken);

        /// <inheritdoc/>
        public void Delete(FileDomain domain, string relativePath) => _inner.Delete(domain, relativePath);

        /// <inheritdoc/>
        public UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default) =>
            _inner.GetFilesAsync(domain, relativePath, searchPattern, cancellationToken);

        /// <inheritdoc/>
        public void CreateDirectory(FileDomain domain, string relativePath) => _inner.CreateDirectory(domain, relativePath);

        /// <inheritdoc/>
        public string GetPhysicalPath(FileDomain domain, string relativePath) => _inner.GetPhysicalPath(domain, relativePath);
    }
}
