using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.Architecture.Tests;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 状态快照与导出测试。
    /// <para>用于回答「面板是不是漏关了」「遮罩为什么还亮着」「有几个打开卡在半路」这类
    /// 只能靠翻运行时状态定位的问题。</para>
    /// </summary>
    [TestFixture]
    public class UIDiagnosticsTests
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

            _root = new GameObject("UIRoot_DiagTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Gate = null;
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #region 快照

        [Test]
        public void GetState_ReflectsCounters()
        {
            var empty = UIManager.GetState();
            Assert.AreEqual(0, empty.OpenCount);
            Assert.AreEqual(0, empty.MaskRefCount);
            Assert.IsFalse(empty.IsMaskShowing);
            Assert.IsFalse(empty.CanGoBack);
        }

        [Test]
        public async Task GetState_TracksPanelsAndMask()
        {
            await UIManager.OpenAsync<FakePanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");
            var mask = UIManager.ShowMask();

            var state = UIManager.GetState();

            Assert.AreEqual(2, state.OpenCount);
            Assert.AreEqual(1, state.MaskRefCount, "遮罩被持有一份");
            Assert.IsTrue(state.IsMaskShowing);
            Assert.IsTrue(state.CanGoBack);

            var second = UIManager.ShowMask();
            Assert.AreEqual(2, UIManager.GetState().MaskRefCount, "第二份持有应计入");

            mask.Dispose();
            second.Dispose();
            Assert.AreEqual(0, UIManager.GetState().MaskRefCount);
            Assert.IsFalse(UIManager.GetState().IsMaskShowing, "引用归零后遮罩应隐藏");
        }

        [Test]
        public async Task GetState_CountsInFlightOpens()
        {
            _factory.Gate = new UniTaskCompletionSource<object>();

            var pending = UIManager.OpenAsync<FakePanel>("ui/slow").Preserve();
            await UniTask.Yield();

            Assert.AreEqual(1, UIManager.GetState().InFlightOpenCount,
                "卡在半路的打开应能被看见——这正是它存在的意义");

            _factory.Gate.TrySetResult(null);
            await pending;

            Assert.AreEqual(0, UIManager.GetState().InFlightOpenCount, "完成后应清零");
        }

        [Test]
        public async Task GetState_DoesNotAllocate()
        {
            await UIManager.OpenAsync<FakePanel>("ui/a");

            AllocationRuler.AssertNoHeapGrowth("读取状态快照 10 万次",
                () => UIManager.GetState(), 100_000, AllocationThresholdBytes);
        }

        /// <summary>
        /// 结构断言：快照是纯值类型、实例字段全为值类型且 <c>readonly</c>，返回类型也钉在快照上。
        /// <para><b>为什么这是「<c>GetState</c> 不分配」的确定性证据</b>：构造一个没有引用字段的值类型
        /// 不可能触发堆分配——这正是本类型 XML 自述「纯值类型、无引用字段，读取时零分配」的机械化。</para>
        /// <para><b>射程</b>：拦不住 <c>GetState</c> 内部的其它动作（它还会跑 <c>PruneStack</c>）——
        /// 那一段由粗尺子兜底。</para>
        /// </summary>
        [Test]
        public void GetState_StructuralEvidence_PureValueType()
        {
            var snapshot = typeof(UIStateSnapshot);

            Assert.IsTrue(snapshot.IsValueType,
                "UIStateSnapshot 必须是值类型——改成类即意味着每次 GetState 都分配一个快照对象");

            var fields = snapshot.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotEmpty(fields, "UIStateSnapshot 一个实例字段都没有？反射没找对目标");

            foreach (var field in fields)
            {
                Assert.IsTrue(field.FieldType.IsValueType,
                    $"UIStateSnapshot.{field.Name} 的类型是 {field.FieldType}——出现引用类型字段，" +
                    "快照构造就可能触发堆分配，调试面板的刷新循环会开始产生 GC");
                Assert.IsTrue(field.IsInitOnly,
                    $"UIStateSnapshot.{field.Name} 不是 readonly——快照必须不可变");
            }

            var getState = typeof(IUIManager).GetMethod("GetState", System.Type.EmptyTypes);
            Assert.IsNotNull(getState, "IUIManager.GetState() 找不到——改名了？");
            Assert.AreEqual(snapshot, getState.ReturnType,
                "IUIManager.GetState() 的返回类型变了——快照一旦是类或接口，每次读取都会分配");
        }

        #endregion

        #region 导出

        [Test]
        public async Task DumpState_ContainsPerPanelDetails()
        {
            var panel = await UIManager.OpenAsync<FakePanelB>("ui/a", UILayers.Popup);
            panel.UpdateTier = UpdateTier.Tier2;

            string dump = UIManager.DumpState();

            StringAssert.Contains("FakePanelB", dump, "应列出面板类型");
            StringAssert.Contains($"layer={UILayers.Popup}", dump);
            StringAssert.Contains("tier=Tier2", dump, "应列出档位——排查降频问题要用");
            StringAssert.Contains("focused", dump);
        }

        [Test]
        public void DumpState_EmptyState_IsReadable()
        {
            string dump = UIManager.DumpState();

            StringAssert.Contains("open=0", dump);
            StringAssert.Contains("(none)", dump);
        }

        [Test]
        public async Task DumpState_ShowsBlurredAndPaused()
        {
            await UIManager.OpenAsync<FakePanel>("ui/a");
            await UIManager.PushAsync<FakePanelB>("ui/b");

            string dump = UIManager.DumpState();

            StringAssert.Contains("blurred", dump, "被覆盖的面板应标出失焦");
            StringAssert.Contains("paused", dump);
        }

        #endregion
    }
}
