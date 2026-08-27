using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 控制台平台（Xbox/PS5/Switch）文件提供者抽象基类。
    /// <para>由于各 Console SDK 受 NDA 保护，此基类仅提供接口桩和基础路径映射。</para>
    /// <para>第三方接入方需继承此类并实现平台特定的文件读写 API。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>接入步骤：</b></para>
    /// <list type="number">
    /// <item>继承 <see cref="ConsoleFileProvider"/>。</item>
    /// <item>重写 <see cref="ReadAllBytesAsync"/>、<see cref="WriteAllBytesAsync"/> 等核心 IO 方法。</item>
    /// <item>根据平台 SDK 将 <see cref="FileDomain"/> 映射到对应存储路径（如 Xbox 的 Connected Storage、PS5 的 SaveData API）。</item>
    /// <item>在应用启动时调用 <c>FileManager.Initialize(new MyConsoleFileProvider());</c>。</item>
    /// </list>
    /// <para><b>参考映射：</b></para>
    /// <list type="bullet">
    /// <item>Xbox GDK：<see cref="FileDomain.SaveData"/> → XGameSave / Connected Storage</item>
    /// <item>PS5：<see cref="FileDomain.SaveData"/> → SCE SaveData API</item>
    /// <item>Switch：<see cref="FileDomain.SaveData"/> → nn::fs 托管目录</item>
    /// </list>
    /// </remarks>
    public abstract class ConsoleFileProvider : IFileProvider
    {
        #region IFileProvider — 需第三方重写的核心方法

        /// <inheritdoc />
        public abstract byte[] ReadAllBytes(FileDomain domain, string relativePath);

        /// <inheritdoc />
        public abstract void WriteAllBytes(FileDomain domain, string relativePath, byte[] data);

        /// <inheritdoc />
        public abstract bool Exists(FileDomain domain, string relativePath);

        /// <inheritdoc />
        public abstract void Delete(FileDomain domain, string relativePath);

        /// <inheritdoc />
        public abstract string GetPhysicalPath(FileDomain domain, string relativePath);

        #endregion

        #region IFileProvider — 基于 ReadAllBytes/WriteAllBytes 的默认实现

        /// <inheritdoc />
        public virtual UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            return UniTask.RunOnThreadPool(
                () => Exists(domain, relativePath),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public virtual async UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            var bytes = await ReadAllBytesAsync(domain, relativePath, cancellationToken);
            if (bytes == null)
                return null;
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        /// <inheritdoc />
        public virtual async UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            return await UniTask.RunOnThreadPool(
                () => ReadAllBytes(domain, relativePath),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public virtual async UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content ?? string.Empty);
            await WriteAllBytesAsync(domain, relativePath, bytes, cancellationToken);
        }

        /// <inheritdoc />
        public virtual async UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            await UniTask.RunOnThreadPool(
                () => WriteAllBytes(domain, relativePath, data),
                configureAwait: false,
                cancellationToken);
        }

        /// <inheritdoc />
        public virtual UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default)
        {
            // Console 平台枚举文件需要平台 SDK 支持，默认返回空
            return UniTask.FromResult(Array.Empty<string>());
        }

        /// <inheritdoc />
        public virtual void CreateDirectory(FileDomain domain, string relativePath)
        {
            // 目录创建由平台 SDK 内部处理，默认空实现
        }

        #endregion
    }
}