using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
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
        public void MapTextAssets_CoversValidInvalidAndMissingAsset()
        {
            // R4-LBN-002：真实 AssetHandle 只能由 YooAsset 句柄构造，成功映射此前零直接覆盖。
            var text = new TextAsset("abc");
            try
            {
                var results = AssetManagerLubanAssetSource.MapTextAssets(
                    new[]
                    {
                        new AssetManagerLubanAssetSource.LubanTextSlice(true, text, null),
                        new AssetManagerLubanAssetSource.LubanTextSlice(false, null, "404"),
                        new AssetManagerLubanAssetSource.LubanTextSlice(true, null, null),
                    });

                Assert.That(results[0].IsValid, Is.True);
                Assert.That(System.Text.Encoding.UTF8.GetString(results[0].Bytes), Is.EqualTo("abc"));
                Assert.That(results[1].IsValid, Is.False);
                Assert.That(results[1].LastError, Is.EqualTo("404"));
                Assert.That(results[2].IsValid, Is.True);
                Assert.That(results[2].Bytes, Is.Null, "有效但 Asset 为 null 时不上报字节");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(text);
            }
        }

        [Test]
        public void MapRawFile_CoversValidAndInvalid()
        {
            var valid = AssetManagerLubanAssetSource.MapRawFile(
                new AssetManagerLubanAssetSource.LubanRawSlice(true, new byte[] { 1, 2 }, null));
            var invalid = AssetManagerLubanAssetSource.MapRawFile(
                new AssetManagerLubanAssetSource.LubanRawSlice(false, null, "missing"));

            Assert.That(valid.IsValid, Is.True);
            Assert.That(valid.GetData(), Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(invalid.LastError, Is.EqualTo("missing"));
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
