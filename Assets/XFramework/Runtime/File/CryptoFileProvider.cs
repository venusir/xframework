using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 加解密装饰器：在 <see cref="IFileProvider"/> 之上叠加 <see cref="ICryptoProvider"/> 加解密层。
    /// <para>组合而非在门面内做 if 分支：加解密是包裹层，读写方法在字节边界统一加解密，
    /// 新增 Provider 方法无需在门面重复加密分支（参考 LocalPrefs CryptoFileAccessor 装饰器设计）。</para>
    /// <para>同时实现 <see cref="IAtomicFileProvider"/>：原子写同样经过加密层，加密后的密文整体原子替换。</para>
    /// <para>同时实现 <see cref="IDirectoryProvider"/>：目录名不含数据，无需加解密，能力取决于被包裹的 Provider。
    /// 装饰器必须显式实现可选能力接口，否则会把底层 Provider 的能力遮蔽掉——
    /// 门面按 <c>is</c> 探测能力时看到的是装饰器本身。</para>
    /// </summary>
    internal sealed class CryptoFileProvider : IFileProvider, IAtomicFileProvider, IDirectoryProvider
    {
        #region Private Fields

        private readonly IFileProvider _inner;
        private readonly ICryptoProvider _crypto;

        #endregion

        #region Constructors

        public CryptoFileProvider(IFileProvider inner, ICryptoProvider crypto)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        }

        #endregion

        #region IFileProvider — 无加密层透传

        /// <inheritdoc />
        public bool Exists(FileDomain domain, string relativePath)
        {
            return _inner.Exists(domain, relativePath);
        }

        /// <inheritdoc />
        public UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            return _inner.ExistsAsync(domain, relativePath, cancellationToken);
        }

        /// <inheritdoc />
        public void Delete(FileDomain domain, string relativePath)
        {
            _inner.Delete(domain, relativePath);
        }

        /// <inheritdoc />
        public UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default)
        {
            return _inner.GetFilesAsync(domain, relativePath, searchPattern, cancellationToken);
        }

        /// <inheritdoc />
        public void CreateDirectory(FileDomain domain, string relativePath)
        {
            _inner.CreateDirectory(domain, relativePath);
        }

        /// <inheritdoc />
        public UniTask<string[]> GetDirectoriesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            // 能力随被包裹的 Provider：底层不支持枚举时返回空数组（与 ConsoleFileProvider
            // 对 GetFilesAsync 的既有处理一致，不抛异常）
            if (_inner is IDirectoryProvider directoryProvider)
                return directoryProvider.GetDirectoriesAsync(domain, relativePath, cancellationToken);

            return UniTask.FromResult(Array.Empty<string>());
        }

        /// <inheritdoc />
        public string GetPhysicalPath(FileDomain domain, string relativePath)
        {
            return _inner.GetPhysicalPath(domain, relativePath);
        }

        #endregion

        #region IFileProvider — 读写经加解密层

        /// <inheritdoc />
        public async UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var bytes = await _inner.ReadAllBytesAsync(domain, relativePath, cancellationToken);
            if (bytes == null)
                return null;

            return Encoding.UTF8.GetString(_crypto.Decrypt(bytes));
        }

        /// <inheritdoc />
        public async UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var bytes = await _inner.ReadAllBytesAsync(domain, relativePath, cancellationToken);
            if (bytes == null)
                return null;

            return _crypto.Decrypt(bytes);
        }

        /// <inheritdoc />
        public UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default)
        {
            // 统一走字节写入路径，加密只发生在字节边界
            var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
            return WriteAllBytesAsync(domain, relativePath, bytes, cancellationToken);
        }

        /// <inheritdoc />
        public UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            return _inner.WriteAllBytesAsync(domain, relativePath, _crypto.Encrypt(data), cancellationToken);
        }

        #endregion

        #region IAtomicFileProvider

        /// <inheritdoc />
        public async UniTask WriteAllBytesAtomicAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            if (!(_inner is IAtomicFileProvider atomicInner))
                throw new NotSupportedException("[FileManager] 底层 Provider 不支持原子写入。");

            await atomicInner.WriteAllBytesAtomicAsync(domain, relativePath, _crypto.Encrypt(data), cancellationToken);
        }

        #endregion
    }
}
