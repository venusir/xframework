using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XAsset;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// <see cref="AssetPanelFactory"/> 的直接构造测试（R3-UIA-008）。
    /// <para>此前默认面板工厂只经 <c>UIManager</c> 间接被摸到，且所有 UI 用例都注入 <c>FakePanelFactory</c>
    /// ——「预制体缺组件」「实例化失败」两条失败面零覆盖。这里用公开的 <c>AssetManager.SetInstance</c>
    /// 注入 <see cref="FakeAssetManager"/>，直接测默认工厂自己的分支。</para>
    /// </summary>
    [TestFixture]
    public class AssetPanelFactoryTests
    {
        private FakeAssetManager _asset;
        private AssetPanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            AssetManager.Destroy();
            _asset = new FakeAssetManager();
            AssetManager.SetInstance(_asset);
            _factory = new AssetPanelFactory();
        }

        [TearDown]
        public void TearDown()
        {
            AssetManager.Destroy();
            _asset.DestroyAll();
        }

        [Test]
        public async System.Threading.Tasks.Task CreateAsync_LoadsPrefabWithComponent_ReturnsPanel()
        {
            _asset.RegisterPrefab<FakePanel>("ui/panel");

            var panel = await _factory.CreateAsync<FakePanel>("ui/panel", null);

            Assert.IsNotNull(panel);
            Assert.AreEqual("FakePanel", panel.gameObject.name, "工厂应把实例名改成面板类型名");
            Assert.AreEqual(1, _asset.InstantiateCount);
        }

        [Test]
        public async System.Threading.Tasks.Task CreateAsync_MissingComponent_DestroysInstanceAndReturnsNull()
        {
            _asset.RegisterPrefab("ui/empty"); // 空物体，没有 FakePanel 组件

            LogAssert.Expect(LogType.Error, new Regex("lacks component"));

            var panel = await _factory.CreateAsync<FakePanel>("ui/empty", null);

            Assert.IsNull(panel, "缺组件时必须返回 null");
            Assert.AreEqual(1, _asset.DestroyCount, "缺组件的实例必须交还 AssetManager 回收");
        }

        [Test]
        public async System.Threading.Tasks.Task CreateAsync_InstantiateFails_ReturnsNull()
        {
            _asset.FailInstantiate = true;

            var panel = await _factory.CreateAsync<FakePanel>("ui/fail", null);

            Assert.IsNull(panel);
            Assert.AreEqual(0, _asset.DestroyCount, "没有实例时不应调用回收");
        }

        [Test]
        public async System.Threading.Tasks.Task Release_ReturnsInstanceToAssetManager()
        {
            _asset.RegisterPrefab<FakePanel>("ui/panel");
            var panel = await _factory.CreateAsync<FakePanel>("ui/panel", null);

            _factory.Release(panel);

            Assert.AreEqual(1, _asset.DestroyCount);
            Assert.IsFalse(panel.gameObject.activeSelf, "默认工厂的回收走 AssetManager 的池语义（失活）");
        }
    }
}
