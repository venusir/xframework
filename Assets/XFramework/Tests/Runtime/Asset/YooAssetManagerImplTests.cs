using NUnit.Framework;
using XFramework.XAsset;

namespace XFramework.XAsset.Tests
{
    /// <summary>
    /// <see cref="YooAssetManagerImpl"/> 的引擎事件订阅生命周期（R3-AST-002）。
    /// <para>适配器在构造时订阅 <c>SceneManager.sceneUnloaded</c>；会话复位必须能「只退订、不 Dispose」
    /// ——否则关闭域重载时旧实例被静态事件钉住、跨会话继续回调。</para>
    /// </summary>
    [TestFixture]
    public class YooAssetManagerImplTests
    {
        [Test]
        public void DetachSceneUnloadedHandler_IsIdempotent()
        {
            var impl = new YooAssetManagerImpl("TestPackage");
            try
            {
                Assert.IsTrue(impl.IsSceneUnloadedHandlerAttached, "构造后应已订阅 sceneUnloaded");

                impl.DetachSceneUnloadedHandler();
                Assert.IsFalse(impl.IsSceneUnloadedHandlerAttached);

                Assert.DoesNotThrow(() => impl.DetachSceneUnloadedHandler(),
                    "重复退订必须是幂等的");
            }
            finally
            {
                impl.Destroy();
            }
        }

        [Test]
        public void Destroy_DetachesSceneUnloadedHandler()
        {
            var impl = new YooAssetManagerImpl("TestPackage");

            impl.Destroy();

            Assert.IsFalse(impl.IsSceneUnloadedHandlerAttached, "Destroy 后不应残留 sceneUnloaded 订阅");
        }
    }
}
