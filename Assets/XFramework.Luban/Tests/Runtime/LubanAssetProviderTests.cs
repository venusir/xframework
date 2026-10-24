using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XAsset;
using XFramework.XConfig;
using XFramework.XConfig.Luban;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 默认加载后端的映射与错误用例：经 <see cref="ILubanAssetSource"/> 接缝注入替身，
    /// 不依赖真实 YooAsset。
    /// </summary>
    [TestFixture]
    public sealed class LubanAssetProviderTests
    {
        [SetUp]
        public void SetUp()
        {
            LubanConfig.DataProviderFactory = null;
            LubanConfig.Destroy();
            ConfigManager.Destroy();
            AssetManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            LubanConfig.DataProviderFactory = null;
            LubanConfig.Destroy();
            ConfigManager.Destroy();
            AssetManager.Destroy();
        }

        [Test]
        public async System.Threading.Tasks.Task LoadTextAssets_ValidHandles_ReturnsBytesAndDisposes()
        {
            var first = new FakeTextAssetHandle { Bytes = new byte[] { 1 } };
            var second = new FakeTextAssetHandle { Bytes = new byte[] { 2 } };
            var source = new FakeLubanAssetSource
            {
                TextHandles = new ILubanTextAssetHandle[] { first, second },
            };
            var provider = new AssetLubanDataProvider(source);
            var progress = new ListProgress();

            var result = await provider.LoadAsync(
                Files(), LubanDataLoadMode.TextAsset, progress, default);

            Assert.That(result["t1"], Is.EqualTo(new byte[] { 1 }));
            Assert.That(result["t2"], Is.EqualTo(new byte[] { 2 }));
            Assert.That(first.DisposeCalls, Is.EqualTo(1));
            Assert.That(second.DisposeCalls, Is.EqualTo(1));
            Assert.That(progress.Reports[progress.Reports.Count - 1].Progress, Is.EqualTo(0.75f));
        }

        [Test]
        public async System.Threading.Tasks.Task LoadTextAssets_InvalidSecondHandle_ThrowsWithContext()
        {
            var first = new FakeTextAssetHandle { Bytes = new byte[] { 1 } };
            var second = new FakeTextAssetHandle { IsValid = false, LastError = "boom" };
            var source = new FakeLubanAssetSource
            {
                TextHandles = new ILubanTextAssetHandle[] { first, second },
            };
            var provider = new AssetLubanDataProvider(source);

            try
            {
                await provider.LoadAsync(Files(), LubanDataLoadMode.TextAsset, null, default);
                Assert.Fail("Expected LubanConfigException.");
            }
            catch (LubanConfigException ex)
            {
                Assert.That(ex.Message, Does.Contain("t2"));
                Assert.That(ex.Message, Does.Contain("configs/t2.bytes"));
                Assert.That(ex.Message, Does.Contain("boom"));
            }

            Assert.That(first.DisposeCalls, Is.EqualTo(1));
            Assert.That(second.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async System.Threading.Tasks.Task LoadRawFiles_ValidHandle_ReturnsData()
        {
            var handle = new FakeRawFileHandle { Data = new byte[] { 3 } };
            var source = new FakeLubanAssetSource { RawHandle = handle };
            var provider = new AssetLubanDataProvider(source);

            var result = await provider.LoadAsync(
                SingleFile(), LubanDataLoadMode.RawFile, null, default);

            Assert.That(result["t1"], Is.EqualTo(new byte[] { 3 }));
            Assert.That(handle.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async System.Threading.Tasks.Task LoadRawFiles_NullData_Throws()
        {
            var handle = new FakeRawFileHandle { Data = null };
            var source = new FakeLubanAssetSource { RawHandle = handle };
            var provider = new AssetLubanDataProvider(source);

            try
            {
                await provider.LoadAsync(SingleFile(), LubanDataLoadMode.RawFile, null, default);
                Assert.Fail("Expected LubanConfigException.");
            }
            catch (LubanConfigException ex)
            {
                Assert.That(ex.Message, Does.Contain("t1"));
                Assert.That(ex.Message, Does.Contain("returned no data"));
            }
        }

        [Test]
        public async System.Threading.Tasks.Task LoadRawFiles_InvalidHandle_ThrowsWithContext()
        {
            var handle = new FakeRawFileHandle { IsValid = false, LastError = "raw boom" };
            var source = new FakeLubanAssetSource { RawHandle = handle };
            var provider = new AssetLubanDataProvider(source);

            try
            {
                await provider.LoadAsync(SingleFile(), LubanDataLoadMode.RawFile, null, default);
                Assert.Fail("Expected LubanConfigException.");
            }
            catch (LubanConfigException ex)
            {
                Assert.That(ex.Message, Does.Contain("t1"));
                Assert.That(ex.Message, Does.Contain("configs/t1.bytes"));
                Assert.That(ex.Message, Does.Contain("raw boom"));
            }
        }

        [Test]
        public async System.Threading.Tasks.Task DefaultSource_WithoutAssetManager_ThrowsHint()
        {
            var provider = new AssetLubanDataProvider();

            try
            {
                await provider.LoadAsync(SingleFile(), LubanDataLoadMode.TextAsset, null, default);
                Assert.Fail("Expected InvalidOperationException.");
            }
            catch (InvalidOperationException ex)
            {
                Assert.That(ex.Message, Does.Contain("AssetManager 尚未初始化"));
                Assert.That(ex.Message, Does.Contain("AssetManager.InitializeAsync"));
            }
        }

        [Test]
        public async System.Threading.Tasks.Task InitializeAsync_DefaultProviderWithoutAssetManager_SurfacesHint()
        {
            try
            {
                await LubanConfig.InitializeAsync(new FakeLubanAdapter());
                Assert.Fail("Expected LubanConfigException.");
            }
            catch (LubanConfigException ex)
            {
                Assert.That(ex.Message, Does.Contain("AssetManager 尚未初始化"));
                Assert.That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
            }
        }

        private static List<LubanTableFile> Files()
        {
            return new List<LubanTableFile>
            {
                new LubanTableFile("t1", "configs/t1.bytes"),
                new LubanTableFile("t2", "configs/t2.bytes"),
            };
        }

        private static List<LubanTableFile> SingleFile()
        {
            return new List<LubanTableFile>
            {
                new LubanTableFile("t1", "configs/t1.bytes"),
            };
        }
    }
}
