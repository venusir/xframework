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
    /// <see cref="FileManager.WriteAllBytesAtomicAsync"/> 原子写契约测试。
    /// <para>覆盖:原子写覆盖既有文件且无 .tmp 残留、底层 Provider 无原子能力时降级普通写并告警。</para>
    /// </summary>
    [TestFixture]
    public class FileManagerAtomicTests
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
            FileManager.Destroy();
        }

        [Test]
        public async Task WriteAllBytesAtomicAsync_OverwritesExisting_WithoutTmpResidue()
        {
            await FileManager.WriteAllBytesAsync(FileDomain.AppData, "slot_1.save", Encoding.UTF8.GetBytes("old"));

            await FileManager.WriteAllBytesAtomicAsync(FileDomain.AppData, "slot_1.save", Encoding.UTF8.GetBytes("new"));

            var read = await FileManager.ReadAllBytesAsync(FileDomain.AppData, "slot_1.save");
            Assert.AreEqual("new", Encoding.UTF8.GetString(read), "原子写应完整覆盖旧内容");
            Assert.IsFalse(FileManager.Exists(FileDomain.AppData, "slot_1.save.tmp"), "原子写完成后不应残留 .tmp 文件");
        }

        [Test]
        public async Task WriteAllBytesAtomicAsync_ProviderUnsupported_FallsBackWithWarning()
        {
            // 切换为无原子能力的哑 Provider(仅实现 IFileProvider)
            var nonAtomic = new NonAtomicFileProvider(_fileProvider);
            FileManager.Destroy();
            FileManager.Initialize(nonAtomic);
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("不支持原子写入"));
                await FileManager.WriteAllBytesAtomicAsync(FileDomain.AppData, "a.bin", Encoding.UTF8.GetBytes("data"));

                var read = await FileManager.ReadAllBytesAsync(FileDomain.AppData, "a.bin");
                Assert.AreEqual("data", Encoding.UTF8.GetString(read), "降级为普通写后文件仍应写入");
            }
            finally
            {
                FileManager.Destroy();
            }
        }

        /// <summary>
        /// 仅实现 <see cref="IFileProvider"/> 的哑 Provider：用于验证门面原子写的降级路径。
        /// </summary>
        private sealed class NonAtomicFileProvider : IFileProvider
        {
            private readonly IFileProvider _inner;

            public NonAtomicFileProvider(IFileProvider inner)
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
