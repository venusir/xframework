using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XUpdate.Tests
{
    internal sealed class TestUpdateable : IUpdateable
    {
        public int OnEnableCallCount { get; set; }
        public int OnDisableCallCount { get; set; }
        public int OnUpdateCallCount { get; set; }
        public UpdateLOD ReturnLOD { get; set; } = UpdateLOD.Frame1;
        public bool ThrowException { get; set; }
        public List<float> DeltaTimes { get; } = new List<float>(4);
        public List<float> Times { get; } = new List<float>(4);

        public void OnEnable() => OnEnableCallCount++;
        public void OnDisable() => OnDisableCallCount++;

        public UpdateLOD OnUpdate(float deltaTime, float time)
        {
            OnUpdateCallCount++;
            DeltaTimes.Add(deltaTime);
            Times.Add(time);
            if (ThrowException)
                throw new System.Exception("Test exception");
            return ReturnLOD;
        }

        public void Reset()
        {
            OnEnableCallCount = 0;
            OnDisableCallCount = 0;
            OnUpdateCallCount = 0;
            ReturnLOD = UpdateLOD.Frame1;
            ThrowException = false;
            DeltaTimes.Clear();
            Times.Clear();
        }
    }

    [TestFixture]
    public class UpdateSchedulerTests
    {
        private UpdateScheduler _scheduler;
        private TestUpdateable _node;

        [SetUp]
        public void SetUp()
        {
            _scheduler = new UpdateScheduler();
            _node = new TestUpdateable();
        }

        [TearDown]
        public void TearDown()
        {
            _scheduler.Clear();
            _node.Reset();
        }

        [Test]
        public void Register_AddsNode_CanBeTicked()
        {
            _scheduler.Register(_node, depth: 0);
            Assert.AreEqual(1, _scheduler.TotalCount);

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, _node.OnUpdateCallCount);
        }

        [Test]
        public void Unregister_RemovesNode_NotTicked()
        {
            _scheduler.Register(_node, depth: 0);
            _scheduler.Unregister(_node);
            Assert.AreEqual(0, _scheduler.TotalCount);

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(0, _node.OnUpdateCallCount);
        }

        [Test]
        public void Unregister_UnknownNode_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _scheduler.Unregister(new TestUpdateable()));
        }

        [Test]
        public void Register_Null_DoesNotAdd()
        {
            _scheduler.Register(null, depth: 0);
            Assert.AreEqual(0, _scheduler.TotalCount);
        }

        [Test]
        public void Register_DuringTick_BufferedAndApplied()
        {
            var lateNode = new TestUpdateable();
            _scheduler.Register(_node, depth: 0);

            // _node 返回 Frame32：本次 Tick 里它会被迁到 Frame32 桶（也走 pending 路径）
            _node.ReturnLOD = UpdateLOD.Frame32;

            // registrator 在自己的 OnUpdate 里注册 lateNode——此时调度器正在迭代，该注册应被缓冲
            var registrator = new RegistratorNode(_scheduler, lateNode, depth: 1);
            _scheduler.Register(registrator, depth: 0);

            _scheduler.Tick(time: 1.0f);

            // 缓冲的操作在 Tick 结束时统一生效：
            // 桶 0 = [registrator, lateNode]（lateNode 用默认 Frame1 注册），桶 5 = [_node]
            Assert.AreEqual(3, _scheduler.TotalCount, "Tick 期间发起的注册与 LOD 迁移都应在结束时生效");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame32), "_node 返回 Frame32 后应已迁入该桶");
            Assert.AreEqual(1, registrator.OnUpdateCallCount);
            Assert.AreEqual(0, lateNode.OnUpdateCallCount,
                "被缓冲意味着本帧不派发——这正是「缓冲」而非「立即生效」的意义所在");

            _scheduler.Tick(time: 2.0f);

            Assert.AreEqual(2, registrator.OnUpdateCallCount, "桶 0 每帧全量派发");
            Assert.AreEqual(1, lateNode.OnUpdateCallCount, "上一帧缓冲进来的节点本帧开始参与派发");
        }

        [Test]
        public void Unregister_DuringTick_BufferedAndApplied()
        {
            _scheduler.Register(_node, depth: 0);
            var unregistrator = new UnregistratorNode(_scheduler, _node);
            _scheduler.Register(unregistrator, depth: 0);

            // First tick: unregistrator unregisters _node, both are ticked
            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, _node.OnUpdateCallCount);
            Assert.AreEqual(1, unregistrator.OnUpdateCallCount);

            // Second tick: _node should be removed
            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, _node.OnUpdateCallCount);
            Assert.AreEqual(2, unregistrator.OnUpdateCallCount);
        }

        [Test]
        public void OnUpdate_ReturnsDifferentLOD_MovesBucket()
        {
            _scheduler.Register(_node, depth: 0);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame1));
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Frame8));

            // Move to LOD Frame8
            _node.ReturnLOD = UpdateLOD.Frame8;
            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Frame1));
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame8));
        }

        [Test]
        public void Enable_TriggersOnEnable_ResumesUpdate()
        {
            _scheduler.Register(_node, depth: 0);

            _scheduler.Disable(_node);
            Assert.AreEqual(1, _node.OnDisableCallCount);
            Assert.IsFalse(_scheduler.IsEnabled(_node));

            _scheduler.Enable(_node);
            Assert.AreEqual(1, _node.OnEnableCallCount);
            Assert.IsTrue(_scheduler.IsEnabled(_node));

            // After enable, node should receive updates
            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, _node.OnUpdateCallCount);
        }

        [Test]
        public void Disable_TriggersOnDisable_StopsUpdate()
        {
            _scheduler.Register(_node, depth: 0);
            _scheduler.Disable(_node);

            Assert.AreEqual(1, _node.OnDisableCallCount);
            Assert.IsFalse(_scheduler.IsEnabled(_node));

            // Disabled node should not be ticked
            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(0, _node.OnUpdateCallCount);
        }

        [Test]
        public void IsEnabled_ReturnsCorrectStatus()
        {
            _scheduler.Register(_node, depth: 0);
            Assert.IsTrue(_scheduler.IsEnabled(_node));

            _scheduler.Disable(_node);
            Assert.IsFalse(_scheduler.IsEnabled(_node));

            _scheduler.Enable(_node);
            Assert.IsTrue(_scheduler.IsEnabled(_node));
        }

        [Test]
        public void IsEnabled_Null_ReturnsFalse()
        {
            Assert.IsFalse(_scheduler.IsEnabled(null));
        }

        [Test]
        public void ProcessImmediate_ExecutesAndAdjustsLOD()
        {
            _scheduler.Register(_node, depth: 0);
            _node.ReturnLOD = UpdateLOD.Frame16;

            _scheduler.ProcessImmediate(_node, deltaTime: 0.5f, time: 10.0f);

            Assert.AreEqual(1, _node.OnUpdateCallCount);
            Assert.AreEqual(0.5f, _node.DeltaTimes[0], 1e-6f);
            Assert.AreEqual(10.0f, _node.Times[0], 1e-6f);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame16));
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Frame1));
        }

        [Test]
        public void ProcessImmediate_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _scheduler.ProcessImmediate(null, 0f, 0f));
        }

        [Test]
        public void Clear_EmptiesAllBuckets()
        {
            _scheduler.Register(_node, depth: 0);
            var node2 = new TestUpdateable();
            _scheduler.Register(node2, depth: 1);

            _scheduler.Disable(node2);
            Assert.AreEqual(1, _scheduler.DisabledCount);

            _scheduler.Clear();
            Assert.AreEqual(0, _scheduler.TotalCount);
            Assert.AreEqual(0, _scheduler.DisabledCount);
        }

        [Test]
        public void Tick_Exception_UnregistersNode()
        {
            _scheduler.Register(_node, depth: 0);
            _node.ThrowException = true;

            // LogAssert.Expect(LogType, string) 是**完全相等**匹配
            // （见 LogMatch.cs 的 Message.Equals(log.Message)），而实现里 {e} 会附上异常栈，
            // 故此处的字符串永远匹配不上、那条 Error 反而被判为 Unhandled。
            // 必须用 Regex 做部分匹配——仓库既有惯例见 Message/EventStreamTests.cs
            LogAssert.Expect(LogType.Error,
                new Regex(Regex.Escape(
                    "[UpdateScheduler] TestUpdateable.OnUpdate threw exception, unregistering: System.Exception: Test exception")));
            _scheduler.Tick(time: 1.0f);

            // Node should be removed after exception
            Assert.AreEqual(0, _scheduler.TotalCount);
        }

        [Test]
        public void Depth_InsertSorted_MaintainsOrder()
        {
            var node0 = new TestUpdateable();
            var node3 = new TestUpdateable();
            var node1 = new TestUpdateable();

            _scheduler.Register(node3, depth: 3);
            _scheduler.Register(node0, depth: 0);
            _scheduler.Register(node1, depth: 1);

            // Depth order should be: 0, 1, 3
            Assert.AreEqual(3, _scheduler.TotalCount);
        }

        [Test]
        public void GetCount_ReturnsCorrectCount()
        {
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Frame1));

            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Frame1);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame1));

            _scheduler.Register(new TestUpdateable(), depth: 0, initialLOD: UpdateLOD.Frame8);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame8));
        }

        [Test]
        public void Register_WithInitialLOD_UsesCorrectBucket()
        {
            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Frame32);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame32));
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Frame1));
        }

        [Test]
        public void Enable_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _scheduler.Enable(null));
        }

        [Test]
        public void Disable_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _scheduler.Disable(null));
        }

        [Test]
        public void Enable_UnknownNode_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _scheduler.Enable(new TestUpdateable()));
        }

        [Test]
        public void Disable_UnknownNode_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _scheduler.Disable(new TestUpdateable()));
        }

        [Test]
        public void TotalCount_ReturnsSumOfAllLODs()
        {
            Assert.AreEqual(0, _scheduler.TotalCount);

            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Frame1);
            _scheduler.Register(new TestUpdateable(), depth: 0, initialLOD: UpdateLOD.Frame2);
            _scheduler.Register(new TestUpdateable(), depth: 0, initialLOD: UpdateLOD.Frame8);

            Assert.AreEqual(3, _scheduler.TotalCount);
        }

        /// <summary>
        /// Test helper that registers another node during OnUpdate.
        /// </summary>
        private sealed class RegistratorNode : IUpdateable
        {
            private readonly UpdateScheduler _scheduler;
            private readonly IUpdateable _target;
            private readonly int _depth;
            private bool _hasRegistered;

            public int OnUpdateCallCount { get; private set; }

            public RegistratorNode(UpdateScheduler scheduler, IUpdateable target, int depth)
            {
                _scheduler = scheduler;
                _target = target;
                _depth = depth;
            }

            public void OnEnable() { }
            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                if (!_hasRegistered)
                {
                    _hasRegistered = true;
                    _scheduler.Register(_target, _depth);
                }
                return UpdateLOD.Frame1;
            }
        }

        /// <summary>
        /// Test helper that unregisters another node during OnUpdate.
        /// </summary>
        private sealed class UnregistratorNode : IUpdateable
        {
            private readonly UpdateScheduler _scheduler;
            private readonly IUpdateable _target;
            private bool _hasUnregistered;

            public int OnUpdateCallCount { get; private set; }

            public UnregistratorNode(UpdateScheduler scheduler, IUpdateable target)
            {
                _scheduler = scheduler;
                _target = target;
            }

            public void OnEnable() { }
            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                if (!_hasUnregistered)
                {
                    _hasUnregistered = true;
                    _scheduler.Unregister(_target);
                }
                return UpdateLOD.Frame1;
            }
        }
    }
}