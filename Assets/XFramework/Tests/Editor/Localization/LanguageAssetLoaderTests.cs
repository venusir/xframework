using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XAsset;
using XFramework.XLocalization;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="LanguageAssetLoader"/> 测试:缓存命中直切、加载→解析→切换、异常与取消传播。
    /// <para>文本加载经 <see cref="LanguageAssetLoader.LoadTextFunc"/> 测试缝注入纯函数——
    /// 带资源内容的句柄依赖 YooAsset 运行环境,EditMode 不可构造;默认路径(资源为 null)经
    /// <see cref="FakeAssetManager"/> 注入 <see cref="AssetManager"/> 覆盖。</para>
    /// </summary>
    class LanguageAssetLoaderTests
    {
        private const string Template = "localization/lang_{0}";
        private const string JsonContent = "{\"title\":\"こんにちは\",\"count\":\"42\"}";

        [SetUp]
        public void SetUp()
        {
            LocalizationManager.Initialize("en", new Dictionary<string, string> { { "hi", "hello" } });
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationManager.Destroy();
        }

        [Test]
        public void CachedLanguage_CompletesWithoutLoading()
        {
            var loader = new LanguageAssetLoader("ja", Template);
            LocalizationManager.SetLanguageData("ja", new Dictionary<string, string> { { "title", "こんにちは" } });

            int loadCount = 0;
            loader.LoadTextFunc = (location, ct) =>
            {
                loadCount++;
                return UniTask.FromResult(JsonContent);
            };

            loader.LoadAsync(default).GetAwaiter().GetResult();

            Assert.AreEqual(0, loadCount, "缓存命中应跳过加载");
            Assert.AreEqual("ja", LocalizationManager.CurrentLanguage, "缓存命中应直接切换");
        }

        [Test]
        public void UnCachedLanguage_LoadsParsesAndSwitches()
        {
            var loader = new LanguageAssetLoader("ja", Template);

            string loadedLocation = null;
            loader.LoadTextFunc = (location, ct) =>
            {
                loadedLocation = location;
                return UniTask.FromResult(JsonContent);
            };

            loader.LoadAsync(default).GetAwaiter().GetResult();

            Assert.AreEqual("localization/lang_ja", loadedLocation, "加载地址应由模板拼接目标语言");
            Assert.AreEqual("ja", LocalizationManager.CurrentLanguage, "加载成功应切换当前语言");
            Assert.AreEqual("こんにちは", LocalizationManager.Get("title"), "解析后的数据应注入缓存");
        }

        [Test]
        public void EmptyJson_ThrowsAndKeepsCurrentLanguage()
        {
            var loader = new LanguageAssetLoader("ja", Template);
            loader.LoadTextFunc = (location, ct) => UniTask.FromResult("not a json");

            InvalidOperationException caught = null;
            try
            {
                loader.LoadAsync(default).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }

            Assert.IsNotNull(caught, "解析为空应抛 InvalidOperationException");
            StringAssert.Contains("Parsed empty language data", caught.Message);
            Assert.AreEqual("en", LocalizationManager.CurrentLanguage, "失败不得切换语言");
            Assert.IsFalse(LocalizationManager.HasLanguage("ja"), "失败不得注入缓存");
        }

        [Test]
        public void AssetReturnsNull_Throws()
        {
            // FakeAssetManager.LoadAsync 返回 default 句柄(Asset == null),覆盖默认加载路径的空资源分支
            AssetManager.SetInstance(new FakeAssetManager());
            try
            {
                var loader = new LanguageAssetLoader("ja", Template);

                InvalidOperationException caught = null;
                try
                {
                    loader.LoadAsync(default).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException ex)
                {
                    caught = ex;
                }

                Assert.IsNotNull(caught, "资源为 null 应抛 InvalidOperationException");
                StringAssert.Contains("AssetManager returned null", caught.Message);
            }
            finally
            {
                // 清理注入,避免影响其他测试对 AssetManager 初始态的假设
                AssetManager.Destroy();
            }
        }

        [Test]
        public void Cancellation_Propagates()
        {
            var loader = new LanguageAssetLoader("ja", Template);
            loader.LoadTextFunc = (location, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromResult(JsonContent);
            };

            var cts = new CancellationTokenSource();
            cts.Cancel();

            bool cancelled = false;
            try
            {
                loader.LoadAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.IsTrue(cancelled, "取消应经 OperationCanceledException 传播");
            Assert.AreEqual("en", LocalizationManager.CurrentLanguage, "取消不得切换语言");
        }
    }
}
