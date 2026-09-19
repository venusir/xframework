using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 面板每帧派发的参数测试。
    /// <para><c>OnUpdate(float deltaTime, float time)</c> 携带的不是 <c>Time.deltaTime</c>，而是
    /// 「距上次派发」的间隔。面板可声明较低档位被降频派发，此时两者相差整数倍——继续用
    /// <c>Time.deltaTime</c> 做积分会慢若干倍。故这里用 <see cref="UpdateManager.Tick(float)"/>
    /// 精确驱动，断言派发方给出的值确实等于两次时刻之差。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelUpdateSignatureTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_UpdateSigTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<UpdateRecordingPanel>();

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
        public async Task OnUpdate_ReceivesTimeFromDriver()
        {
            var panel = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/rec");

            UpdateManager.Tick(time: 1.0f);

            Assert.AreEqual(1, panel.UpdateCount, "注册后首次 Tick 即应派发");
            Assert.AreEqual(1.0f, panel.LastTime, "time 应原样来自驱动方传入的时刻");
        }

        [Test]
        public async Task OnUpdate_ReceivesDeltaTimeBetweenDispatches()
        {
            var panel = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/rec");

            UpdateManager.Tick(time: 1.0f);   // 首次派发：无基准，delta 记 0
            UpdateManager.Tick(time: 2.5f);

            Assert.AreEqual(2, panel.UpdateCount);
            Assert.AreEqual(2.5f, panel.LastTime);
            Assert.AreEqual(1.5f, panel.LastDeltaTime,
                "deltaTime 应为两次派发的时刻差，而非引擎的 Time.deltaTime");
        }

        [Test]
        public async Task Update_DirectCall_ForwardsParameters()
        {
            var panel = await UIManager.OpenAsync<UpdateRecordingPanel>("ui/rec");

            // 测试与自定义驱动方仍可手动推进一步
            UIManager.Update(0.25f, 7.5f);

            Assert.AreEqual(1, panel.UpdateCount);
            Assert.AreEqual(0.25f, panel.LastDeltaTime);
            Assert.AreEqual(7.5f, panel.LastTime);
        }
    }
}
