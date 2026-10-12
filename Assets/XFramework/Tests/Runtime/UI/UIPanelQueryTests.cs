using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.Architecture.Tests;
using XFramework.XPool;
using XFramework.XUI.View;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 面板查询 API 测试。
    /// <para>此前查询只能逐类型试 <c>IsOpen&lt;T&gt;()</c>——「现在开着哪些面板」「哪个在最前面」
    /// 这类问题无从回答，除非调用方自己维护一份账。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelQueryTests
    {
        /// <summary>粗尺子阈值 = 实测坏形态信号 1/4（校准方法与数值见 Timer 侧同批用例）。</summary>
        private const long AllocationThresholdBytes = 400_000;

        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            // 归零：不依赖上一个 fixture 的 TearDown（SetUp 中途失败时它不会执行）
            UIManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_QueryTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();
            _factory.RegisterPanel<FakePanelC>();

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

        #region 外部销毁后的剪枝

        /// <summary>
        /// 第三方绕过 <c>CloseAsync</c> 直接销毁面板后，两份账必须一起剪。
        /// <para>显示栈（按次序、持强引用）与活动字典（按类型）是同一批面板的两份索引。此前只剪
        /// <c>_stack</c>，于是 <c>IsOpen&lt;T&gt;()</c> / <c>OpenCount</c> / <c>IsAnyOpen</c> 继续报
        /// 「还开着」，<c>GetPanel&lt;T&gt;()</c> 还会把伪 null 交给调用方；而同一次读取里
        /// <c>Panels</c> 不剪、<c>CopyPanels</c> 剪，两条读路径对同一份状态给出不同答案。</para>
        /// </summary>
        [Test]
        public async Task ExternalDestroy_PrunesBothAccounts()
        {
            var a = await UIManager.OpenAsync<FakePanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");

            // 绕过关闭路径，直接销毁——这正是空洞的来源
            UnityEngine.Object.DestroyImmediate(a.gameObject);

            Assert.IsFalse(UIManager.IsOpen<FakePanel>(), "已销毁的面板不该还报「开着」");
            Assert.IsNull(UIManager.GetPanel<FakePanel>(), "取回的应当是 null，而不是伪 null 引用");
            Assert.AreEqual(1, UIManager.OpenCount, "只剩另一个面板");
            Assert.IsTrue(UIManager.IsAnyOpen);

            var panels = UIManager.Panels;
            Assert.AreEqual(UIManager.OpenCount, panels.Count,
                "几条读路径必须给出同一个答案");
            for (int i = 0; i < panels.Count; i++)
                Assert.IsNotNull(panels[i], "活视图里不该留下空洞");

            using (ListPool<UIPanelBase>.GetPooled(out var buffer))
            {
                Assert.AreEqual(panels.Count, UIManager.CopyPanels(buffer),
                    "CopyPanels 与 Panels 不该出现两套口径");
            }
        }

        #endregion

        #region 计数与栈顶

        [Test]
        public async Task Counts_TrackOpenAndClose()
        {
            Assert.AreEqual(0, UIManager.OpenCount);
            Assert.IsFalse(UIManager.IsAnyOpen);
            Assert.IsNull(UIManager.GetTopPanel(), "无面板时栈顶为 null");

            await UIManager.OpenAsync<FakePanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");

            Assert.AreEqual(2, UIManager.OpenCount);
            Assert.IsTrue(UIManager.IsAnyOpen);

            await UIManager.CloseAsync<FakePanelB>();
            Assert.AreEqual(1, UIManager.OpenCount);

            await UIManager.CloseAsync<FakePanel>();
            Assert.AreEqual(0, UIManager.OpenCount);
            Assert.IsNull(UIManager.GetTopPanel());
        }

        [Test]
        public async Task GetTopPanel_FollowsDisplayStack()
        {
            var a = await UIManager.OpenAsync<FakePanel>("ui/a");
            Assert.AreSame(a, UIManager.GetTopPanel());

            var b = await UIManager.PushAsync<FakePanelB>("ui/b");
            Assert.AreSame(b, UIManager.GetTopPanel(), "后打开的在最前");

            await UIManager.PopAsync();
            Assert.AreSame(a, UIManager.GetTopPanel(), "弹出后回到前一个");
        }

        #endregion

        #region 缓冲区填充

        [Test]
        public async Task CopyPanels_ReturnsDisplayOrderBottomToTop()
        {
            var a = await UIManager.OpenAsync<FakePanel>("ui/a");
            var b = await UIManager.PushAsync<FakePanelB>("ui/b");
            var c = await UIManager.PushAsync<FakePanelC>("ui/c");

            var buffer = new List<UIPanelBase>();
            int count = UIManager.CopyPanels(buffer);

            Assert.AreEqual(3, count);
            Assert.AreSame(a, buffer[0], "底 → 顶");
            Assert.AreSame(b, buffer[1]);
            Assert.AreSame(c, buffer[2]);
        }

        [Test]
        public async Task CopyPanels_ClearsBufferFirst()
        {
            var buffer = new List<UIPanelBase> { null, null, null, null, null };
            await UIManager.OpenAsync<FakePanel>("ui/a");

            int count = UIManager.CopyPanels(buffer);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, buffer.Count, "缓冲区应先被清空，而不是追加");
        }

        [Test]
        public async Task CopyPanelsInLayer_FiltersByLayer()
        {
            var top = await UIManager.OpenAsync<FakePanel>("ui/top", UILayers.Top);
            var mid = await UIManager.OpenAsync<FakePanelB>("ui/mid", UILayers.Default);
            var bottom = await UIManager.OpenAsync<FakePanelC>("ui/bottom", UILayers.Background);

            var buffer = new List<UIPanelBase>();

            Assert.AreEqual(1, UIManager.CopyPanelsInLayer(UILayers.Default, buffer));
            Assert.AreSame(mid, buffer[0]);

            Assert.AreEqual(1, UIManager.CopyPanelsInLayer(UILayers.Top, buffer));
            Assert.AreSame(top, buffer[0]);

            Assert.AreEqual(1, UIManager.CopyPanelsInLayer(UILayers.Background, buffer));
            Assert.AreSame(bottom, buffer[0]);

            Assert.AreEqual(0, UIManager.CopyPanelsInLayer(UILayers.Popup, buffer));
        }

        [Test]
        public void CopyPanels_NullBuffer_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => UIManager.CopyPanels(null));
        }

        #endregion

        #region 零分配

        [Test]
        public async Task CopyPanels_ReusesCallerBuffer_WithoutAllocating()
        {
            for (int i = 0; i < 3; i++)
                await UIManager.OpenAsync<FakePanel>($"ui/p{i}");

            var buffer = new List<UIPanelBase>(8);

            AllocationRuler.AssertNoHeapGrowth("反复复制面板列表 10 万次",
                () => UIManager.CopyPanels(buffer), 100_000, AllocationThresholdBytes);
        }

        /// <summary>
        /// 结构断言：显示栈声明为<b>具体的</b> <c>List&lt;UIPanelBase&gt;</c>。
        /// <para><b>为什么这是证据</b>：<c>CopyPanels</c> 的复制走 <c>buffer.AddRange(_stack)</c>，
        /// 而 <c>AddRange</c> 只有在实参<b>运行期</b>实现 <c>ICollection&lt;T&gt;</c> 时才走 <c>CopyTo</c>
        /// 快速路径、不经枚举器。字段声明为 <c>List&lt;UIPanelBase&gt;</c> 就保证了运行期对象必然实现它
        /// （任何赋值进这个字段的对象都得是 <c>List&lt;UIPanelBase&gt;</c>）——源码里那句注释
        /// 「List 走 ICollection 快速路径，不经枚举器，不分配」由此被钉住。附带效果：声明成接口会让
        /// 将来任何 <c>foreach (_stack)</c> 每次装箱一个枚举器（Message 侧刚实测过这一档：≈23 B/次）。</para>
        /// <para><b>射程</b>：拦不住复制路径被改写成 LINQ 或引入临时容器——那由粗尺子兜底。</para>
        /// </summary>
        [Test]
        public void CopyPanels_StructuralEvidence_SourcesFromAConcreteList()
        {
            var field = typeof(UIManagerImpl).GetField("_stack", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "UIManagerImpl._stack 找不到——改名了？");

            Assert.AreEqual(typeof(List<UIPanelBase>), field.FieldType,
                $"_stack 的声明类型是 {field.FieldType}——必须是具体 List<UIPanelBase>：" +
                "声明成接口会失去「运行期必然实现 ICollection<T>」这条快速路径前提，" +
                "且让 foreach 每次装箱一个枚举器");
        }

        [Test]
        public async Task Panels_IsLiveView_NotACopy()
        {
            await UIManager.OpenAsync<FakePanel>("ui/a");

            var view = UIManager.Panels;
            Assert.AreEqual(1, view.Count);

            await UIManager.PushAsync<FakePanelB>("ui/b");

            Assert.AreEqual(2, view.Count,
                "Panels 是活视图，面板开合后随之变化——不要跨帧缓存它");
            Assert.AreSame(UIManager.GetTopPanel(), view[1]);
        }

        #endregion
    }
}
