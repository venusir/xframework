using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XAsset;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// Tip 通路在「管理器仍在服役」与「管理器已被拆除」两种情形下的行为。
    /// <para>本 fixture 用 <see cref="FakeAssetManager"/> 注入假的资源层，从而第一次能在 Runtime 测试里
    /// 真的跑起 <c>UITipManagerImpl</c>——它硬编码了 <see cref="AssetManager"/>，此前只能靠评审。</para>
    /// </summary>
    [TestFixture]
    public class UITipProviderTests
    {
        /// <summary>Tip 预制体地址，与 <c>UITipManagerImpl.TipAssetPath</c> 一致。</summary>
        private const string TipAssetPath = "PF_UITipText";

        private GameObject _root;
        private FakeAssetManager _assets;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _assets = new FakeAssetManager();
            _assets.RegisterPrefab<UITipItem>(TipAssetPath);
            AssetManager.SetInstance(_assets);

            _root = new GameObject("UIRoot_TipTest", typeof(RectTransform));
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            _assets.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            // AssetManager 也必须复位：本 fixture 注入过假实例，留着会污染后续 fixture
            // （例如那些期待「资产层因为没有 YooAsset 而报错」的用例）
            AssetManager.Destroy();

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        /// <summary>
        /// 全部关闭之后，Tip 仍要能显示。
        /// <para><c>DetachAll</c> 会把管理器的 <c>_detached</c> 置位，而在途实例化完成后据此立即回池
        /// ——那是为「管理器已被拆除（Dispose / 换根）」准备的守卫。但 <c>CloseAllAsync</c> 收在播 Tip
        /// 时用的也是同一个 <c>DetachAll</c>，而管理器仍在服役；那个粘性标志却没有任何地方复位。
        /// 于是「关过一次全部」之后，每次 <c>ShowTipAsync</c> 都会实例化完立刻销毁：Tip 从此再也不显示，
        /// 而且不报任何错。</para>
        /// </summary>
        [Test]
        public async Task ShowTipAsync_AfterCloseAll_StillPlays()
        {
            // 本工程未导入 TMP Essentials（同 UITipSchedulingTests 记下的那条限制），故假预制体上
            // 没有 TMP_Text，UITipItem.Begin 会打一条「TMP_Text not found」的错误日志后提前返回。
            // 那与本用例要验的东西无关：_detached 守卫在 Begin 之前就已判定，断言看的是回收次数。
            LogAssert.ignoreFailingMessages = true;
            try
            {
                await UIManager.ShowTipAsync("first");
                Assert.AreEqual(1, _assets.InstantiateCount, "Tip 应经资源层实例化");

                await UIManager.CloseAllAsync();
                Assert.AreEqual(1, _assets.DestroyCount, "全部关闭应回收在播的 Tip");

                await UIManager.ShowTipAsync("second");

                Assert.AreEqual(2, _assets.InstantiateCount, "第二次 Tip 同样要经资源层实例化");
                Assert.AreEqual(1, _assets.DestroyCount,
                    "CloseAllAsync 之后新开的 Tip 不该被立刻回收——那等于 Tip 永久失效");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
