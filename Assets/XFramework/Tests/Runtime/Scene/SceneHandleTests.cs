using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XFramework.XAsset;

// 本文件的命名空间里就有 XFramework.XScene.SceneManager（判的是类型名），想指 Unity 那个必须起别名
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace XFramework.XScene.Tests
{
    /// <summary>
    /// <see cref="AssetSceneHandle"/> 经由本模块暴露出去的那一半契约：默认值语义、卸载转发、
    /// 即发即忘的 <c>Dispose</c>，以及激活 / 查询的转发。
    /// <para>句柄本身的字段与构造归 <c>XAsset</c> 管（它在那边有更细的用例）；这里打的是「使用方看到的形状」。</para>
    /// </summary>
    [TestFixture]
    public class SceneHandleTests
    {
        #region Private Fields

        private FakeSceneAssetManager _fake;

        #endregion

        #region Lifecycle

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _fake = new FakeSceneAssetManager();
            AssetManager.SetInstance(_fake);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AssetManager.ImplFactory = null;
            AssetManager.Destroy();
            yield return null;
        }

        #endregion

        #region Tests — 句柄形状

        [Test]
        public void DefaultHandle_IsDefaultAndInvalid()
        {
            var handle = default(AssetSceneHandle);

            Assert.IsTrue(handle.IsDefault, "加载失败拿到的是 default——它是「没有句柄」的判据");
            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, handle.Id, "0 保留给无效句柄");
        }

        [Test]
        public void DefaultHandle_DisposeDoesNotThrow()
        {
            var handle = default(AssetSceneHandle);

            Assert.DoesNotThrow(() => handle.Dispose(), "失败路径拿到的无效句柄必须能安全 Dispose（使用方不必判空）");
        }

        /// <summary>
        /// 结构断言而不是分配断言：本仓两把分配尺子在本环境恒返回 0（见 <c>Workflow.md</c> §六），
        /// 「零 GC 的原因」只能靠反射钉住——句柄是值类型、公开字段全是值类型，复制它不产生堆分配。
        /// </summary>
        [Test]
        public void Handle_IsAValueTypeWhosePublicFieldsAreAllValueTypes()
        {
            var type = typeof(AssetSceneHandle);

            Assert.IsTrue(type.IsValueType, "句柄必须是值类型：它要在每帧路径上被复制、可长期持有");
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(type), "配对卸载靠 IDisposable 这一半");

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsTrue(field.FieldType.IsValueType,
                    $"公开字段 {field.Name} 是引用类型——那会让「零 GC」这条承诺站不住");
            }
        }

        #endregion

        #region Tests — 卸载

        [Test]
        public async Task UnloadAsync_ForwardsToTheCapability()
        {
            var handle = new AssetSceneHandle(7, default);

            var unloaded = await handle.UnloadAsync();

            Assert.IsTrue(unloaded);
            Assert.AreEqual(1, _fake.SceneUnloadCallCount);
            Assert.AreEqual(7, _fake.LastUnloadedHandle.Id, "句柄必须原样下传——后端要靠它找回那次加载");
        }

        [Test]
        public async Task UnloadAsync_WhenBackendRefuses_ReturnsFalse()
        {
            _fake.SceneUnloadResult = false;

            var unloaded = await new AssetSceneHandle(1, default).UnloadAsync();

            Assert.IsFalse(unloaded, "后端拒绝（场景已失效 / 它是唯一场景）时返回 false 而不抛");
        }

        [Test]
        public async Task Dispose_KicksOffUnload()
        {
            var handle = new AssetSceneHandle(3, default);

            handle.Dispose();
            await UniTask.DelayFrame(2); // 即发即忘：卸载在后台发起，等两帧让接力跑完

            Assert.AreEqual(1, _fake.SceneUnloadCallCount, "Dispose 必须真的发起卸载");
            Assert.AreEqual(3, _fake.LastUnloadedHandle.Id);
        }

        #endregion

        #region Tests — 激活与查询

        [Test]
        public void SetActiveScene_InvalidScene_ReturnsFalse()
        {
            Assert.IsFalse(SceneManager.SetActiveScene(default(Scene)), "无效场景应当返回 false 而不是抛");
            Assert.IsFalse(SceneManager.SetActiveScene(UnitySceneManager.GetSceneByName("不存在的场景")));
        }

        [Test]
        public void ActiveScene_ForwardsToUnity()
        {
            Assert.AreEqual(UnitySceneManager.GetActiveScene(), SceneManager.ActiveScene);
        }

        #endregion
    }
}
