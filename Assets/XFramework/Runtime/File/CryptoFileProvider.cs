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
    /// <para>可限定作用域（<see cref="FileDomain"/>）：限定时只有目标域经加解密、其余域原样透传，
    /// 避免「为存档开加密却顺带把 AppData / Cache 一起加密了」这类副作用。</para>
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
        private readonly FileDomain? _targetDomain;

        #endregion

        #region Constructors

        /// <summary>
        /// 构造加解密装饰器。
        /// </summary>
        /// <param name="inner">被包裹的底层 Provider。</param>
        /// <param name="crypto">加解密实现。</param>
        /// <param name="targetDomain">限定只对该域加解密；为 <c>null</c> 时对所有域生效。</param>
        public CryptoFileProvider(IFileProvider inner, ICryptoProvider crypto, FileDomain? targetDomain = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
            _targetDomain = targetDomain;
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

        #region Private Methods

        /// <summary>
        /// 判断指定域是否需要加解密。
        /// <para>未限定域时对所有域生效；限定时只有目标域走加解密，其余域原样透传。</para>
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <returns>需要加解密返回 <c>true</c>。</returns>
        private bool ShouldCrypt(FileDomain domain)
        {
            return _targetDomain == null || _targetDomain.Value == domain;
        }

        #endregion

        #region IFileProvider — 读写经加解密层

        /// <inheritdoc />
        public async UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var bytes = await _inner.ReadAllBytesAsync(domain, relativePath, cancellationToken);
            if (bytes == null)
                return null;

            return Encoding.UTF8.GetString(ShouldCrypt(domain) ? _crypto.Decrypt(bytes) : bytes);
        }

        /// <inheritdoc />
        public async UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var bytes = await _inner.ReadAllBytesAsync(domain, relativePath, cancellationToken);
            if (bytes == null)
                return null;

            return ShouldCrypt(domain) ? _crypto.Decrypt(bytes) : bytes;
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
            return _inner.WriteAllBytesAsync(domain, relativePath, ShouldCrypt(domain) ? _crypto.Encrypt(data) : data, cancellationToken);
        }

        #endregion

        #region IAtomicFileProvider

        /// <inheritdoc />
        public async UniTask WriteAllBytesAtomicAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            if (!(_inner is IAtomicFileProvider atomicInner))
                throw new NotSupportedException("[FileManager] 底层 Provider 不支持原子写入。");

            var payload = ShouldCrypt(domain) ? _crypto.Encrypt(data) : data;
            await atomicInner.WriteAllBytesAtomicAsync(domain, relativePath, payload, cancellationToken);
        }

        #endregion
    }
}
