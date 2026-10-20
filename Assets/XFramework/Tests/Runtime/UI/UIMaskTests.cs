using System;
using System.Text.RegularExpressions;
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
            // 归零：不依赖上一个 fixture 的 TearDown（SetUp 中途失败时它不会执行）
            UIManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_MaskTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();
            _factory.RegisterPanel<ThrowingOnClosePanel>();

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
            var handle = UIManager.ShowMask();

            Assert.IsTrue(UIManager.IsMaskShowing);
            Assert.IsTrue(handle.IsValid);

            handle.Dispose();

            Assert.IsFalse(UIManager.IsMaskShowing, "唯一的持有者释放后遮罩应隐藏");
            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void SecondHolder_DoesNotHideUntilBothReleased()
        {
            var first = UIManager.ShowMask();
            var second = UIManager.ShowMask();

            first.Dispose();
            Assert.IsTrue(UIManager.IsMaskShowing,
                "还有持有者时不该隐藏——早先没有计数，先结束的那个会把遮罩关掉");

            second.Dispose();
            Assert.IsFalse(UIManager.IsMaskShowing);
        }

        [Test]
        public void Dispose_IsIdempotent_AndDoesNotTouchOtherHolders()
        {
            var first = UIManager.ShowMask();
            var second = UIManager.ShowMask();

            first.Dispose();
            first.Dispose();   // 重复释放不得再移除一项
            first.Dispose();

            Assert.IsFalse(first.IsValid);
            Assert.IsTrue(second.IsValid, "重复释放不该误伤其它持有者");
            Assert.IsTrue(UIManager.IsMaskShowing);

            second.Dispose();
            Assert.IsFalse(UIManager.IsMaskShowing);
        }

        [Test]
        public void HideMask_ClearsAllHolders()
        {
            UIManager.ShowMask();
            var handle = UIManager.ShowMask();

            UIManager.HideMask();

            Assert.IsFalse(UIManager.IsMaskShowing, "HideMask 清掉全部引用");
            Assert.IsFalse(handle.IsValid, "被 HideMask 清掉的句柄应失效");

            handle.Dispose();
        }

        #endregion

        #region 样式与排序

        [Test]
        public void ShowMask_AppliesStyleToCanvas()
        {
            var style = new UIMaskStyle(UILayers.Popup, new Color(1f, 0f, 0f, 0.25f));
            UIManager.ShowMask(style);

            var mask = _root.transform.Find("UIManager_Mask");
            Assert.IsNotNull(mask, "遮罩应挂在 UIRoot 下");

            Assert.AreEqual(UISorting.MaskOrder(UILayers.Popup), mask.GetComponent<Canvas>().sortingOrder);

            var image = mask.GetComponent<Image>();
            Assert.AreEqual(1f, image.color.r, 0.001f);
            Assert.AreEqual(0.25f, image.color.a, 0.001f);
        }

        /// <summary>
        /// 越界的遮罩层级要被钳制，与面板打开路径同口径。
        /// <para>此前只钳面板：遮罩把调用方给的层级直接送进 <c>MaskOrder</c>（= 层级 * 32 + 31），
        /// 传 2000 会算出 64031——而 <c>Canvas.sortingOrder</c> 是 16 位的，静默回绕成负值后遮罩会跑到
        /// 所有面板<b>后面</b>：既挡不住射线也看不见，且不报任何错。</para>
        /// </summary>
        [Test]
        public void ShowMask_OutOfRangeLayer_IsClamped()
        {
            LogAssert.Expect(LogType.Warning, new Regex("exceeds the panel layer limit"));

            UIManager.ShowMask(new UIMaskStyle(2000, Color.black));

            var mask = _root.transform.Find("UIManager_Mask");
            Assert.IsNotNull(mask, "遮罩应挂在 UIRoot 下");

            int order = mask.GetComponent<Canvas>().sortingOrder;
            Assert.AreEqual(UISorting.MaskOrder(UISorting.MaxPanelLayer), order,
                "越界层级应被钳到面板可用上限，而不是算出会回绕的序号");
            Assert.Greater(order, UISorting.PanelOrder(UISorting.MaxPanelLayer, 1),
                "钳制后仍要盖住同层最上的面板——否则遮罩形同虚设");
        }

        [Test]
        public void FullTransparentMask_IsExpressible()
        {
            // 挡住输入但不显示任何东西——「0 表示未指定」那类哨兵会让它无法表达
            UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, new Color(0f, 0f, 0f, 0f)));

            var mask = _root.transform.Find("UIManager_Mask");
            Assert.AreEqual(0f, mask.GetComponent<Image>().color.a, 0.001f);
            Assert.IsTrue(UIManager.IsMaskShowing);
        }

        #endregion

        #region 点击关闭

        [Test]
        public void SetMaskClickToClose_OnExistingMask_TakesEffect()
        {
            // 回归：早先 clickToClose 只在创建遮罩的那条分支里被读过，
            // 已存在的遮罩上传 true 完全没有反应
            UIManager.ShowMask();
            Assert.IsFalse(IsClickToCloseActive(), "默认不启用点击关闭");

            UIManager.SetMaskClickToClose(true);
            Assert.IsTrue(IsClickToCloseActive(), "对已存在的遮罩开启应生效");

            UIManager.SetMaskClickToClose(false);
            Assert.IsFalse(IsClickToCloseActive(), "关闭后应失效");
        }

        [Test]
        public void SetMaskClickToClose_WithoutMask_WarnsInsteadOfSilentlyIgnoring()
        {
            // 样式属于每一次 ShowMask 调用，本方法只用于「正在显示时改」。
            // 没有遮罩时无对象可改——出声，而不是静默丢弃。
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("遮罩尚未显示"));

            UIManager.SetMaskClickToClose(true);

            Assert.IsFalse(IsMaskVisible(), "不应凭空创建遮罩");
        }

        [Test]
        public void SetMaskClickToClose_AfterHideMask_Warns()
        {
            // UI-C-005：实例还在但已隐藏时同样无对象可改，必须告警而不是静默无效。
            UIManager.ShowMask();
            UIManager.HideMask();
            Assert.IsFalse(IsMaskVisible(), "前提：遮罩已隐藏");

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("遮罩尚未显示"));
            UIManager.SetMaskClickToClose(true);
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
            await UIManager.OpenAsync<FakePanel>("ui/first");
            await UIManager.PushAsync<FakePanelB>("ui/second");

            UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, Color.black, clickToClose: true));

            var button = FindMaskButton();
            Assert.IsTrue(IsClickToCloseActive(), "前置条件：点击关闭已启用");

            button.onClick.Invoke();
            await UniTask.Yield();

            Assert.IsFalse(UIManager.IsOpen<FakePanelB>(), "点击遮罩应弹掉栈顶");
            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "栈底面板应保持打开");
        }

        [Test]
        public async Task ClickToClose_OnCloseThrows_IsolatedAndLogged()
        {
            await UIManager.OpenAsync<FakePanel>("ui/first");
            await UIManager.PushAsync<ThrowingOnClosePanel>("ui/throwing");

            UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, Color.black, clickToClose: true));

            LogAssert.Expect(LogType.Error, new Regex("遮罩点击关闭面板时抛出异常"));
            FindMaskButton().onClick.Invoke();
            await UniTask.Yield();

            Assert.IsFalse(UIManager.IsOpen<ThrowingOnClosePanel>(),
                "OnClose 抛异常也必须完成关闭，不能把面板卡在半关闭态");
            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "栈底面板应保持打开");
        }

        [Test]
        public async Task ClickToClose_WithoutStack_DoesNothing()
        {
            await UIManager.OpenAsync<FakePanel>("ui/first");

            UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, Color.black, clickToClose: true));
            FindMaskButton().onClick.Invoke();
            await UniTask.Yield();

            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "栈底面板不参与弹出，点击遮罩应为 no-op");
        }

        #endregion

        #region 射线管道（点击真的送得到吗）

        /// <summary>
        /// 遮罩必须自带 <see cref="GraphicRaycaster"/>——这是「点击真的送得到」与「挡得住下方输入」
        /// 的<b>同一个</b>前提，不是可选装饰。
        /// <para><b>机制</b>：UGUI 的 raycaster 只查询<b>自己那个 Canvas</b> 下注册的图形，而
        /// <c>Graphic</c> 注册到<b>最近的</b> Canvas。遮罩自带一个 Canvas
        /// （<c>overrideSorting = true</c>），于是它的 <c>Image</c> 注册在遮罩自己的 Canvas 上，
        /// 挂在 UIRoot 那个 Canvas 上的 raycaster <b>看不到它</b>。少了遮罩自己的 raycaster，
        /// 它就是一张接不到点击的透明图片。</para>
        /// <para><b>为什么此前是盲区</b>：<c>ClickToClose_PopsTopPanel</c> 验的是<b>接线</b>
        /// （直接调 <c>Button.onClick.Invoke()</c>），不是 UGUI 的射线管道——测试壳没有
        /// <c>EventSystem</c> 与输入模块，真实指针事件跑不了（前提同 <c>UIPopupTests</c> 的说明）。
        /// 接线通了，管道无人过问。</para>
        /// <para>对照：弹窗捕获层有同样的要求且已被测住
        /// （<c>UIPopupTests.Catcher_HasGraphicRaycaster</c>）；Tip 与 HUD 的 Canvas 也都显式补了它。</para>
        /// </summary>
        [Test]
        public void Mask_HasGraphicRaycaster()
        {
            UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, Color.black, clickToClose: true));

            var mask = _root.transform.Find("UIManager_Mask");
            Assert.IsNotNull(mask, "前置条件：遮罩已创建");
            Assert.IsNotNull(mask.GetComponent<Image>(), "挡输入靠的是可被射线命中的图形");

            Assert.IsNotNull(mask.GetComponent<GraphicRaycaster>(),
                "遮罩必须自带 GraphicRaycaster，否则 clickToClose 永不触发，且挡不住下方输入");
        }

        /// <summary>
        /// 钉住上一条的<b>机制</b>：遮罩的 <c>Image</c> 注册在它自己的 Canvas 上，而非 UIRoot 的。
        /// <para>这条单独成立，才能解释「为什么根 Canvas 上的 raycaster 救不了它」——否则
        /// 「往 UIRoot 补一个 raycaster」看起来也是个可行修法。</para>
        /// </summary>
        [Test]
        public void Mask_Image_IsRegisteredToItsOwnCanvas()
        {
            UIManager.ShowMask();

            var mask = _root.transform.Find("UIManager_Mask");
            Assert.IsNotNull(mask, "前置条件：遮罩已创建");

            var image = mask.GetComponent<Image>();
            Assert.AreSame(mask.GetComponent<Canvas>(), image.canvas,
                "Image 注册到最近的 Canvas，而遮罩自带 Canvas——故只有遮罩自己的 raycaster 能命中它");
        }

        /// <summary>
        /// 正向控制：同一套装置下，面板视图<b>确实</b>拿到了 <see cref="GraphicRaycaster"/>
        /// （<c>UIViewBase</c> 的 <c>[RequireComponent]</c> 自动补齐）。
        /// <para>它绿而 <c>Mask_HasGraphicRaycaster</c> 红，才说明红的是<b>遮罩</b>本身，
        /// 而不是「这套测试里根本拿不到 raycaster」。</para>
        /// </summary>
        [Test]
        public async Task Control_PanelView_HasGraphicRaycaster()
        {
            var panel = await UIManager.OpenAsync<FakePanel>("ui/first");

            Assert.IsNotNull(panel.GetComponent<GraphicRaycaster>(),
                "面板视图经 UIViewBase 的 RequireComponent 自带 raycaster——它是本 fixture 的正向控制");
        }

        #endregion

        #region 面板联动

        [Test]
        public async Task OwnerPanelClosed_ReleasesItsMaskHold()
        {
            var panel = await UIManager.OpenAsync<FakePanel>("ui/first");
            UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, Color.black), panel);

            Assert.IsTrue(UIManager.IsMaskShowing);

            await UIManager.CloseAsync<FakePanel>();

            Assert.IsFalse(UIManager.IsMaskShowing,
                "面板自己开的遮罩应随它一起释放，调用方无需记得配对关闭");
        }

        [Test]
        public async Task OwnerPanelClosed_KeepsOtherHoldersMask()
        {
            var panel = await UIManager.OpenAsync<FakePanel>("ui/first");
            UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, Color.black), panel);
            UIManager.ShowMask();   // 另一个系统的持有

            await UIManager.CloseAsync<FakePanel>();

            Assert.IsTrue(UIManager.IsMaskShowing, "面板释放自己那份后，别人的遮罩应保留");
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
