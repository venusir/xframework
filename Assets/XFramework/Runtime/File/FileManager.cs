using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 跨平台文件管理器（静态外观）。
    /// <para>提供统一的静态 API 进行文件读写，内部根据运行时平台自动选择 <see cref="IFileProvider"/> 实现。</para>
    /// <para>支持可选的加解密层（通过 <see cref="SetCryptoProvider"/> 设置）。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>使用流程：</b></para>
    /// <list type="number">
    /// <item>（可选）调用 <see cref="Initialize"/> 使用自定义 <see cref="IFileProvider"/>；不调用则自动选择内置实现。</item>
    /// <item>（可选）调用 <see cref="SetCryptoProvider"/> 启用加解密。</item>
    /// <item>使用 <see cref="ReadAllTextAsync"/> / <see cref="WriteAllTextAsync"/> 等方法读写文件。</item>
    /// </list>
    /// <para><b>示例：</b></para>
    /// <code>
    /// // 初始化（可选，默认自动选择平台实现）
    /// FileManager.Initialize();
    ///
    /// // 读写文本文件
    /// await FileManager.WriteAllTextAsync(FileDomain.AppData, "config.json", jsonContent);
    /// string json = await FileManager.ReadAllTextAsync(FileDomain.AppData, "config.json");
    ///
    /// // 启用加密
    /// FileManager.SetCryptoProvider(new XorCryptoProvider("my-key"));
    ///
    /// // 检查文件是否存在
    /// if (FileManager.Exists(FileDomain.Streaming, "data.csv"))
    /// {
    ///     var bytes = await FileManager.ReadAllBytesAsync(FileDomain.Streaming, "data.csv");
    /// }
    /// </code>
    /// </remarks>
    public static class FileManager
    {
        #region Private Fields

        private static IFileProvider _provider;
        private static ICryptoProvider _cryptoProvider;
        private static bool _destroyed;
        private static bool _initialized;

        #endregion

        #region Lifecycle

        /// <summary>
        /// 是否已初始化。
        /// </summary>
        public static bool IsInitialized => _initialized;

        /// <summary>
        /// 初始化文件管理器。如果不传入自定义 Provider，则根据运行时平台自动选择：
        /// <list type="bullet">
        /// <item>Windows / Linux / macOS Standalone → <see cref="DesktopFileProvider"/></item>
        /// <item>iOS / Android → <see cref="MobileFileProvider"/></item>
        /// <item>其他平台（Console、WebGL 等）→ 抛出异常，需手动传入自定义 <see cref="IFileProvider"/></item>
        /// </list>
        /// </summary>
        /// <param name="provider">自定义文件提供者。为 <c>null</c> 时自动选择内置实现。</param>
        /// <exception cref="PlatformNotSupportedException">当前平台无内置实现且未提供自定义 Provider 时抛出。</exception>
        public static void Initialize(IFileProvider provider = null)
        {
            // Destroy 后允许重新初始化：与错误文案「请重新调用 Initialize」对齐，
            // 也是测试隔离（注入临时目录 Provider）的前提
            if (_destroyed)
                _destroyed = false;

            ThrowIfDestroyed();

            if (_initialized)
                return;

            if (provider != null)
            {
                _provider = provider;
            }
            else
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.WindowsEditor:
                    case RuntimePlatform.LinuxPlayer:
                    case RuntimePlatform.LinuxEditor:
                    case RuntimePlatform.OSXPlayer:
                    case RuntimePlatform.OSXEditor:
                        _provider = new DesktopFileProvider();
                        break;

                    case RuntimePlatform.IPhonePlayer:
                    case RuntimePlatform.Android:
                        _provider = new MobileFileProvider();
                        break;

                    default:
                        throw new PlatformNotSupportedException(
                            $"当前平台 '{Application.platform}' 无内置 FileProvider。" +
                            $"请为 Console/WebGL/其他平台实现 IFileProvider 并通过 FileManager.Initialize(yourProvider) 传入。");
                }
            }

            _initialized = true;
        }

        /// <summary>
        /// 销毁文件管理器，清理内部状态。
        /// <para>通常在应用退出时调用。</para>
        /// </summary>
        public static void Destroy()
        {
            _provider = null;
            _cryptoProvider = null;
            _initialized = false;
            _destroyed = true;
        }

        #endregion

        #region Crypto

        /// <summary>
        /// 设置加解密提供者。设置后所有读写操作将自动进行加解密。
        /// <para>设置为 <c>null</c> 可禁用加解密。</para>
        /// </summary>
        /// <param name="cryptoProvider">加解密提供者。为 <c>null</c> 时禁用加解密。</param>
        public static void SetCryptoProvider(ICryptoProvider cryptoProvider)
        {
            _cryptoProvider = cryptoProvider;
        }

        /// <summary>
        /// 获取当前加解密提供者。
        /// </summary>
        /// <returns>当前加解密提供者，未设置时返回 <c>null</c>。</returns>
        public static ICryptoProvider GetCryptoProvider()
        {
            return _cryptoProvider;
        }

        #endregion

        #region File Operations — Text

        /// <summary>
        /// 异步读取文件全部文本内容。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件文本内容。文件不存在时返回 <c>null</c>。</returns>
        public static UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (_cryptoProvider != null)
                return ReadTextWithCryptoAsync(domain, relativePath, cancellationToken);

            return _provider.ReadAllTextAsync(domain, relativePath, cancellationToken);
        }

        /// <summary>
        /// 异步写入文本内容到文件。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="content">要写入的文本内容。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static async UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (_cryptoProvider != null)
            {
                var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
                bytes = _cryptoProvider.Encrypt(bytes);
                await _provider.WriteAllBytesAsync(domain, relativePath, bytes, cancellationToken);
            }
            else
            {
                await _provider.WriteAllTextAsync(domain, relativePath, content, cancellationToken);
            }
        }

        #endregion

        #region File Operations — Bytes

        /// <summary>
        /// 异步读取文件全部字节内容。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>文件字节数组。文件不存在时返回 <c>null</c>。</returns>
        public static async UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var bytes = await _provider.ReadAllBytesAsync(domain, relativePath, cancellationToken);

            if (bytes != null && _cryptoProvider != null)
            {
                bytes = _cryptoProvider.Decrypt(bytes);
            }

            return bytes;
        }

        /// <summary>
        /// 异步写入字节内容到文件。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <param name="data">要写入的字节数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static async UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var bytes = data;
            if (_cryptoProvider != null)
            {
                bytes = _cryptoProvider.Encrypt(bytes);
            }

            await _provider.WriteAllBytesAsync(domain, relativePath, bytes, cancellationToken);
        }

        #endregion

        #region File Operations — Exists / Delete

        /// <summary>
        /// 检查指定文件是否存在。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        /// <returns>文件存在返回 <c>true</c>。</returns>
        public static bool Exists(FileDomain domain, string relativePath)
        {
            EnsureInitialized();
            return _provider.Exists(domain, relativePath);
        }

        /// <summary>
        /// 删除指定文件。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的文件路径。</param>
        public static void Delete(FileDomain domain, string relativePath)
        {
            EnsureInitialized();
            _provider.Delete(domain, relativePath);
        }

        #endregion

        #region Directory Operations

        /// <summary>
        /// 异步获取目录下所有文件路径。
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的目录路径。</param>
        /// <param name="searchPattern">搜索模式，默认为 <c>*</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>匹配的文件相对路径数组。</returns>
        public static UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _provider.GetFilesAsync(domain, relativePath, searchPattern, cancellationToken);
        }

        /// <summary>
        /// 创建目录（包括所有父目录）。
        /// <para>如果目录已存在，不执行任何操作。</para>
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的目录路径。</param>
        public static void CreateDirectory(FileDomain domain, string relativePath)
        {
            EnsureInitialized();
            _provider.CreateDirectory(domain, relativePath);
        }

        #endregion

        #region Path Utilities

        /// <summary>
        /// 将路径域 + 相对路径转换为物理绝对路径。
        /// <para>此方法供内部或需要绕过 FileManager 的底层操作使用。</para>
        /// </summary>
        /// <param name="domain">路径域。</param>
        /// <param name="relativePath">相对于域根目录的相对路径。为 <c>null</c> 时仅返回域根目录。</param>
        /// <returns>物理绝对路径。</returns>
        public static string GetPhysicalPath(FileDomain domain, string relativePath)
        {
            EnsureInitialized();
            return _provider.GetPhysicalPath(domain, relativePath);
        }

        #endregion

        #region Internal

        /// <summary>
        /// 确保已初始化，否则自动使用默认提供者。
        /// </summary>
        private static void EnsureInitialized()
        {
            ThrowIfDestroyed();

            if (!_initialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// 在已销毁状态下调用任何方法均抛出异常。
        /// </summary>
        private static void ThrowIfDestroyed()
        {
            if (_destroyed)
                throw new ObjectDisposedException(nameof(FileManager),
                    "FileManager 已被销毁，请重新调用 Initialize。");
        }

        /// <summary>
        /// 带加解密层的文本读取（内部实现）。
        /// </summary>
        private static async UniTask<string> ReadTextWithCryptoAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken)
        {
            var bytes = await _provider.ReadAllBytesAsync(domain, relativePath, cancellationToken);
            if (bytes == null)
                return null;

            bytes = _cryptoProvider.Decrypt(bytes);
            return Encoding.UTF8.GetString(bytes);
        }

        #endregion
    }
}