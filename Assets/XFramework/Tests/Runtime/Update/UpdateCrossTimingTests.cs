using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XUpdate;

namespace XFramework.XUpdate.Tests
{
    /// <summary>
    /// 跨时机（同一对象同时注册在多个时机）的操作次序测试。
    /// <para><b>为什么单列一个 fixture：</b>「派发期间发起的操作帧末生效」与「不会出现
    /// 「OnDisable 之后又 OnUpdate」的倒序」这两条契约，在每套调度器各自迭代时由
    /// <c>UpdateSchedulerTests</c> 钉住；但门面的 Enable/Disable/Unregister/Tick/ProcessImmediate
    /// 是<b>向三套调度器全部转发</b>的，于是「当前这一套在迭代、另外两套不在」时，另外两套会
    /// 立即应用操作——把用户回调嵌进别人的回调栈里，并让同桶靠后的条目在本帧倒序收到回调。
    /// 这类缺陷单调度器用例看不见，必须在门面层构造。</para>
    /// <para>本 fixture 关闭自动驱动以保证计数与次序确定（见 fixture 复位约定）。</para>
    /// </summary>
    [TestFixture]
    public class UpdateCrossTimingTests
    {
        #region Test Doubles

        /// <summary>
        /// 回调次序记录器。除事件序列外还记录<b>回调嵌套深度</b>——「这个回调是不是嵌在
        /// 别人的派发回调栈里执行」是本次要钉的关键事实，光看事件序列分不出来。
        /// </summary>
        private sealed class Recorder
        {
            public readonly List<string> Events = new List<string>(16);

            /// <summary>当前正在执行的派发回调层数。</summary>
            public int Depth;

            /// <summary>被禁用/被启用的回调是否在另一节点的回调栈内执行过。</summary>
            public bool RanInsideAnotherCallback;

            /// <summary>LateUpdate 时机的派发次数。</summary>
            public int LateCount;

            public int IndexOf(string evt)
            {
                return Events.IndexOf(evt);
            }
        }

        /// <summary>
        /// 注册在 Update 时机、在<b>自己的派发回调里</b>禁用另一个节点的调用者。
        /// </summary>
        private sealed class DisablerNode : IUpdateable
        {
            private readonly Recorder _recorder;
            private readonly IUpdateLifecycle _target;
            private bool _issued;

            public DisablerNode(Recorder recorder, IUpdateLifecycle target)
            {
                _recorder = recorder;
                _target = target;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                _recorder.Depth++;
                _recorder.Events.Add("caller.update");

                // 只发一次：本用例要看的是「这一条操作怎么落地」，不是重复请求的幂等性
                if (!_issued)
                {
                    _issued = true;
                    UpdateManager.Disable(_target);
                }

                _recorder.Depth--;
                return UpdateTier.Tier0;
            }
        }

        /// <summary>
        /// 注册在 Update 时机、在派发回调里再次调 <see cref="UpdateManager.Tick(float)"/> 的调用者。
        /// </summary>
        private sealed class ReentrantTickNode : IUpdateable
        {
            private readonly Recorder _recorder;
            private bool _issued;

            public ReentrantTickNode(Recorder recorder)
            {
                _recorder = recorder;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                _recorder.Depth++;
                _recorder.Events.Add("caller.update");

                if (!_issued)
                {
                    _issued = true;
                    UpdateManager.Tick(time: 2.0f);
                }

                _recorder.Depth--;
                return UpdateTier.Tier0;
            }
        }

        /// <summary>
        /// 同时注册在两个时机上的目标节点。
        /// </summary>
        private sealed class BothTimingsTarget : IUpdateable, ILateUpdateable
        {
            private readonly Recorder _recorder;

            public BothTimingsTarget(Recorder recorder)
            {
                _recorder = recorder;
            }

            public void OnEnable()
            {
                _recorder.Events.Add("target.enable");
            }

            public void OnDisable()
            {
                if (_recorder.Depth > 0)
                {
                    _recorder.RanInsideAnotherCallback = true;
                }
                _recorder.Events.Add("target.disable");
            }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                _recorder.Events.Add("target.update");
                return UpdateTier.Tier0;
            }

            public UpdateTier OnLateUpdate(float deltaTime, float time)
            {
                if (_recorder.Depth > 0)
                {
                    _recorder.RanInsideAnotherCallback = true;
                }
                _recorder.LateCount++;
                _recorder.Events.Add("target.late");
                return UpdateTier.Tier0;
            }
        }

        #endregion

        #region Fixture

        [SetUp]
        public void SetUp()
        {
            // 关闭自动驱动：本 fixture 的断言是精确次序，注入的每帧驱动会在用例中途插进来
            UpdateManager.AutoDriveEnabled = false;
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            UpdateManager.AutoDriveEnabled = true;
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        #endregion

        #region 跨时机的操作落地

        [Test]
        public void DisableFromDispatch_DoesNotRunCallbackInsideAnotherNodesCallback()
        {
            var recorder = new Recorder();
            var target = new BothTimingsTarget(recorder);
            var caller = new DisablerNode(recorder, target);

            UpdateManager.Register(caller, order: 0);
            UpdateManager.Register(target, order: 1); // 同桶靠后：禁用请求发出后它仍在本帧的派发范围内
            UpdateManager.RegisterLate(target, order: 0);

            UpdateManager.Tick(time: 1.0f);

            Assert.IsFalse(recorder.RanInsideAnotherCallback,
                "派发期间的操作必须由各调度器收尾时应用，不得嵌进当前回调的栈里执行" +
                "（Update 那套在迭代、LateUpdate 那套不在时会立即生效）");

            int updateAt = recorder.IndexOf("target.update");
            int disableAt = recorder.IndexOf("target.disable");
            Assert.GreaterOrEqual(updateAt, 0, "被禁用的节点本帧仍可能收到一次回调（帧末才生效）");
            Assert.Greater(disableAt, updateAt, "不得出现「OnDisable 之后又 OnUpdate」的倒序");

            Assert.AreEqual(-1, recorder.IndexOf("target.late"),
                "禁用已在 Update 阶段落表，LateUpdate 时机不应再派发它");
        }

        [Test]
        public void TickFromDispatch_DoesNotDriveAnotherTiming()
        {
            var recorder = new Recorder();
            var target = new BothTimingsTarget(recorder);
            var caller = new ReentrantTickNode(recorder);

            UpdateManager.Register(caller, order: 0);
            UpdateManager.RegisterLate(target, order: 0);

            UpdateManager.Tick(time: 1.0f);

            Assert.IsFalse(recorder.RanInsideAnotherCallback,
                "派发中调 Tick 不得嵌套派发另一时机（自己那一套被闩锁挡下、另外两套原本挡不住）");
            Assert.AreEqual(1, recorder.LateCount, "外层 Tick 的 LateUpdate 时机仍应正常驱动一次");
        }

        #endregion
    }
}
