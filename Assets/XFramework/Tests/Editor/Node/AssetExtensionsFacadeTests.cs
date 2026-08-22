using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XNode;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// AssetExtensions 节点便捷方法测试。验证每个扩展方法正确转发到 <see cref="AssetManager"/> 静态门面。
    /// <para>self 参数仅占位（扩展方法不使用节点实例），传 null 即可。</para>
    /// </summary>
    class AssetExtensionsFacadeTests
    {
        private FakeAssetManager _fake;

        [SetUp]
        public void SetUp()
        {
            _fake = new FakeAssetManager();
            AssetManager.SetInstance(_fake);
        }

        [TearDown]
        public void TearDown()
        {
            AssetManager.Destroy();
        }

        #region Load — UniTask

        [Test]
        public void LoadAssetAsync_ForwardsToManager()
        {
            IBaseNode self = null;
            self.LoadAssetAsync<GameObject>("characters/player").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.LoadCallCount);
        }

        [Test]
        public void LoadAssetAsync_WithPriority_ForwardsToManager()
        {
            IBaseNode self = null;
            self.LoadAssetAsync<GameObject>("characters/player", 1).GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.LoadCallCount);
        }

        [Test]
        public void InstantiateAssetAsync_ForwardsToManager()
        {
            IBaseNode self = null;
            self.InstantiateAssetAsync("characters/player").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.InstantiateCallCount);
        }

        [Test]
        public void InstantiateAssetAsync_WithPosition_ForwardsToManager()
        {
            IBaseNode self = null;
            self.InstantiateAssetAsync("characters/player", Vector3.zero, Quaternion.identity).GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.InstantiateCallCount);
        }

        [Test]
        public void InstantiateAssetAsync_Generic_ForwardsToManager()
        {
            IBaseNode self = null;
            self.InstantiateAssetAsync<Camera>("ui/main_camera").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.InstantiateCallCount);
        }

        [Test]
        public void InstantiateAssetAsync_GenericWithPosition_ForwardsToManager()
        {
            IBaseNode self = null;
            self.InstantiateAssetAsync<Camera>("ui/main_camera", Vector3.zero, Quaternion.identity).GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.InstantiateCallCount);
        }

        [Test]
        public void LoadSceneAssetAsync_ForwardsToManager()
        {
            IBaseNode self = null;
            self.LoadSceneAssetAsync("scenes/main").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.SceneLoadCallCount);
        }

        [Test]
        public void PreloadAssetsAsync_ForwardsToManager()
        {
            IBaseNode self = null;
            self.PreloadAssetsAsync(new[] { "a", "b" }).GetAwaiter().GetResult();
            Assert.AreEqual(1, _fake.PreloadCallCount);
        }

        #endregion

        #region Pool Config / Lifecycle

        [Test]
        public void SetAssetPoolMaxSize_ForwardsToManager()
        {
            IBaseNode self = null;
            self.SetAssetPoolMaxSize("characters/bullet", 10);
            Assert.AreEqual(1, _fake.SetPoolMaxSizeCallCount);
        }

        [Test]
        public void GetAssetPoolStatus_ForwardsToManager()
        {
            IBaseNode self = null;
            var (pooled, active, max) = self.GetAssetPoolStatus("characters/bullet");
            Assert.AreEqual((0, 0, 0), (pooled, active, max));
        }

        [Test]
        public void DestroyAssetInstance_ForwardsToManager()
        {
            IBaseNode self = null;
            self.DestroyAssetInstance((GameObject)null);
            Assert.AreEqual(1, _fake.DestroyInstanceCallCount);
        }

        [Test]
        public void DestroyAssetInstance_Generic_ForwardsToManager()
        {
            IBaseNode self = null;
            self.DestroyAssetInstance((Camera)null);
            Assert.AreEqual(1, _fake.DestroyInstanceCallCount);
        }

        #endregion
    }
}
