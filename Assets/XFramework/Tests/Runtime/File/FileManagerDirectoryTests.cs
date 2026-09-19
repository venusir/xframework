using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSave.Tests;

namespace XFramework.XFileManager.Tests
{
    /// <summary>
    /// <see cref="FileManager.GetDirectoriesAsync"/> 目录枚举契约测试。
    /// <para>覆盖:直接子目录枚举(非递归)、目录不存在返回空、加解密装饰器不遮蔽该能力、
    /// 底层 Provider 无该能力时降级为空数组并告警。</para>
    /// </summary>
    [TestFixture]
    public class FileManagerDirectoryTests
    {
        private TempFileProvider _fileProvider;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _fileProvider?.Cleanup();
        }

        [SetUp]
        public void SetUp()
        {
            _fileProvider?.Cleanup();
            _fileProvider = new TempFileProvider();

            FileManager.Destroy();
            FileManager.Initialize(_fileProvider);
        }

        [TearDown]
        public void TearDown()
        {
            FileManager.SetCryptoProvider(null);
            FileManager.Destroy();

            // 还原成「零配置可用」，而不是把销毁闩锁留给后续 fixture——懒加载豁免只对「未初始化」生效，
            // 已销毁时 EnsureInitialized 直接抛，同进程混跑两个平台会让后面的 EditMode 用例吃到它
            FileManager.Initialize();
        }

        [Test]
        public async Task GetDirectoriesAsync_ReturnsDirectSubdirectories_NonRecursive()
        {
            FileManager.CreateDirectory(FileDomain.SaveData, "Alice");
            FileManager.CreateDirectory(FileDomain.SaveData, "Bob");
            FileManager.CreateDirectory(FileDomain.SaveData, "Alice/Nested");
            await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "root.save", Encoding.UTF8.GetBytes("x"));

            var dirs = await FileManager.GetDirectoriesAsync(FileDomain.SaveData, "");

            Assert.AreEqual(2, dirs.Length, "应只返回直接子目录，且不包含文件");
            CollectionAssert.Contains(dirs, "Alice");
            CollectionAssert.Contains(dirs, "Bob");
            CollectionAssert.DoesNotContain(dirs, "Alice/Nested", "枚举应为非递归，不返回孙目录");
            CollectionAssert.DoesNotContain(dirs, "root.save", "目录枚举不应返回文件");
        }

        [Test]
        public async Task GetDirectoriesAsync_MissingDirectory_ReturnsEmpty()
        {
            var dirs = await FileManager.GetDirectoriesAsync(FileDomain.SaveData, "NotCreatedYet");

            Assert.IsNotNull(dirs, "目录不存在时应返回空数组而非 null");
            Assert.AreEqual(0, dirs.Length, "目录不存在时应返回空数组");
        }

        [Test]
        public async Task GetDirectoriesAsync_CryptoEnabled_StillEnumerates()
        {
            // 加解密装饰器会包裹底层 Provider，若它不实现 IDirectoryProvider，
            // 门面的能力探测就会看到装饰器本身而误判为「不支持」——本测试锁定该遮蔽问题
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"));
            FileManager.CreateDirectory(FileDomain.SaveData, "Alice");

            var dirs = await FileManager.GetDirectoriesAsync(FileDomain.SaveData, "");

            CollectionAssert.Contains(dirs, "Alice", "开启加密后目录枚举仍应可用");
        }

        [Test]
        public async Task GetDirectoriesAsync_ProviderUnsupported_WarnsAndReturnsEmpty()
        {
            // 切换为无目录枚举能力的哑 Provider(仅实现 IFileProvider)
            var nonEnumerable = new NonEnumerableFileProvider(_fileProvider);
            FileManager.Destroy();
            FileManager.Initialize(nonEnumerable);
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("不支持目录枚举"));

                var dirs = await FileManager.GetDirectoriesAsync(FileDomain.SaveData, "");

                Assert.IsNotNull(dirs, "能力缺失时应返回空数组而非 null");
                Assert.AreEqual(0, dirs.Length, "能力缺失时应返回空数组");
            }
            finally
            {
                FileManager.Destroy();
            }
        }

        /// <summary>
        /// 仅实现 <see cref="IFileProvider"/> 的哑 Provider：用于验证门面的目录枚举降级路径。
        /// </summary>
        private sealed class NonEnumerableFileProvider : IFileProvider
        {
            private readonly IFileProvider _inner;

            public NonEnumerableFileProvider(IFileProvider inner)
            {
                _inner = inner;
            }

            public bool Exists(FileDomain domain, string relativePath) => _inner.Exists(domain, relativePath);

            public UniTask<bool> ExistsAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default) =>
                _inner.ExistsAsync(domain, relativePath, cancellationToken);

            public UniTask<string> ReadAllTextAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default) =>
                _inner.ReadAllTextAsync(domain, relativePath, cancellationToken);

            public UniTask<byte[]> ReadAllBytesAsync(FileDomain domain, string relativePath, CancellationToken cancellationToken = default) =>
                _inner.ReadAllBytesAsync(domain, relativePath, cancellationToken);

            public UniTask WriteAllTextAsync(FileDomain domain, string relativePath, string content, CancellationToken cancellationToken = default) =>
                _inner.WriteAllTextAsync(domain, relativePath, content, cancellationToken);

            public UniTask WriteAllBytesAsync(FileDomain domain, string relativePath, byte[] data, CancellationToken cancellationToken = default) =>
                _inner.WriteAllBytesAsync(domain, relativePath, data, cancellationToken);

            public void Delete(FileDomain domain, string relativePath) => _inner.Delete(domain, relativePath);

            public UniTask<string[]> GetFilesAsync(FileDomain domain, string relativePath, string searchPattern = "*", CancellationToken cancellationToken = default) =>
                _inner.GetFilesAsync(domain, relativePath, searchPattern, cancellationToken);

            public void CreateDirectory(FileDomain domain, string relativePath) => _inner.CreateDirectory(domain, relativePath);

            public string GetPhysicalPath(FileDomain domain, string relativePath) => _inner.GetPhysicalPath(domain, relativePath);
        }
    }
}
