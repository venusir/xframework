using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 模态遮罩测试。
    /// <para>遮罩按引用计数：多个系统各自需要遮罩时，谁的流程结束都不该把别人的一起关掉。
    /// 点击关闭此前只在「创建遮罩的那一次」生效，已存在的遮罩上传 <c>true</c> 没有反应。</para>
    /// </summary>
    [TestFixture]
    public class UIMaskTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_MaskTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();

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

        #region 引用计数

        [Test]
        public void ShowMask_IsShowingUntilReleased()
        {
            var handle = UIManager.Mask.Show();

            Assert.IsTrue(UIManager.Mask.IsShowing);
            Assert.IsTrue(handle.IsValid);

            handle.Dispose();

            Assert.IsFalse(UIManager.Mask.IsShowing, "唯一的持有者释放后遮罩应隐藏");
            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void SecondHolder_DoesNotHideUntilBothReleased()
        {
            var first = UIManager.Mask.Show();
            var second = UIManager.Mask.Show();

            first.Dispose();
            Assert.IsTrue(UIManager.Mask.IsShowing,
                "还有持有者时不该隐藏——早先没有计数，先结束的那个会把遮罩关掉");

            second.Dispose();
            Assert.IsFalse(UIManager.Mask.IsShowing);
        }

        [Test]
        public void Dispose_IsIdempotent_AndDoesNotTouchOtherHolders()
        {
            var first = UIManager.Mask.Show();
            var second = UIManager.Mask.Show();

            first.Dispose();
            first.Dispose();   // 重复释放不得再移除一项
            first.Dispose();

            Assert.IsFalse(first.IsValid);
            Assert.IsTrue(second.IsValid, "重复释放不该误伤其它持有者");
            Assert.IsTrue(UIManager.Mask.IsShowing);

            second.Dispose();
            Assert.IsFalse(UIManager.Mask.IsShowing);
        }

        [Test]
        public void HideMask_ClearsAllHolders()
        {
            UIManager.Mask.Show();
            var handle = UIManager.Mask.Show();

            UIManager.Mask.Hide();

            Assert.IsFalse(UIManager.Mask.IsShowing, "HideMask 清掉全部引用");
            Assert.IsFalse(handle.IsValid, "被 HideMask 清掉的句柄应失效");

            handle.Dispose();
        }

        #endregion

        #region 样式与排序

        [Test]
        public void ShowMask_AppliesStyleToCanvas()
        {
            var style = new UIMaskStyle(UILayers.Popup, new Color(1f, 0f, 0f, 0.25f));
            UIManager.Mask.Show(style);

            var mask = _root.transform.Find("UIManager_Mask");
            Assert.IsNotNull(mask, "遮罩应挂在 UIRoot 下");

            Assert.AreEqual(UISorting.MaskOrder(UILayers.Popup), mask.GetComponent<Canvas>().sortingOrder);

            var image = mask.GetComponent<Image>();
            Assert.AreEqual(1f, image.color.r, 0.001f);
            Assert.AreEqual(0.25f, image.color.a, 0.001f);
        }

        [Test]
        public void FullTransparentMask_IsExpressible()
        {
            // 挡住输入但不显示任何东西——「0 表示未指定」那类哨兵会让它无法表达
            UIManager.Mask.Show(new UIMaskStyle(UILayers.Mask, new Color(0f, 0f, 0f, 0f)));

            var mask = _root.transform.Find("UIManager_Mask");
            Assert.AreEqual(0f, mask.GetComponent<Image>().color.a, 0.001f);
            Assert.IsTrue(UIManager.Mask.IsShowing);
        }

        #endregion

        #region 点击关闭

        [Test]
        public void SetMaskClickToClose_OnExistingMask_TakesEffect()
        {
            // 回归：早先 clickToClose 只在创建遮罩的那条分支里被读过，
            // 已存在的遮罩上传 true 完全没有反应
            UIManager.Mask.Show();
            Assert.IsFalse(IsClickToCloseActive(), "默认不启用点击关闭");

            UIManager.Mask.SetClickToClose(true);
            Assert.IsTrue(IsClickToCloseActive(), "对已存在的遮罩开启应生效");

            UIManager.Mask.SetClickToClose(false);
            Assert.IsFalse(IsClickToCloseActive(), "关闭后应失效");
        }

        [Test]
        public void SetMaskClickToClose_WithoutMask_WarnsInsteadOfSilentlyIgnoring()
        {
            // 样式属于每一次 ShowMask 调用，本方法只用于「正在显示时改」。
            // 没有遮罩时无对象可改——出声，而不是静默丢弃。
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("遮罩尚未显示"));

            UIManager.Mask.SetClickToClose(true);

            Assert.IsFalse(IsMaskVisible(), "不应凭空创建遮罩");
        }

        private bool IsMaskVisible()
        {
            var mask = _root.transform.Find("UIManager_Mask");
            return mask != null && mask.gameObject.activeSelf;
        }

        [Test]
        public async Task ClickToClose_PopsTopPanel()
        {
            // C2 时就想要这条端到端用例，但被「遮罩走 AssetManager 销毁」挡住——
            // 只要走过 ShowMask 的 fixture 在 TearDown 调 UIManager.Destroy() 就会抛异常
            await UIManager.Panel.OpenAsync<FakePanel>("ui/first");
            await UIManager.Stack.PushAsync<FakePanelB>("ui/second");

            UIManager.Mask.Show(new UIMaskStyle(UILayers.Mask, Color.black, clickToClose: true));

            var button = FindMaskButton();
            Assert.IsTrue(IsClickToCloseActive(), "前置条件：点击关闭已启用");

            button.onClick.Invoke();
            await UniTask.Yield();

            Assert.IsFalse(UIManager.Panel.IsOpen<FakePanelB>(), "点击遮罩应弹掉栈顶");
            Assert.IsTrue(UIManager.Panel.IsOpen<FakePanel>(), "栈底面板应保持打开");
        }

        [Test]
        public async Task ClickToClose_WithoutStack_DoesNothing()
        {
            await UIManager.Panel.OpenAsync<FakePanel>("ui/first");

            UIManager.Mask.Show(new UIMaskStyle(UILayers.Mask, Color.black, clickToClose: true));
            FindMaskButton().onClick.Invoke();
            await UniTask.Yield();

            Assert.IsTrue(UIManager.Panel.IsOpen<FakePanel>(), "栈底面板不参与弹出，点击遮罩应为 no-op");
        }

        #endregion

        #region 面板联动

        [Test]
        public async Task OwnerPanelClosed_ReleasesItsMaskHold()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/first");
            UIManager.Mask.Show(new UIMaskStyle(UILayers.Mask, Color.black), panel);

            Assert.IsTrue(UIManager.Mask.IsShowing);

            await UIManager.Panel.CloseAsync<FakePanel>();

            Assert.IsFalse(UIManager.Mask.IsShowing,
                "面板自己开的遮罩应随它一起释放，调用方无需记得配对关闭");
        }

        [Test]
        public async Task OwnerPanelClosed_KeepsOtherHoldersMask()
        {
            var panel = await UIManager.Panel.OpenAsync<FakePanel>("ui/first");
            UIManager.Mask.Show(new UIMaskStyle(UILayers.Mask, Color.black), panel);
            UIManager.Mask.Show();   // 另一个系统的持有

            await UIManager.Panel.CloseAsync<FakePanel>();

            Assert.IsTrue(UIManager.Mask.IsShowing, "面板释放自己那份后，别人的遮罩应保留");
        }

        #endregion

        /// <summary>
        /// 点击关闭是否真的可用：Button 存在且处于启用状态。
        /// <para>开关用 <c>enabled</c> 而非增删组件，故不能只判断组件是否存在。</para>
        /// </summary>
        private bool IsClickToCloseActive()
        {
            var mask = _root.transform.Find("UIManager_Mask");
            if (mask == null)
                return false;

            var button = mask.GetComponent<Button>();
            return button != null && button.enabled;
        }

        private Button FindMaskButton()
        {
            var mask = _root.transform.Find("UIManager_Mask");
            return mask != null ? mask.GetComponent<Button>() : null;
        }
    }
}
