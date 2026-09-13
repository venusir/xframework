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
        public void UnregisterSelf_WhileMigrating_StaysUnregistered()
        {
            // 同一帧内既迁移 LOD 又注销自己。旧实现把两者各拆成一条 remove + 一条 add，
            // flush 又是「先全部 remove 再全部 add」，于是注销完又被插进新桶——永久复活
            var node = new ScriptedNode(_scheduler) { NextLOD = UpdateLOD.Frame8 };
            node.Script = (scheduler, self) => scheduler.Unregister(self);
            _scheduler.Register(node, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(0, _scheduler.TotalCount, "注销后不应残留任何条目");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Frame8), "迁移必须作废，不能把节点插进新桶");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, node.OnUpdateCallCount, "注销后不应再被派发");
        }

        [Test]
        public void UnregisterByOther_AfterTargetDispatched_MoveIsDiscarded()
        {
            var target = new TestUpdateable { ReturnLOD = UpdateLOD.Frame8 };
            var unregistrator = new ScriptedNode(_scheduler);
            unregistrator.Script = (scheduler, self) => scheduler.Unregister(target);

            // 同深度时按注册顺序派发：unregistrator 先跑，注销操作排在 target 的迁移操作之前
            _scheduler.Register(unregistrator, depth: 0);
            _scheduler.Register(target, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, target.OnUpdateCallCount, "注销被缓冲，目标本帧仍应被派发一次");
            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 unregistrator");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Frame8), "迁移必须作废");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, target.OnUpdateCallCount, "注销已生效，不再派发");
        }

        [Test]
        public void UnregisterDisabledNode_EnableDoesNotResurrect()
        {
            // 锁定既有语义：注销要一并清掉禁用表。只清桶的话，此后一旦有人 Enable，
            // 已注销的节点会被从禁用表里捞出来插回桶 0——又一条复活路径
            // （旧实现已如此，本用例防止重构时把它改掉）
            _scheduler.Register(_node, depth: 0);
            _scheduler.Disable(_node);
            _scheduler.Unregister(_node);

            Assert.AreEqual(0, _scheduler.TotalCount);
            Assert.AreEqual(0, _scheduler.DisabledCount, "注销应连同禁用表一起清理");

            _scheduler.Enable(_node);
            Assert.AreEqual(0, _scheduler.TotalCount, "Enable 不应把已注销的节点捞回桶里");

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(0, _node.OnUpdateCallCount);
        }

        [Test]
        public void UnregisterThenRegister_SameTick_RegisteredExactlyOnce()
        {
            // 逐条应用才能表达「先注销再注册」：两阶段 flush 无论怎么调顺序都得不到恰好一条
            var node = new ScriptedNode(_scheduler);
            node.Script = (scheduler, self) =>
            {
                scheduler.Unregister(self);
                scheduler.Register(self, depth: 0, initialLOD: UpdateLOD.Frame4);
            };
            _scheduler.Register(node, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, _scheduler.TotalCount, "先注销再注册应恰好剩一条");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame4), "重新注册应使用新的 LOD");

            // Frame4 桶每 4 帧才轮到一次切片：两份条目会在四帧内各派发一次，一份只派发一次
            _scheduler.Tick(time: 2.0f);
            _scheduler.Tick(time: 3.0f);
            _scheduler.Tick(time: 4.0f);
            _scheduler.Tick(time: 5.0f);
            Assert.AreEqual(2, node.OnUpdateCallCount, "四帧内恰好再派发一次");
        }

        [Test]
        public void DisableSelf_WhileMigrating_StaysDisabled()
        {
            // 同一帧内既迁移 LOD 又禁用自己：迁移是条件操作，节点已被移入禁用表后必须作废
            var node = new ScriptedNode(_scheduler) { NextLOD = UpdateLOD.Frame8 };
            node.Script = (scheduler, self) => scheduler.Disable(self);
            _scheduler.Register(node, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(0, _scheduler.TotalCount);
            Assert.AreEqual(1, _scheduler.DisabledCount);
            Assert.IsFalse(_scheduler.IsEnabled(node));
            Assert.AreEqual(1, node.OnDisableCallCount);
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Frame8),
                "迁移必须作废，不能把禁用中的节点插进新桶");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, node.OnUpdateCallCount, "禁用后不再派发");
        }

        [Test]
        public void DisableLowerIndexNode_DuringTick_DoesNotClobberRemaining()
        {
            // 桶 0 = [a, b, c]（同深度按注册顺序）。b 在自己的 OnUpdate 里禁用排在前面的 a：
            // 旧实现让 Disable 立即 RemoveAt，而 Tick 用 entries[i] = entry 写回——下标错位后
            // c 的整条 Entry 会被 b 覆盖（c 永久停更、b 此后每帧被派发两次）
            var a = new TestUpdateable();
            var b = new ScriptedNode(_scheduler);
            var c = new TestUpdateable();
            b.Script = (scheduler, self) => scheduler.Disable(a);

            _scheduler.Register(a, depth: 0);
            _scheduler.Register(b, depth: 0);
            _scheduler.Register(c, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, a.OnUpdateCallCount);
            Assert.AreEqual(1, b.OnUpdateCallCount);
            Assert.AreEqual(1, c.OnUpdateCallCount, "c 必须在——被覆盖则此后恒为 0");
            Assert.AreEqual(2, _scheduler.TotalCount);
            Assert.AreEqual(1, _scheduler.DisabledCount);

            _scheduler.Tick(time: 2.0f);

            Assert.AreEqual(1, a.OnUpdateCallCount, "a 已禁用，不再派发");
            Assert.AreEqual(2, b.OnUpdateCallCount);
            Assert.AreEqual(2, c.OnUpdateCallCount, "c 每帧都该派发一次");
        }

        [Test]
        public void DisableHigherIndexNode_DuringTick_NoReversedOrder()
        {
            // 禁用自下一帧生效：目标本帧仍会被派发一次，但绝不能出现
            // 「OnDisable 之后又 OnUpdate」的倒序（就地删除正是那个顺序）
            var a = new ScriptedNode(_scheduler);
            var b = new ScriptedNode(_scheduler);
            bool disabledBeforeOwnUpdate = false;
            a.Script = (scheduler, self) => scheduler.Disable(b);
            b.Script = (scheduler, self) => disabledBeforeOwnUpdate = b.OnDisableCallCount > 0;

            _scheduler.Register(a, depth: 0);
            _scheduler.Register(b, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, b.OnUpdateCallCount);
            Assert.AreEqual(1, b.OnDisableCallCount);
            Assert.IsFalse(disabledBeforeOwnUpdate, "OnDisable 必须排在本次 OnUpdate 之后");
            Assert.AreEqual(1, _scheduler.TotalCount);
            Assert.AreEqual(1, _scheduler.DisabledCount);

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, b.OnUpdateCallCount, "下一帧起不再派发");
        }

        [Test]
        public void EnableDuringTick_InsertsAfterFrameEnd_NoShift()
        {
            // 在 OnUpdate 里启用先前被禁用的节点：插入推迟到帧末，否则 InsertSorted
            // 会把正在遍历的元素整体后移，导致写回错位、本帧有节点被跳过
            var b = new TestUpdateable();
            _scheduler.Register(b, depth: 0);
            _scheduler.Disable(b);

            var a = new ScriptedNode(_scheduler);
            var c = new TestUpdateable();
            a.Script = (scheduler, self) => scheduler.Enable(b);

            _scheduler.Register(a, depth: 0);
            _scheduler.Register(c, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, a.OnUpdateCallCount);
            Assert.AreEqual(1, c.OnUpdateCallCount);
            Assert.AreEqual(0, b.OnUpdateCallCount, "帧末才插回桶里，本帧不派发");
            Assert.AreEqual(1, b.OnEnableCallCount);
            Assert.IsTrue(_scheduler.IsEnabled(b));

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, b.OnUpdateCallCount, "下一帧起参与派发");
            Assert.AreEqual(2, c.OnUpdateCallCount, "c 未受插入位移影响");
        }

        [Test]
        public void IsEnabled_ReflectsPendingOperations()
        {
            var target = new TestUpdateable();
            _scheduler.Register(target, depth: 0);

            bool? afterDisable = null;
            var disabler = new ScriptedNode(_scheduler);
            disabler.Script = (scheduler, self) =>
            {
                scheduler.Disable(target);
                afterDisable = scheduler.IsEnabled(target);
            };
            _scheduler.Register(disabler, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.IsFalse(afterDisable, "派发期间查询必须反映待处理操作，而非过期状态");
            Assert.IsFalse(_scheduler.IsEnabled(target));

            bool? afterEnable = null;
            var enabler = new ScriptedNode(_scheduler);
            enabler.Script = (scheduler, self) =>
            {
                scheduler.Enable(target);
                afterEnable = scheduler.IsEnabled(target);
            };
            _scheduler.Register(enabler, depth: 0);

            _scheduler.Tick(time: 2.0f);

            Assert.IsTrue(afterEnable);
            Assert.IsTrue(_scheduler.IsEnabled(target));
        }

        [Test]
        public void RegisterThenDisable_SameTick_NotDispatchedAndDisabled()
        {
            // Disable 只搜活表时会静默失败（新节点还在缓冲里），随后 flush 把它插进桶 0：
            // 于是这个「已被禁用」的节点每帧照收 OnUpdate，而 OnDisable 从未触发
            var late = new TestUpdateable();
            var a = new ScriptedNode(_scheduler);
            a.Script = (scheduler, self) =>
            {
                scheduler.Register(late, depth: 0);
                scheduler.Disable(late);
            };
            _scheduler.Register(a, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 a：late 应在禁用表里而不是桶里");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame1), "桶里只有 a 一个");
            Assert.AreEqual(1, _scheduler.DisabledCount);
            Assert.IsFalse(_scheduler.IsEnabled(late));
            Assert.AreEqual(1, late.OnDisableCallCount, "即使目标是当帧新注册的，OnDisable 也必须触发");
            Assert.AreEqual(0, late.OnUpdateCallCount);

            _scheduler.Enable(late);
            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, late.OnUpdateCallCount, "启用后恢复派发");
        }

        [Test]
        public void RegisterThenUnregister_SameTick_NotRegistered()
        {
            var late = new TestUpdateable();
            var a = new ScriptedNode(_scheduler);
            a.Script = (scheduler, self) =>
            {
                scheduler.Register(late, depth: 0);
                scheduler.Unregister(late);
            };
            _scheduler.Register(a, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 a");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(0, late.OnUpdateCallCount);
        }

        [Test]
        public void RegisterWhileDisabled_DoesNotResumeDispatch()
        {
            // 对处于禁用态的节点重复 Register（例如节点被重新挂到树上）不应让它开始派发
            _scheduler.Register(_node, depth: 0);
            _scheduler.Disable(_node);
            _scheduler.Register(_node, depth: 1);

            Assert.AreEqual(0, _scheduler.TotalCount);
            Assert.AreEqual(1, _scheduler.DisabledCount);
            Assert.IsFalse(_scheduler.IsEnabled(_node));

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(0, _node.OnUpdateCallCount);
        }

        [Test]
        public void TickReentrantFromOnUpdate_IsIgnored()
        {
            // 从 OnUpdate 里再调 Tick 会嵌套派发，并把闩锁提前置 false，
            // 让外层循环在「以为已退出迭代」的状态下继续跑
            var node = new ScriptedNode(_scheduler);
            node.Script = (scheduler, self) => scheduler.Tick(999f);
            _scheduler.Register(node, depth: 0);

            Assert.DoesNotThrow(() => _scheduler.Tick(time: 1.0f));
            Assert.AreEqual(1, node.OnUpdateCallCount, "重入的 Tick 不应再派发一次");
        }

        [Test]
        public void ClearDuringTick_DoesNotThrow()
        {
            // 切片分支在进入本帧时缓存了 count/end：就地把表清空会让随后的写回越界
            var node = new ScriptedNode(_scheduler) { NextLOD = UpdateLOD.Frame2 };
            node.Script = (scheduler, self) => scheduler.Clear();
            _scheduler.Register(node, depth: 0, initialLOD: UpdateLOD.Frame2);

            Assert.DoesNotThrow(() => _scheduler.Tick(time: 1.0f));
            Assert.AreEqual(0, _scheduler.TotalCount, "清空在帧末生效");
            Assert.AreEqual(0, _scheduler.DisabledCount);
        }

        [Test]
        public void ProcessImmediate_TargetUnregistersSelf_DoesNotCorruptOthers()
        {
            // ProcessImmediate 的写回用的是回调前查到的下标：回调里把节点注销后活表已变，
            // 写回会把顶替上来的 b 整条覆盖（b 永久停更、a 复活后继续被派发）
            var a = new ScriptedNode(_scheduler);
            var b = new TestUpdateable();
            a.Script = (scheduler, self) => scheduler.Unregister(self);

            _scheduler.Register(a, depth: 0);
            _scheduler.Register(b, depth: 0);

            _scheduler.ProcessImmediate(a, deltaTime: 0.5f, time: 1.0f);

            Assert.AreEqual(1, a.OnUpdateCallCount);
            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 b：a 已注销");

            _scheduler.Tick(time: 2.0f);

            Assert.AreEqual(1, b.OnUpdateCallCount, "b 必须还在——被覆盖则此后恒为 0");
            Assert.AreEqual(1, a.OnUpdateCallCount, "a 已注销，不再派发");
        }

        [Test]
        public void ProcessImmediate_TargetUnregistersSelfAndMigrates_MoveIsDiscarded()
        {
            // 旧实现按缓存的 index 先 RemoveAt 再插新桶：删掉的是顶替上来的 b，
            // 而已被注销的 a 反被插进新桶
            var a = new ScriptedNode(_scheduler) { NextLOD = UpdateLOD.Frame8 };
            var b = new TestUpdateable();
            a.Script = (scheduler, self) => scheduler.Unregister(self);

            _scheduler.Register(a, depth: 0);
            _scheduler.Register(b, depth: 0);

            _scheduler.ProcessImmediate(a, deltaTime: 0.5f, time: 1.0f);

            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 b");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Frame1), "b 仍在原桶");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Frame8), "迁移必须作废");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, b.OnUpdateCallCount);
        }

        [Test]
        public void ProcessImmediate_UnknownOrDisabledNode_DoesNothing()
        {
            // 未注册 / 已禁用的节点：与「不在管理中」一致，静默无操作且不回调
            var node = new ScriptedNode(_scheduler);

            _scheduler.ProcessImmediate(node, deltaTime: 0.5f, time: 1.0f);
            Assert.AreEqual(0, node.OnUpdateCallCount);

            _scheduler.Register(node, depth: 0);
            _scheduler.Disable(node);

            _scheduler.ProcessImmediate(node, deltaTime: 0.5f, time: 2.0f);
            Assert.AreEqual(0, node.OnUpdateCallCount);
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

        /// <summary>
        /// 派发中可执行一次剧本动作的测试替身，用于构造「回调里增删改调度状态」的各种组合。
        /// <para><see cref="Script"/> 在<b>第一次</b> OnUpdate 时执行一次，参数为调度器与自身。</para>
        /// </summary>
        private sealed class ScriptedNode : IUpdateable
        {
            private readonly UpdateScheduler _scheduler;
            private bool _hasRunScript;

            public UpdateLOD NextLOD { get; set; } = UpdateLOD.Frame1;
            public System.Action<UpdateScheduler, IUpdateable> Script { get; set; }
            public int OnUpdateCallCount { get; private set; }
            public int OnEnableCallCount { get; private set; }
            public int OnDisableCallCount { get; private set; }

            public ScriptedNode(UpdateScheduler scheduler)
            {
                _scheduler = scheduler;
            }

            public void OnEnable() => OnEnableCallCount++;

            public void OnDisable() => OnDisableCallCount++;

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                if (!_hasRunScript)
                {
                    _hasRunScript = true;
                    Script?.Invoke(_scheduler, this);
                }
                return NextLOD;
            }
        }
    }
}