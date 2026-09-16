using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 面板资源真释放的测试。
    /// <para><c>ForgetPreload&lt;T&gt;</c> 只清记账；本组覆盖的 <c>UnloadPanelAssetAsync</c> 才是真释放。
    /// 分工是刻意的：现有 Asset 侧 API 无法在不清池的前提下把引用计数降到 0，故不做「看起来像释放」
    /// 的 no-op。</para>
    /// <para><b>未覆盖</b>：<c>ClearPool</c> / <c>UnloadUnusedAssetsAsync</c> 的真实资源行为——那需要
    /// YooAsset，属 Asset 模块自己的测试范围。这里覆盖 UI 侧的准入判断与「确实走到了 Asset 层」。</para>
    /// </summary>
    [TestFixture]
    public class UIAssetUnloadTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_UnloadTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        [Test]
        public async Task Unload_EmptyPath_ReturnsFalse()
        {
            Assert.IsFalse(await UIManager.UnloadPanelAssetAsync(null));
            Assert.IsFalse(await UIManager.UnloadPanelAssetAsync(string.Empty));
        }

        [Test]
        public async Task Unload_WhilePanelOpen_IsRefused()
        {
            // 资源被回收后，仍在使用的那个面板会变成悬空引用——宁可拒绝
            var panel = await UIManager.OpenAsync<FakePanel>("ui/a");
            Assert.AreEqual("ui/a", panel.AssetPath, "前置条件：面板记着自己的地址");

            LogAssert.Expect(LogType.Warning, new Regex("仍在使用"));

            Assert.IsFalse(await UIManager.UnloadPanelAssetAsync("ui/a"));
            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "面板不受影响");
        }

        [Test]
        public async Task Unload_UnopenedPath_PassesAdmissionAndReachesAssetLayer()
        {
            await UIManager.OpenAsync<FakePanel>("ui/a");

            // 打开的是 ui/a，故对 ui/b 的请求不该被「面板仍在使用」挡下，而应一路走到 Asset 层，
            // 在那里因为测试环境没有 YooAsset 而抛 InvalidOperationException。
            // 那个异常恰好证明准入判断没有误拦——这比断言一个 Bool 更有说服力。
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await UIManager.UnloadPanelAssetAsync("ui/b"));
        }
    }
}
