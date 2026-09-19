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
        public UpdateTier ReturnTier { get; set; } = UpdateTier.Tier0;

        /// <summary>让 <see cref="OnUpdate"/> 抛异常（用于锁定「OnUpdate 抛异常即注销」）。</summary>
        public bool ThrowException { get; set; }

        /// <summary>让 <see cref="OnDisable"/> 抛异常（用于锁定「回调异常不打断本帧剩余操作」）。</summary>
        public bool ThrowOnDisable { get; set; }

        /// <summary>让 <see cref="OnEnable"/> 抛异常。</summary>
        public bool ThrowOnEnable { get; set; }

        public List<float> DeltaTimes { get; } = new List<float>(4);
        public List<float> Times { get; } = new List<float>(4);

        public void OnEnable()
        {
            OnEnableCallCount++;
            if (ThrowOnEnable)
                throw new System.Exception("Test exception");
        }

        public void OnDisable()
        {
            OnDisableCallCount++;
            if (ThrowOnDisable)
                throw new System.Exception("Test exception");
        }

        public UpdateTier OnUpdate(float deltaTime, float time)
        {
            OnUpdateCallCount++;
            DeltaTimes.Add(deltaTime);
            Times.Add(time);
            if (ThrowException)
                throw new System.Exception("Test exception");
            return ReturnTier;
        }

        public void Reset()
        {
            OnEnableCallCount = 0;
            OnDisableCallCount = 0;
            OnUpdateCallCount = 0;
            ReturnTier = UpdateTier.Tier0;
            ThrowException = false;
            ThrowOnDisable = false;
            ThrowOnEnable = false;
            DeltaTimes.Clear();
            Times.Clear();
        }
    }

    [TestFixture]
    public class UpdateSchedulerTests
    {
        /// <summary>
        /// 驱动步长：略大于 <see cref="UpdateScheduler.TickPeriod"/>，使每次 Tick 恰好推进一<b>格</b>，
        /// 于是本夹具里「帧」与「格」一一对应——断言里的「每 N 帧」即「每 N 格」。
        /// <para>不能精确取 <c>1f / 60f</c>：浮点累加会让个别帧落到「不足一格」而被吞掉、下一帧
        /// 又补成两格，相位随之漂移。多出的 0.1% 在测试窗口（≤ 数十格）内攒不出第二格。</para>
        /// </summary>
        private const float FrameSeconds = 1.001f / 60f;

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
            _scheduler.Register(_node, order: 0);
            Assert.AreEqual(1, _scheduler.TotalCount);

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, _node.OnUpdateCallCount);
        }

        [Test]
        public void Unregister_RemovesNode_NotTicked()
        {
            _scheduler.Register(_node, order: 0);
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
            _scheduler.Register(null, order: 0);
            Assert.AreEqual(0, _scheduler.TotalCount);
        }

        [Test]
        public void Register_ValueTypeNode_IsRejectedWithError()
        {
            // 值类型节点每次转成接口都是一次新的装箱：注册进去也注销不掉（按引用找不回那个箱），
            // 条目会永远留在桶里每帧派发。与其留一条谁也管不着的幽灵条目，不如在注册处拒绝并留痕
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[UpdateScheduler] ValueTypeNode 是值类型：IUpdateLifecycle 必须由引用类型实现")));

            _scheduler.Register(new ValueTypeNode(), order: 0);

            Assert.AreEqual(0, _scheduler.TotalCount);
        }

        [Test]
        public void Register_EqualsOverridingNodes_AreDistinctEntries()
        {
            // 身份判定必须与桶内查找（接口引用比较）用同一把尺子。用默认比较器时，这两个「值相等」
            // 的实例会互相顶掉索引：注销其中一个就把两人的索引一起清掉，另一个从此注销不掉、
            // 留在桶里每帧继续被派发
            var first = new EqualsByValueNode(1);
            var second = new EqualsByValueNode(1);
            Assert.IsTrue(first.Equals(second), "前提：两个实例值相等（这正是默认比较器会混淆的情形）");

            _scheduler.Register(first, order: 0);
            _scheduler.Register(second, order: 0);
            Assert.AreEqual(2, _scheduler.TotalCount, "引用不同就是两个节点");

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, first.OnUpdateCallCount);
            Assert.AreEqual(1, second.OnUpdateCallCount);

            _scheduler.Unregister(first);
            Assert.AreEqual(1, _scheduler.TotalCount, "只摘掉按引用指定的那一个");

            _scheduler.Unregister(second);

            Assert.AreEqual(0, _scheduler.TotalCount, "两个都摘掉后不应残留幽灵条目");
            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, first.OnUpdateCallCount, "被注销的不再派发");
            Assert.AreEqual(1, second.OnUpdateCallCount);
        }

        [Test]
        public void Register_OutOfRangeTimeMode_Throws()
        {
            // 轴越界只能来自强转，没有可饱和的语义：放进桶下标就是数组越界，故在注册处按参数防御拒绝。
            // 与档位刻意不对称——档位越界是节点运行时返回的值，属框架控制之外的数据，应当钳而不抛
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => _scheduler.Register(_node, order: 0, timeMode: (UpdateTimeMode)2),
                "越上界的轴");
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => _scheduler.Register(_node, order: 0, timeMode: (UpdateTimeMode)(-1)),
                "越下界的轴");

            Assert.AreEqual(0, _scheduler.TotalCount, "被拒绝的注册不应留下任何条目");
        }

        [Test]
        public void Register_DuringTick_BufferedAndApplied()
        {
            var lateNode = new TestUpdateable();
            _scheduler.Register(_node, order: 0);

            // _node 返回 Tier5：本次 Tick 里它会被迁到 Tier5 桶（也走 pending 路径）
            _node.ReturnTier = UpdateTier.Tier5;

            // registrator 在自己的 OnUpdate 里注册 lateNode——此时调度器正在迭代，该注册应被缓冲
            var registrator = new RegistratorNode(_scheduler, lateNode, order: 1);
            _scheduler.Register(registrator, order: 0);

            _scheduler.Tick(time: 1.0f);

            // 缓冲的操作在 Tick 结束时统一生效：
            // 桶 0 = [registrator, lateNode]（lateNode 用默认 Tier0 注册），桶 5 = [_node]
            Assert.AreEqual(3, _scheduler.TotalCount, "Tick 期间发起的注册与档位迁移都应在结束时生效");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier5), "_node 返回 Tier5 后应已迁入该桶");
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
            _scheduler.Register(_node, order: 0);
            var unregistrator = new UnregistratorNode(_scheduler, _node);
            _scheduler.Register(unregistrator, order: 0);

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
            // 同一帧内既迁移档位又注销自己。旧实现把两者各拆成一条 remove + 一条 add，
            // flush 又是「先全部 remove 再全部 add」，于是注销完又被插进新桶——永久复活
            var node = new ScriptedNode(_scheduler) { NextTier = UpdateTier.Tier3 };
            node.Script = (scheduler, self) => scheduler.Unregister(self);
            _scheduler.Register(node, order: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(0, _scheduler.TotalCount, "注销后不应残留任何条目");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier3), "迁移必须作废，不能把节点插进新桶");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, node.OnUpdateCallCount, "注销后不应再被派发");
        }

        [Test]
        public void UnregisterByOther_AfterTargetDispatched_MoveIsDiscarded()
        {
            var target = new TestUpdateable { ReturnTier = UpdateTier.Tier3 };
            var unregistrator = new ScriptedNode(_scheduler);
            unregistrator.Script = (scheduler, self) => scheduler.Unregister(target);

            // 同排序号时按注册顺序派发：unregistrator 先跑，注销操作排在 target 的迁移操作之前
            _scheduler.Register(unregistrator, order: 0);
            _scheduler.Register(target, order: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, target.OnUpdateCallCount, "注销被缓冲，目标本帧仍应被派发一次");
            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 unregistrator");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier3), "迁移必须作废");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, target.OnUpdateCallCount, "注销已生效，不再派发");
        }

        [Test]
        public void UnregisterDisabledNode_EnableDoesNotResurrect()
        {
            // 锁定既有语义：注销要一并清掉禁用表。只清桶的话，此后一旦有人 Enable，
            // 已注销的节点会被从禁用表里捞出来插回桶 0——又一条复活路径
            // （旧实现已如此，本用例防止重构时把它改掉）
            _scheduler.Register(_node, order: 0);
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
                scheduler.Register(self, order: 0, initialTier: UpdateTier.Tier2);
            };
            _scheduler.Register(node, order: 0);

            float time = 0f;
            _scheduler.Tick(time += FrameSeconds);

            Assert.AreEqual(1, _scheduler.TotalCount, "先注销再注册应恰好剩一条");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier2), "重新注册应使用新的档位");

            // Tier2 桶每 4 格才轮到一次切片：两份条目会在四格内各派发一次，一份只派发一次
            _scheduler.Tick(time += FrameSeconds);
            _scheduler.Tick(time += FrameSeconds);
            _scheduler.Tick(time += FrameSeconds);
            _scheduler.Tick(time += FrameSeconds);
            Assert.AreEqual(2, node.OnUpdateCallCount, "四格内恰好再派发一次");
        }

        [Test]
        public void DisableSelf_WhileMigrating_StaysDisabled()
        {
            // 同一帧内既迁移档位又禁用自己：迁移是条件操作，节点已被移入禁用表后必须作废
            var node = new ScriptedNode(_scheduler) { NextTier = UpdateTier.Tier3 };
            node.Script = (scheduler, self) => scheduler.Disable(self);
            _scheduler.Register(node, order: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(0, _scheduler.TotalCount);
            Assert.AreEqual(1, _scheduler.DisabledCount);
            Assert.IsFalse(_scheduler.IsEnabled(node));
            Assert.AreEqual(1, node.OnDisableCallCount);
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier3),
                "迁移必须作废，不能把禁用中的节点插进新桶");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, node.OnUpdateCallCount, "禁用后不再派发");
        }

        [Test]
        public void DisableLowerIndexNode_DuringTick_DoesNotClobberRemaining()
        {
            // 桶 0 = [a, b, c]（同排序号按注册顺序）。b 在自己的 OnUpdate 里禁用排在前面的 a：
            // 旧实现让 Disable 立即 RemoveAt，而 Tick 用 entries[i] = entry 写回——下标错位后
            // c 的整条 Entry 会被 b 覆盖（c 永久停更、b 此后每帧被派发两次）
            var a = new TestUpdateable();
            var b = new ScriptedNode(_scheduler);
            var c = new TestUpdateable();
            b.Script = (scheduler, self) => scheduler.Disable(a);

            _scheduler.Register(a, order: 0);
            _scheduler.Register(b, order: 0);
            _scheduler.Register(c, order: 0);

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

            _scheduler.Register(a, order: 0);
            _scheduler.Register(b, order: 0);

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
            _scheduler.Register(b, order: 0);
            _scheduler.Disable(b);

            var a = new ScriptedNode(_scheduler);
            var c = new TestUpdateable();
            a.Script = (scheduler, self) => scheduler.Enable(b);

            _scheduler.Register(a, order: 0);
            _scheduler.Register(c, order: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, a.OnUpdateCallCount);
            Assert.AreEqual(1, c.OnUpdateCallCount);
            Assert.AreEqual(0, b.OnUpdateCallCount, "帧末才插回桶里，本帧不派发");
            Assert.AreEqual(2, b.OnEnableCallCount, "注册时宣告一次，帧末重新启用再宣告一次");
            Assert.IsTrue(_scheduler.IsEnabled(b));

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, b.OnUpdateCallCount, "下一帧起参与派发");
            Assert.AreEqual(2, c.OnUpdateCallCount, "c 未受插入位移影响");
        }

        [Test]
        public void IsEnabled_ReflectsPendingOperations()
        {
            var target = new TestUpdateable();
            _scheduler.Register(target, order: 0);

            bool? afterDisable = null;
            var disabler = new ScriptedNode(_scheduler);
            disabler.Script = (scheduler, self) =>
            {
                scheduler.Disable(target);
                afterDisable = scheduler.IsEnabled(target);
            };
            _scheduler.Register(disabler, order: 0);

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
            _scheduler.Register(enabler, order: 0);

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
                scheduler.Register(late, order: 0);
                scheduler.Disable(late);
            };
            _scheduler.Register(a, order: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 a：late 应在禁用表里而不是桶里");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier0), "桶里只有 a 一个");
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
                scheduler.Register(late, order: 0);
                scheduler.Unregister(late);
            };
            _scheduler.Register(a, order: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 a");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(0, late.OnUpdateCallCount);
        }

        [Test]
        public void RegisterWhileDisabled_DoesNotResumeDispatch()
        {
            // 对处于禁用态的节点重复 Register（例如节点被重新挂到树上）不应让它开始派发
            _scheduler.Register(_node, order: 0);
            _scheduler.Disable(_node);
            _scheduler.Register(_node, order: 1);

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
            _scheduler.Register(node, order: 0);

            Assert.DoesNotThrow(() => _scheduler.Tick(time: 1.0f));
            Assert.AreEqual(1, node.OnUpdateCallCount, "重入的 Tick 不应再派发一次");
        }

        [Test]
        public void ClearDuringTick_DoesNotThrow()
        {
            // 切片分支在进入本帧时缓存了 count/end：就地把表清空会让随后的写回越界
            var node = new ScriptedNode(_scheduler) { NextTier = UpdateTier.Tier1 };
            node.Script = (scheduler, self) => scheduler.Clear();
            _scheduler.Register(node, order: 0, initialTier: UpdateTier.Tier1);

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

            _scheduler.Register(a, order: 0);
            _scheduler.Register(b, order: 0);

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
            var a = new ScriptedNode(_scheduler) { NextTier = UpdateTier.Tier3 };
            var b = new TestUpdateable();
            a.Script = (scheduler, self) => scheduler.Unregister(self);

            _scheduler.Register(a, order: 0);
            _scheduler.Register(b, order: 0);

            _scheduler.ProcessImmediate(a, deltaTime: 0.5f, time: 1.0f);

            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 b");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier0), "b 仍在原桶");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier3), "迁移必须作废");

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

            _scheduler.Register(node, order: 0);
            _scheduler.Disable(node);

            _scheduler.ProcessImmediate(node, deltaTime: 0.5f, time: 2.0f);
            Assert.AreEqual(0, node.OnUpdateCallCount);
        }

        [Test]
        public void ProcessImmediate_DuringDispatch_OnlyPushesTimeBase()
        {
            // 派发期间调 ProcessImmediate 不执行更新——在别人的 OnUpdate 里再回调自己会形成嵌套派发；
            // 它只把时间基准推到该时刻，于是下一次正常派发的间隔自那一刻起算。
            // 目标放在 Tier1（单占桶、每格一转）：这样才能让「推进基准」与「下一次派发」之间
            // 隔着一格，否则同一帧内的派发会立刻把基准覆盖掉，效果就观察不到了
            // ReturnTier 必须跟着声明档位走：返回值是第二条档位通道，让它默认返回 Tier0 的话，
            // 目标首派之后就被迁回 Tier0 桶，本用例的「目标在 Tier1 上隔格派发」前提就没了
            var target = new TestUpdateable { ReturnTier = UpdateTier.Tier1 };
            float time = 0f;
            var caller = new ScriptedNode(_scheduler) { RunAtDispatchCount = 2 };
            caller.Script = (scheduler, self) => scheduler.ProcessImmediate(target, deltaTime: 0.5f, time: time);

            _scheduler.Register(caller, order: 0);
            _scheduler.Register(target, order: 0, initialTier: UpdateTier.Tier1);

            _scheduler.Tick(time += FrameSeconds);          // 格 0：目标首派（锚定，delta 0）
            Assert.AreEqual(1, target.OnUpdateCallCount);

            // 格 1：caller 第 2 次被派发 → 发 ProcessImmediate；目标不在本格切片上。
            // 基准推到的时刻取「本格时刻」，于是下一格的间隔应恰为一格
            _scheduler.Tick(time += FrameSeconds);

            Assert.AreEqual(1, target.OnUpdateCallCount, "派发期间调用不得额外回调一次（那就是嵌套派发）");

            _scheduler.Tick(time += FrameSeconds);          // 格 2：目标派发
            Assert.AreEqual(2, target.OnUpdateCallCount);
            Assert.AreEqual(FrameSeconds, target.DeltaTimes[1], 1e-6f,
                "间隔应自 ProcessImmediate 推到的时刻起算；若读到两格，说明基准根本没推");
        }

        [Test]
        public void SlicedBucket_SpreadsLoadEvenly_NoIdleFrames()
        {
            // 17 个条目、8 个切片：区间切片派发成 3,3,3,3,3,2,0,0（后两格白跑一遍循环），
            // 步长切片摊成 3,2,2,2,2,2,2,2——每格都干活且每格只差 1
            const int nodeCount = 17;
            var nodes = new TestUpdateable[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                nodes[i] = new TestUpdateable { ReturnTier = UpdateTier.Tier3 };
                _scheduler.Register(nodes[i], order: 0, initialTier: UpdateTier.Tier3);
            }

            var perFrame = new int[8];
            int dispatched = 0;
            float time = 0f;
            for (int frame = 0; frame < perFrame.Length; frame++)
            {
                time += FrameSeconds;
                _scheduler.Tick(time);

                int total = 0;
                for (int i = 0; i < nodeCount; i++)
                {
                    total += nodes[i].OnUpdateCallCount;
                }

                perFrame[frame] = total - dispatched;
                dispatched = total;
            }

            Assert.AreEqual(nodeCount, dispatched, "8 帧内每个节点恰好被派发一次");
            for (int i = 0; i < nodeCount; i++)
            {
                Assert.AreEqual(1, nodes[i].OnUpdateCallCount, $"节点 {i} 应恰好被派发一次");
            }

            for (int frame = 0; frame < perFrame.Length; frame++)
            {
                Assert.GreaterOrEqual(perFrame[frame], nodeCount / perFrame.Length,
                    $"第 {frame} 帧派发量不应低于均值下界（区间切片下尾部切片为 0）");
                Assert.LessOrEqual(perFrame[frame], (nodeCount + perFrame.Length - 1) / perFrame.Length,
                    $"第 {frame} 帧派发量不应超过均值上界");
            }
        }

        [Test]
        public void LoadProfile_PeakStaysWithinBoundOfMean()
        {
            // 「避免帧消耗集中」是本模块的目标，故把负载剖面钉住。实测（256 帧）：
            //   混合 63 节点（30/15/8/4/3/2/1 铺在 Tier1~Tier7）：峰 24@第 0 帧、均值 20.13、峰均比 1.19
            //   稀疏 7 节点（每档各 1 个）：峰 7@第 0 帧、均值 0.99、峰均比 7.06
            // 簇的形状：24,23,22,20,20... 与 7,0,1,0,2,0,1,0,3,0...——切片相位由桶内下标决定，
            // 而所有档位共用同一个 tickIndex，2 的幂互相整除，于是各档位的「下标 0」都在同一格命中。
            //
            // 已实测并否决的修法：给每档加相位偏移（(tickIndex + tier) & mask）只能把混合峰从 24
            // 降到 22（+4 → +2 次派发）、稀疏峰从 7 降到 2，即最坏帧多跑约 2~5 次回调、每 2.13 秒
            // 一次（几十 ns 量级）；而代价是「首帧落在切片 0」这条相位规律与若干钉相位的用例。
            // 叠束的绝对量本就被桶数上界（轴 × 档位 ≤ 16）封住，不值得为它动相位
            var mixProfile = CaptureLoadProfile(new[] { 30, 15, 8, 4, 3, 2, 1 }, 256);
            var sparseProfile = CaptureLoadProfile(new[] { 1, 1, 1, 1, 1, 1, 1 }, 256);

            Assert.LessOrEqual(Max(mixProfile), Mean(mixProfile) * 1.25,
                $"混合分布的峰均比应留在 1.25 以内（实测峰 {Max(mixProfile)}、均值 {Mean(mixProfile):F2}）");
            Assert.LessOrEqual(Max(sparseProfile), 8,
                $"稀疏分布下同格命中的派发量不应超过稀疏桶的个数（实测峰 {Max(sparseProfile)}）");
        }

        /// <summary>
        /// 按「每档节点数」表（下标 0 即 Tier1）铺一批节点，在独立调度器上逐帧驱动，
        /// 返回每帧的派发总量。用独立实例是为了让两组测量不共用桶。
        /// </summary>
        private static int[] CaptureLoadProfile(int[] countPerTier, int frames)
        {
            var scheduler = new UpdateScheduler();
            var nodes = new List<TestUpdateable>();

            for (int tier = 1; tier <= countPerTier.Length; tier++)
            {
                for (int i = 0; i < countPerTier[tier - 1]; i++)
                {
                    var node = new TestUpdateable { ReturnTier = (UpdateTier)tier };
                    nodes.Add(node);
                    scheduler.Register(node, order: 0, initialTier: (UpdateTier)tier);
                }
            }

            var perFrame = new int[frames];
            int dispatched = 0;
            float time = 0f;
            for (int frame = 0; frame < frames; frame++)
            {
                time += FrameSeconds;
                scheduler.Tick(time);

                int total = 0;
                for (int i = 0; i < nodes.Count; i++)
                {
                    total += nodes[i].OnUpdateCallCount;
                }

                perFrame[frame] = total - dispatched;
                dispatched = total;
            }

            return perFrame;
        }

        private static int Max(int[] values)
        {
            int max = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max) max = values[i];
            }
            return max;
        }

        private static double Mean(int[] values)
        {
            int sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }
            return (double)sum / values.Length;
        }

        [Test]
        public void SlicedNode_ReceivesAccumulatedDelta()
        {
            // 降频不导致时间失真：被跳过的帧应累积进下一次的 deltaTime——这是本调度器
            // 相对「固定步长 + 累加器」方案的核心取舍，此前零覆盖
            var node = new TestUpdateable { ReturnTier = UpdateTier.Tier3 };
            _scheduler.Register(node, order: 0, initialTier: UpdateTier.Tier3);

            float time = 0f;
            for (int frame = 0; frame < 8; frame++)
            {
                time += FrameSeconds;
                _scheduler.Tick(time);
            }

            Assert.AreEqual(1, node.OnUpdateCallCount, "8 格内首次派发");

            float secondDispatchTime = 0f;
            for (int frame = 0; frame < 8; frame++)
            {
                time += FrameSeconds;
                _scheduler.Tick(time);
                if (secondDispatchTime == 0f && node.OnUpdateCallCount == 2)
                {
                    secondDispatchTime = time;
                }
            }

            Assert.AreEqual(2, node.OnUpdateCallCount, "再过 8 格派发第二次");
            Assert.AreEqual(8f * FrameSeconds, node.DeltaTimes[1], 1e-4f, "被跳过的 7 格应累积进 delta");
            Assert.AreEqual(secondDispatchTime, node.Times[1], 1e-4f, "time 参数应是本次派发的绝对时刻");
        }

        [Test]
        public void RegisterTwice_SameNode_KeepsSingleEntry()
        {
            // 重复注册视为「重新注册」：两条条目会让同一节点每帧被派发两次，
            // 而且单值桶索引表达不了「分处两个桶」，注销时会漏删
            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier0);
            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier0);

            Assert.AreEqual(1, _scheduler.TotalCount);

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, _node.OnUpdateCallCount, "每帧恰好派发一次");

            _scheduler.Unregister(_node);
            Assert.AreEqual(0, _scheduler.TotalCount, "注销后不应留下第二条条目");
        }

        [Test]
        public void RegisterTwice_DifferentTier_MovesToLatestBucket()
        {
            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier3);
            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier1);

            Assert.AreEqual(1, _scheduler.TotalCount);
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier3), "旧桶不应残留条目");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier1));
        }

        [Test]
        public void BucketIndex_StaysConsistentUnderTierChurn()
        {
            // 桶索引只在 ApplyOp 一处维护：任何一条路径漏写，后续的迁移/禁用/注销就会找不到节点。
            // 先制造持续的迁桶抖动，再逐一对全部节点做禁用/启用/注销——索引一旦与桶内容脱节，
            // 这些操作就会「找不到人」，表现为计数不减（幽灵条目）或启用后回不来
            const int nodeCount = 50;
            var nodes = new CyclingTierNode[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                nodes[i] = new CyclingTierNode(i);
                _scheduler.Register(nodes[i], order: i % 3, initialTier: UpdateTier.Tier0);
            }

            float time = 0f;
            for (int frame = 0; frame < 40; frame++)
            {
                time += 0.1f;
                _scheduler.Tick(time);
            }

            Assert.AreEqual(nodeCount, _scheduler.TotalCount, "迁桶抖动中不应丢失条目");

            foreach (var node in nodes)
            {
                _scheduler.Disable(node);
            }

            Assert.AreEqual(0, _scheduler.TotalCount, "全部禁用后桶应为空");
            Assert.AreEqual(nodeCount, _scheduler.DisabledCount);

            foreach (var node in nodes)
            {
                _scheduler.Enable(node);
            }

            Assert.AreEqual(nodeCount, _scheduler.TotalCount, "全部启用后应回到桶里");
            Assert.AreEqual(0, _scheduler.DisabledCount);

            foreach (var node in nodes)
            {
                _scheduler.Unregister(node);
            }

            Assert.AreEqual(0, _scheduler.TotalCount, "注销后不应留下幽灵条目");
        }

        [Test]
        public void UnscaledNode_UsesUnscaledClock()
        {
            // 墙钟轴的节点必须拿到 unscaled 的时间基：暂停时逻辑时间冻结而墙钟继续走，
            // 用错轴就等于「暂停期间仍应运行」的逻辑在暂停期间停摆
            var scaled = new TestUpdateable { ReturnTier = UpdateTier.Tier0 };
            var unscaled = new TestUpdateable { ReturnTier = UpdateTier.Tier0 };

            _scheduler.Register(scaled, order: 0);
            _scheduler.Register(unscaled, order: 0, timeMode: UpdateTimeMode.Unscaled);

            _scheduler.Tick(new UpdateClock(time: 1.0f, unscaledTime: 10.0f));
            _scheduler.Tick(new UpdateClock(time: 1.1f, unscaledTime: 10.5f));

            Assert.AreEqual(0.1f, scaled.DeltaTimes[1], 1e-4f, "逻辑轴用 Time");
            Assert.AreEqual(1.1f, scaled.Times[1], 1e-4f);

            Assert.AreEqual(0.5f, unscaled.DeltaTimes[1], 1e-4f, "墙钟轴用 UnscaledTime");
            Assert.AreEqual(10.5f, unscaled.Times[1], 1e-4f);
        }

        [Test]
        public void ScaledAndUnscaled_AggregateInQueries()
        {
            // 两条轴分开存桶，但对外查询应是合计——否则调用方统计注册量时会漏掉一条轴
            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier2);
            _scheduler.Register(new TestUpdateable(), order: 0, initialTier: UpdateTier.Tier2,
                timeMode: UpdateTimeMode.Unscaled);

            Assert.AreEqual(2, _scheduler.TotalCount);
            Assert.AreEqual(2, _scheduler.GetCount(UpdateTier.Tier2), "查询应跨时间轴聚合");
        }

        [Test]
        public void UnscaledSlicedNode_AccumulatesUnscaledDelta()
        {
            // 每条轴有独立的节拍与切片相位：墙钟轴的周期由它自己推进，
            // 逻辑时刻在本用例里全程不动
            var node = new TestUpdateable { ReturnTier = UpdateTier.Tier3 };
            _scheduler.Register(node, order: 0, initialTier: UpdateTier.Tier3,
                timeMode: UpdateTimeMode.Unscaled);

            float unscaled = 0f;
            for (int frame = 0; frame < 8; frame++)
            {
                unscaled += FrameSeconds;
                _scheduler.Tick(new UpdateClock(time: 100f, unscaledTime: unscaled));
            }

            Assert.AreEqual(1, node.OnUpdateCallCount, "8 格内首次派发");

            for (int frame = 0; frame < 8; frame++)
            {
                unscaled += FrameSeconds;
                _scheduler.Tick(new UpdateClock(time: 100f, unscaledTime: unscaled));
            }

            Assert.AreEqual(2, node.OnUpdateCallCount, "再过 8 格派发第二次");
            Assert.AreEqual(8f * FrameSeconds, node.DeltaTimes[1], 1e-4f, "累积量取自墙钟轴");
        }

        [Test]
        public void Pause_DoesNotAdvanceSlicePhase()
        {
            // 暂停期间若照常推进节拍格，恢复后切片相位已经漂移：长周期节点会白丢一轮——
            // Tier5 在 60fps 下意味着半秒多的空窗
            var node = new TestUpdateable { ReturnTier = UpdateTier.Tier2 };
            _scheduler.Register(node, order: 0, initialTier: UpdateTier.Tier2);

            float time = 0f;
            _scheduler.Tick(time += FrameSeconds);
            Assert.AreEqual(1, node.OnUpdateCallCount, "第 1 帧轮到这个切片");

            for (int i = 0; i < 2; i++)
            {
                _scheduler.Tick(time += FrameSeconds);
            }
            Assert.AreEqual(1, node.OnUpdateCallCount, "Tier2 每 4 帧才轮到一次");

            _scheduler.Pause();
            for (int i = 0; i < 5; i++)
            {
                _scheduler.Tick(time += FrameSeconds);
            }
            Assert.AreEqual(1, node.OnUpdateCallCount, "暂停期间不派发");
            _scheduler.Resume();

            // 相位应从暂停前接续（累计格数停在 3），而不是被 5 帧暂停推走
            _scheduler.Tick(time += FrameSeconds);
            Assert.AreEqual(1, node.OnUpdateCallCount, "暂停后第 1 帧仍未轮到");

            _scheduler.Tick(time += FrameSeconds);
            Assert.AreEqual(2, node.OnUpdateCallCount, "暂停后第 2 帧才是到期点");
        }

        [Test]
        public void Resume_ReanchorsSoNoCatchUpDelta()
        {
            // 显式 Pause 不改动 Unity 时间：恢复时若不重锚，节点会拿到「整段暂停时长」的
            // delta 并试图一次补完。本调度器刻意不追赶——恢复后第一帧 delta 为 0
            var node = new TestUpdateable { ReturnTier = UpdateTier.Tier0 };
            _scheduler.Register(node, order: 0);

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, node.OnUpdateCallCount);

            _scheduler.Pause();
            for (int i = 1; i <= 10; i++)
            {
                _scheduler.Tick(time: 1.0f + i);
            }
            Assert.AreEqual(1, node.OnUpdateCallCount, "暂停期间不派发");

            _scheduler.Resume();
            _scheduler.Tick(time: 11.1f);

            Assert.AreEqual(2, node.OnUpdateCallCount);
            Assert.AreEqual(0f, node.DeltaTimes[1], 1e-4f,
                "重锚后第一帧 delta 为 0，而不是整段暂停时长（10 秒）");

            _scheduler.Tick(time: 11.2f);
            Assert.AreEqual(0.1f, node.DeltaTimes[2], 1e-4f, "恢复后第二帧起回到正常间隔");
        }

        [Test]
        public void Resume_WithoutPause_KeepsNormalDelta()
        {
            // Resume 应当幂等：没暂停过就没有「整段暂停时长」需要抹掉。否则「重复调用 Resume」
            // 这种无害写法会白丢一帧——重锚把每个条目的时间基准推到当前时刻，那次派发的 delta 成 0
            var node = new TestUpdateable { ReturnTier = UpdateTier.Tier0 };
            _scheduler.Register(node, order: 0);

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, node.OnUpdateCallCount);

            _scheduler.Resume();            // 并未处于暂停态
            _scheduler.Tick(time: 1.1f);

            Assert.AreEqual(2, node.OnUpdateCallCount);
            Assert.AreEqual(0.1f, node.DeltaTimes[1], 1e-4f,
                "未暂停时 Resume 不该重锚——这一帧应是正常间隔，而不是 0");
        }

        [Test]
        public void TimeScaleZero_FreezesScaledAxisOnly()
        {
            // timeScale = 0 时 Time.time 冻结、Time.unscaledTime 照走：
            // 逻辑轴停摆，墙钟轴（暂停菜单、UI 动画、振动到期）继续运行
            var scaled = new TestUpdateable { ReturnTier = UpdateTier.Tier0 };
            var unscaled = new TestUpdateable { ReturnTier = UpdateTier.Tier0 };
            _scheduler.Register(scaled, order: 0);
            _scheduler.Register(unscaled, order: 0, timeMode: UpdateTimeMode.Unscaled);

            _scheduler.Tick(new UpdateClock(time: 1.0f, unscaledTime: 10.0f));
            Assert.AreEqual(1, scaled.OnUpdateCallCount);
            Assert.AreEqual(1, unscaled.OnUpdateCallCount);

            for (int i = 1; i <= 5; i++)
            {
                _scheduler.Tick(new UpdateClock(time: 1.0f, unscaledTime: 10.0f + i, isPaused: true));
            }

            Assert.AreEqual(1, scaled.OnUpdateCallCount, "逻辑轴冻结");
            Assert.AreEqual(6, unscaled.OnUpdateCallCount, "墙钟轴照常");

            // 引擎时间冻结不需要重锚：Time.time 自己没走，恢复后 delta 就是一个正常帧
            _scheduler.Tick(new UpdateClock(time: 1.1f, unscaledTime: 16.0f));
            Assert.AreEqual(2, scaled.OnUpdateCallCount);
            Assert.AreEqual(0.1f, scaled.DeltaTimes[1], 1e-4f, "无追赶");
        }

        [Test]
        public void HalfTimeScale_TickFollowsEachAxisOwnTime()
        {
            // 节拍按各轴<b>自己的时间</b>走，故 timeScale = 0.5 时逻辑轴的墙钟周期翻倍：
            // 同样 32 帧里墙钟轴走了 32 格、逻辑轴只走了 16 格，于是同为 Tier3 的两个节点
            // 分别轮到 4 次与 2 次。这是「时间语义」的直接体现——旧实现按帧计数，两边都是
            // 「每 8 帧一次」，节流强度与 timeScale 无关
            var scaledNode = new TestUpdateable { ReturnTier = UpdateTier.Tier3 };
            var unscaledNode = new TestUpdateable { ReturnTier = UpdateTier.Tier3 };
            _scheduler.Register(scaledNode, order: 0, initialTier: UpdateTier.Tier3);
            _scheduler.Register(unscaledNode, order: 1, initialTier: UpdateTier.Tier3,
                timeMode: UpdateTimeMode.Unscaled);

            // 逻辑轴每帧走半格、墙钟轴每帧走一格（同一个 timeScale = 0.5 的两侧）
            float scaled = 0f;
            float unscaled = 0f;
            for (int i = 0; i < 32; i++)
            {
                scaled += FrameSeconds * 0.5f;
                unscaled += FrameSeconds;
                _scheduler.Tick(new UpdateClock(time: scaled, unscaledTime: unscaled));
            }

            Assert.AreEqual(2, scaledNode.OnUpdateCallCount,
                "逻辑轴 32 帧只走 16 格，Tier3 轮到两次");
            Assert.AreEqual(4, unscaledNode.OnUpdateCallCount,
                "墙钟轴 32 帧走了 32 格，Tier3 轮到四次");

            // 两者的 DeltaTimes[1] 相同：各自都是 8 个「自己的」节拍，差别只在墙钟上花了几帧
            Assert.AreEqual(8f * FrameSeconds, scaledNode.DeltaTimes[1], 1e-4f,
                "delta 取自逻辑时间：8 个逻辑节拍");
            Assert.AreEqual(8f * FrameSeconds, unscaledNode.DeltaTimes[1], 1e-4f,
                "delta 取自墙钟时间：8 个墙钟节拍");
        }

        [Test]
        public void HighFrameRate_KeepsTickPeriodInsteadOfScalingWithFrames()
        {
            // 旧实现按帧计节拍：144fps 下 8 个切片 = 8 帧 = 55ms，比设计意图勤 2.4 倍。
            // 新节拍按时间走，约 1 秒（60 格）里应当只轮到 8 次——与 60fps 下等长
            _node.ReturnTier = UpdateTier.Tier3;
            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier3);

            float step = FrameSeconds * (60f / 144f);   // 约 1/144 秒一帧
            float time = 0f;
            for (int i = 0; i < 144; i++)
            {
                time += step;
                _scheduler.Tick(time);
            }

            Assert.GreaterOrEqual(_node.OnUpdateCallCount, 7, "144fps 下 1 秒内应轮到 7~8 次");
            Assert.LessOrEqual(_node.OnUpdateCallCount, 8, "144fps 下 1 秒内应轮到 7~8 次");

            float averageInterval = (_node.Times[_node.OnUpdateCallCount - 1] - _node.Times[0])
                / (_node.OnUpdateCallCount - 1);
            Assert.AreEqual(8f * UpdateScheduler.TickPeriod, averageInterval, 0.004f,
                "平均间隔应是 8 格（约 133ms），而不是 8 帧（约 55ms）");
        }

        [Test]
        public void LowFrameRate_CatchesUpTwoTicksPerFrame()
        {
            // 30fps 一帧抵三格多的时长，节拍按时间走就必须每帧补两格，否则周期会被拉长一倍。
            // 铺满 32 个切片格后，「总派发次数」即「总推进格数」
            var nodes = RegisterOnePerSlice();

            float step = FrameSeconds * 2f;             // 约 1/30 秒一帧
            float time = 0f;
            _scheduler.Tick(time += step);              // 首帧只锚定，基准线取在它之后
            int baseline = DispatchedCount(nodes);

            for (int i = 0; i < 60; i++)
            {
                _scheduler.Tick(time += step);
            }

            Assert.AreEqual(120, DispatchedCount(nodes) - baseline,
                "60 帧应恰好推进 120 格（每帧 2 格）——夹紧规则若把余量一并销毁会系统性丢格");
        }

        [Test]
        public void Hitch_DoesNotBankTicksForLaterFrames()
        {
            // 一帧卡了 1 秒：补格上限挡住一次补满，且攒下的整格债务被丢弃而不是摊到后续帧
            var nodes = RegisterOnePerSlice();

            float time = 0f;
            _scheduler.Tick(time += FrameSeconds);      // 首帧只锚定
            int baseline = DispatchedCount(nodes);

            _scheduler.Tick(time += 1.0f);              // 卡顿帧
            Assert.AreEqual(3, DispatchedCount(nodes) - baseline,
                "卡顿帧补到上限即止，不一次补满（1 秒攒下 60 格，只放行 3 格）");

            baseline = DispatchedCount(nodes);
            for (int i = 0; i < 60; i++)
            {
                _scheduler.Tick(time += FrameSeconds);
            }

            // 60~61 而非 120：卡顿攒下的「整格」债务被丢弃，只保留不足一格的余量——余量本身是
            // 合法的切片相位（丢掉它才是缺陷，30fps 下会系统性丢格），因此后续至多出现一次
            // 「余量凑满一格」的双格帧。若这里超过 61，说明整格债务被摊到了后续帧（追赶）
            int after = DispatchedCount(nodes) - baseline;
            Assert.GreaterOrEqual(after, 60, "卡顿后的 60 帧不得少跑");
            Assert.LessOrEqual(after, 61, "卡顿后的整格债务必须丢弃，否则就是雪崩");
        }

        [Test]
        public void LowFrameRate_Tier1DoesNotOutrunTier0()
        {
            // 每帧档在补格循环之外（每帧一次），切片档在循环之内（每格一次）——帧长超过一格后
            // 每帧会补多格，档位次序就可能反转：20fps 下每帧恰好 3 格，Tier1（2 格一轮）于是
            // 每帧被轮到约 1.5 次，比 Tier0 的每帧一次还频繁。梯子在最吃紧的设备上倒挂
            var tier0 = new TestUpdateable();
            var tier1 = new TestUpdateable { ReturnTier = UpdateTier.Tier1 };
            _scheduler.Register(tier0, order: 0);
            _scheduler.Register(tier1, order: 1, initialTier: UpdateTier.Tier1);

            const float slowFrameSeconds = 1f / 20f;    // 恰好 3 格
            float time = 0f;
            for (int i = 0; i < 60; i++)
            {
                _scheduler.Tick(time += slowFrameSeconds);
            }

            Assert.AreEqual(60, tier0.OnUpdateCallCount, "Tier0 每帧一次，60 帧即 60 次");
            Assert.AreEqual(tier0.OnUpdateCallCount, tier1.OnUpdateCallCount,
                $"Tier1 不得比 Tier0 更频繁（实测 Tier1={tier1.OnUpdateCallCount}、Tier0={tier0.OnUpdateCallCount}）");
        }

        [Test]
        public void Hitch_Tier1IsNotDispatchedTwiceInOneFrame()
        {
            // 同一帧内的多格共用同一个时刻，所以一帧里被轮到两次的节点，第二次的 delta 必然是 0
            // （now - LastUpdateTime，而 LastUpdateTime 刚被推成 now）。卡顿帧一次补 3 格，Tier1
            // 的切片相位只有 2 个，必然有节点被轮两次
            var nodes = new TestUpdateable[4];          // 下标 0~3，两种切片相位都覆盖
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = new TestUpdateable { ReturnTier = UpdateTier.Tier1 };
                _scheduler.Register(nodes[i], order: i, initialTier: UpdateTier.Tier1);
            }

            float time = 0f;
            for (int i = 0; i < 4; i++)
            {
                _scheduler.Tick(time += FrameSeconds);  // 先正常走几帧，让相位离开起点
            }

            var before = new int[nodes.Length];
            var beforeDeltaCount = new int[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                before[i] = nodes[i].OnUpdateCallCount;
                beforeDeltaCount[i] = nodes[i].DeltaTimes.Count;
            }

            _scheduler.Tick(time += 1.0f);              // 卡顿帧：补到上限 3 格

            for (int i = 0; i < nodes.Length; i++)
            {
                var recorded = nodes[i].DeltaTimes.GetRange(
                    beforeDeltaCount[i], nodes[i].DeltaTimes.Count - beforeDeltaCount[i]);

                Assert.AreEqual(before[i] + 1, nodes[i].OnUpdateCallCount,
                    $"卡顿帧内同一节点只应被派发一次。本帧实测 delta 序列: {string.Join(", ", recorded)}");
            }
        }

        [Test]
        public void SameTier_SameDispatchCountAcrossFrameRates()
        {
            // 旧实现里同一档位在 60fps 与 144fps 下差 2.4 倍；新节拍下二者应当一致
            int at60 = CountDispatchesInOneSecond(60);
            int at144 = CountDispatchesInOneSecond(144);

            Assert.AreEqual(at60, at144, "同一档位在 60fps 与 144fps 下 1 秒内的派发次数应相同");
            Assert.GreaterOrEqual(at60, 7);
            Assert.LessOrEqual(at60, 8);
        }

        [Test]
        public void Pause_DoesNotBankTicksForResume()
        {
            // 显式暂停期间逻辑时间仍在流逝。若冻结分支不前推时间基准，恢复首帧会拿到
            // 「整段暂停时长」并一路补到上限——等价于追赶，节点会突然连跑两格
            var nodes = RegisterOnePerSlice();

            float time = 0f;
            _scheduler.Tick(time += FrameSeconds);      // 首帧只锚定
            int baseline = DispatchedCount(nodes);

            _scheduler.Pause();
            for (int i = 0; i < 5; i++)
            {
                _scheduler.Tick(time += 1.0f);           // 暂停期间 5 秒照常流逝
            }
            Assert.AreEqual(0, DispatchedCount(nodes) - baseline, "暂停期间不派发");

            _scheduler.Resume();
            _scheduler.Tick(time += FrameSeconds);

            Assert.AreEqual(1, DispatchedCount(nodes) - baseline,
                "恢复首帧恰好推进一格，而不是把暂停期间攒下的格一次补出来");
        }

        [Test]
        public void FirstTick_LandsOnSliceZero()
        {
            // 新实例的「上一帧时刻」是 0 而时刻早已不是 0：若不做首帧锚定，首次 Tick 会把
            // 「会话已运行时长」算进间隔，相位直接跳过第 0 格——这里表现为两格同时被派发
            var nodes = RegisterOnePerSlice();

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, nodes[0].OnUpdateCallCount, "第 0 格应是首个被派发的");
            for (int i = 1; i < nodes.Length; i++)
            {
                Assert.AreEqual(0, nodes[i].OnUpdateCallCount, $"第 {i} 格不该在首帧被派发");
            }
        }

        [Test]
        public void FixedTiming_AdvancesExactlyOneTickPerStep()
        {
            // 固定步长本就等长（默认 0.02s），没有漂移可修；若共用墙钟累加器，50Hz 的固定步
            // 会被派成 60Hz 的节奏（每 5 步出现一次 2 格），「每 2^k 个固定步」的语义随之改变
            var fixedScheduler = new UpdateScheduler(UpdateTiming.FixedUpdate);
            var node = new FixedUpdateNode { NextTier = UpdateTier.Tier3 };
            fixedScheduler.Register(node, order: 0, initialTier: UpdateTier.Tier3);

            float time = 0f;
            for (int i = 0; i < 8; i++)
            {
                time += 0.02f;
                fixedScheduler.Tick(time);
            }

            Assert.AreEqual(1, node.OnFixedUpdateCallCount,
                "8 个固定步恰好一格：第 9 步才轮到第二次");

            fixedScheduler.Clear();
        }

        [Test]
        public void ThirtyFpsWithJitter_KeepsTickRate()
        {
            // 30fps 是支持的底线，而真实帧长总在均值附近抖动。补格上限若恰好压在 30fps
            // （33.3ms）上，抖动会让个别帧「应补 3 格」而被截断，且截断只砍多、不补少，
            // 于是系统性欠跑——上限取 3 把精确边界推到 50ms，才容得下抖动
            var nodes = RegisterOnePerSlice();

            float time = 0f;
            _scheduler.Tick(time += FrameSeconds);
            int baseline = DispatchedCount(nodes);

            for (int i = 0; i < 60; i++)
            {
                // 帧长在 33.3ms 上下交替 ±5%（31.7ms / 35ms），均值仍是 30fps
                time += FrameSeconds * (i % 2 == 0 ? 1.9f : 2.1f);
                _scheduler.Tick(time);
            }

            Assert.AreEqual(120, DispatchedCount(nodes) - baseline,
                "均值 30fps 下 60 帧应推进 120 格——上限不足会单边截断");
        }

        [Test]
        public void LongSession_StillAdvancesOneTickPerFrame()
        {
            // 会话跑到几十小时后，float 时刻的分辨率会逼近一格（Time.time 到 27 小时时 ULP
            // 约 7.8ms）：逐帧增量随之在「不足一格」与「超过一格」之间跳，补格数退化成 0/2
            // 交替。时钟改用 double 后，这个量级下仍应每帧恰好一格
            var nodes = RegisterOnePerSlice();

            const double sessionStart = 100000d;        // 约 27.8 小时
            double time = sessionStart;
            _scheduler.Tick(new UpdateClock(time, time));   // 首帧只锚定
            int baseline = DispatchedCount(nodes);

            for (int i = 0; i < 60; i++)
            {
                time += FrameSeconds;
                _scheduler.Tick(new UpdateClock(time, time));

                Assert.AreEqual(i + 1, DispatchedCount(nodes) - baseline,
                    $"第 {i + 1} 帧应恰好推进一格");
            }
        }

        [Test]
        public void LongSession_NodeDeltaHasNoFloatCancellation()
        {
            // 与上一条同源，但查的是另一条路径：补格判定已用 double，而逐节点 delta 至今是拿
            // 「两个各带 ±ULP/2 量化误差的大 float」相减——会话越长，量化台阶越接近一帧，
            // 差值就在相邻台阶之间跳。27.8 小时时 float 的 ULP ≈ 7.8ms，而帧步长只有 16.7ms
            // （约 2.1 个 ULP），于是 delta 会在 2 个 ULP 与 3 个 ULP 之间交替
            _scheduler.Register(_node, order: 0);   // Tier0：每帧派发，delta 应恒为帧步长

            const double sessionStart = 100000d;    // 约 27.8 小时
            double time = sessionStart;
            _scheduler.Tick(new UpdateClock(time, time));   // 首帧：锚定轴 + 节点首次派发（delta = 0）

            for (int i = 0; i < 60; i++)
            {
                time += FrameSeconds;
                _scheduler.Tick(new UpdateClock(time, time));
            }

            Assert.AreEqual(61, _node.DeltaTimes.Count, "首帧记 0，其后 60 帧各一次");

            for (int i = 1; i < _node.DeltaTimes.Count; i++)
            {
                Assert.AreEqual(FrameSeconds, _node.DeltaTimes[i], 1e-5f,
                    $"第 {i} 帧的 delta 应等于帧步长。实测序列: {string.Join(", ", _node.DeltaTimes)}");
            }
        }

        [Test]
        public void FirstDispatchAfterRegister_HasZeroDelta()
        {
            // 注册时不该由调度器自己去猜「现在几点」：驱动方给的时间轴未必是 Unity 的 Time.time
            // （测试与确定性回放都自带时刻），而 timeScale != 1 时墙钟轴与逻辑时间还差着截距。
            // 猜错就是一次成片的假 delta——两个轴都用远大于真实 Time.time 的时刻来暴露它
            var scaledNode = new TestUpdateable { ReturnTier = UpdateTier.Tier0 };
            var unscaledNode = new TestUpdateable { ReturnTier = UpdateTier.Tier0 };

            _scheduler.Tick(new UpdateClock(time: 100f, unscaledTime: 10000f));   // 首帧只锚定
            _scheduler.Register(scaledNode, order: 0);
            _scheduler.Register(unscaledNode, order: 1, timeMode: UpdateTimeMode.Unscaled);

            _scheduler.Tick(new UpdateClock(time: 100.1f, unscaledTime: 10000.1f));

            Assert.AreEqual(1, scaledNode.OnUpdateCallCount);
            Assert.AreEqual(1, unscaledNode.OnUpdateCallCount);
            Assert.AreEqual(0f, scaledNode.DeltaTimes[0], 1e-4f, "逻辑轴首次派发的 delta 应为 0");
            Assert.AreEqual(0f, unscaledNode.DeltaTimes[0], 1e-4f, "墙钟轴首次派发的 delta 应为 0");
        }

        [Test]
        public void FirstDispatchAfterEnable_HasZeroDelta()
        {
            // 与注册同理：禁用期间累积的间隔不该算进本次 delta，而调度器同样无从知道
            // 「重新启用那一刻」的轴时刻。用被禁用掉的 49 秒来暴露猜测式的锚点
            _scheduler.Register(_node, order: 0);
            _scheduler.Tick(new UpdateClock(time: 1f, unscaledTime: 1f));
            Assert.AreEqual(1, _node.OnUpdateCallCount);

            _scheduler.Disable(_node);
            _scheduler.Tick(new UpdateClock(time: 50f, unscaledTime: 50f));
            Assert.AreEqual(1, _node.OnUpdateCallCount, "禁用期间不派发");

            _scheduler.Enable(_node);
            _scheduler.Tick(new UpdateClock(time: 100f, unscaledTime: 100f));

            Assert.AreEqual(2, _node.OnUpdateCallCount);
            Assert.AreEqual(0f, _node.DeltaTimes[1], 1e-4f, "重新启用后首次派发的 delta 应为 0");
        }

        [Test]
        public void LongPeriodTiers_HaveDoublingPeriods()
        {
            // 长周期档位让「每秒一次」这类需求能直接表达：旧阶梯封顶在 32 格，而它在 30fps 下
            // 是 1.07 秒、144fps 下只有 222ms，两头都不是作者想要的那个时长
            var tier6 = new TestUpdateable { ReturnTier = UpdateTier.Tier6 };
            var tier7 = new TestUpdateable { ReturnTier = UpdateTier.Tier7 };
            _scheduler.Register(tier6, order: 0, initialTier: UpdateTier.Tier6);
            _scheduler.Register(tier7, order: 1, initialTier: UpdateTier.Tier7);

            float time = 0f;
            for (int i = 0; i < 128; i++)
            {
                _scheduler.Tick(time += FrameSeconds);
            }

            // 128 格窗口内：Tier6（64 格）在第 0、64 格各一次，Tier7（128 格）只在第 0 格
            Assert.AreEqual(2, tier6.OnUpdateCallCount, "Tier6 每 64 格轮到一次");
            Assert.AreEqual(1, tier7.OnUpdateCallCount, "Tier7 每 128 格轮到一次");

            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier6));
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier7));
        }

        /// <summary>
        /// 在 Tier5 桶里铺满 32 个节点（下标 0~31 各占一格），使「推进了几格」可以直接从
        /// 派发次数读出来——每格恰好派发一个节点。节点返回 Tier5，故不会迁桶。
        /// </summary>
        private TestUpdateable[] RegisterOnePerSlice()
        {
            var nodes = new TestUpdateable[32];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = new TestUpdateable { ReturnTier = UpdateTier.Tier5 };
                _scheduler.Register(nodes[i], order: 0, initialTier: UpdateTier.Tier5);
            }
            return nodes;
        }

        /// <summary>统计一组节点的派发次数合计。</summary>
        private static int DispatchedCount(TestUpdateable[] nodes)
        {
            int total = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                total += nodes[i].OnUpdateCallCount;
            }
            return total;
        }

        /// <summary>
        /// 用一个独立调度器把 Tier3 节点驱动约 1 秒，返回派发次数。
        /// <para>调度器自身不持有静态状态，故这里的局部实例无需登记到 fixture 的清理里。</para>
        /// </summary>
        /// <param name="fps">驱动帧率。</param>
        private static int CountDispatchesInOneSecond(int fps)
        {
            var scheduler = new UpdateScheduler();
            var node = new TestUpdateable { ReturnTier = UpdateTier.Tier3 };
            scheduler.Register(node, order: 0, initialTier: UpdateTier.Tier3);

            float step = FrameSeconds * (60f / fps);
            float time = 0f;
            for (int i = 0; i < fps; i++)
            {
                time += step;
                scheduler.Tick(time);
            }

            int count = node.OnUpdateCallCount;
            scheduler.Clear();
            return count;
        }

        [Test]
        public void NegativeDelta_IsClampedToZero()
        {
            // timeScale < 0（倒放）时 Time.time 会倒着走：负 delta 会让
            // 「位置 += 速度 × delta」反向积分
            var node = new TestUpdateable { ReturnTier = UpdateTier.Tier0 };
            _scheduler.Register(node, order: 0);

            _scheduler.Tick(time: 5.0f);
            _scheduler.Tick(time: 4.0f);

            Assert.AreEqual(0f, node.DeltaTimes[1], 1e-4f, "负间隔钳制为 0");
            Assert.AreEqual(4.0f, node.Times[1], 1e-4f, "时刻基准照常前进，否则倒放段会被重复计入");
        }

        [Test]
        public void FixedTiming_DispatchesOnFixedUpdate()
        {
            // 固定步长时机走 OnFixedUpdate，且不该碰 OnUpdate——两套时机各调各的方法
            var fixedScheduler = new UpdateScheduler(UpdateTiming.FixedUpdate);
            var node = new FixedUpdateNode();
            fixedScheduler.Register(node, order: 0);

            fixedScheduler.Tick(0.02f);
            fixedScheduler.Tick(0.04f);

            Assert.AreEqual(2, node.OnFixedUpdateCallCount);
            Assert.AreEqual(0, node.OnUpdateCallCount);

            fixedScheduler.Clear();
        }

        [Test]
        public void OnUpdate_ReturnsDifferentTier_MovesBucket()
        {
            _scheduler.Register(_node, order: 0);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier0));
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier3));

            // Move to Tier3
            _node.ReturnTier = UpdateTier.Tier3;
            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier0));
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier3));
        }

        [Test]
        public void Enable_TriggersOnEnable_ResumesUpdate()
        {
            _scheduler.Register(_node, order: 0);
            Assert.AreEqual(1, _node.OnEnableCallCount, "注册即宣告一次启用（进入派发集合）");

            _scheduler.Disable(_node);
            Assert.AreEqual(1, _node.OnDisableCallCount);
            Assert.IsFalse(_scheduler.IsEnabled(_node));

            _scheduler.Enable(_node);
            Assert.AreEqual(2, _node.OnEnableCallCount, "重新进入派发集合，再宣告一次");
            Assert.IsTrue(_scheduler.IsEnabled(_node));

            // After enable, node should receive updates
            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, _node.OnUpdateCallCount);
        }

        [Test]
        public void Disable_TriggersOnDisable_StopsUpdate()
        {
            _scheduler.Register(_node, order: 0);
            _scheduler.Disable(_node);

            Assert.AreEqual(1, _node.OnDisableCallCount);
            Assert.IsFalse(_scheduler.IsEnabled(_node));

            // Disabled node should not be ticked
            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(0, _node.OnUpdateCallCount);
        }

        [Test]
        public void Enable_ReturnsToDeclaredTier()
        {
            // 档位是注册时声明的设计决定，一次启停不该把它清掉。旧实现让条目回 Tier0 桶，
            // 于是 Tier5 的后台同步在启用后会先每帧跑一轮——而桶号即档位，进禁用表时它就丢了
            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier3);
            // 返回值是第二条档位通道：节点自己也该返回声明的档位，否则首次派发后它就被拉回 Tier0，
            // 本用例就测不到「档位是否被记住」了
            _node.ReturnTier = UpdateTier.Tier3;
            _scheduler.Disable(_node);
            _scheduler.Enable(_node);

            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier3), "启用后应回到声明的档位");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier0), "而不是一律回 Tier0");

            // Tier3 每 8 格一轮：8 格内恰好派发一次（档位真的在管节拍，不只是记了个数）
            float time = 0f;
            for (int i = 0; i < 8; i++)
            {
                _scheduler.Tick(time += FrameSeconds);
            }
            Assert.AreEqual(1, _node.OnUpdateCallCount, "Tier3 的节拍是 8 格一次");
            Assert.AreEqual(0f, _node.DeltaTimes[0], 1e-6f, "首次派发仍按锚定规则记 0");
        }

        [Test]
        public void Enable_AfterTierMigration_ReturnsToLatestTier()
        {
            // 档位字段必须随迁移同步，否则一次启停会把节点拉回迁移前的旧档位——
            // 比「回到 Tier0」更隐蔽：档位看起来还是被记住的，只是记的是过期的那份
            _scheduler.Register(_node, order: 0);
            _node.ReturnTier = UpdateTier.Tier2;

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier2), "返回值把节点迁到了 Tier2");

            _scheduler.Disable(_node);
            _scheduler.Enable(_node);

            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier2), "应回到迁移后的档位，而不是注册时的 Tier0");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier0));
        }

        [Test]
        public void RegisterWhileDisabled_UpdatesTimeMode()
        {
            // 文档给出的改轴手段是「先注销再重新注册」；对禁用中的对象，重新注册若只刷新排序号，
            // 这条就静默无效——节点仍留在旧轴上，而按时间轴选择的语义（暂停期间是否运行）就错了
            _scheduler.Register(_node, order: 0);
            _scheduler.Disable(_node);

            _scheduler.Register(_node, order: 0, timeMode: UpdateTimeMode.Unscaled);
            _scheduler.Enable(_node);

            _scheduler.Tick(new UpdateClock(time: 1.0, unscaledTime: 100.0));

            Assert.AreEqual(1, _node.OnUpdateCallCount);
            Assert.AreEqual(100f, _node.Times[0], 1e-6f, "应按新轴取时刻（墙钟轴），而不是旧轴");
        }

        [Test]
        public void LifecycleStream_AlternatesStartingWithEnable()
        {
            // 每套调度器上的生命周期回调严格交替、且以 OnEnable 起头。这是「节点按配对计数判断
            // 自己是否已完全停用」这条朴素写法成立的前提——旧实现在注册时不宣告，节点收到的
            // 第一条回调是没有配对的 OnDisable，计数会走到 -1
            _scheduler.Register(_node, order: 0);
            Assert.AreEqual(1, _node.OnEnableCallCount, "注册即宣告启用，而不是等第一次 Disable");
            Assert.AreEqual(0, _node.OnDisableCallCount);

            _scheduler.Disable(_node);
            Assert.AreEqual(1, _node.OnEnableCallCount);
            Assert.AreEqual(1, _node.OnDisableCallCount);

            _scheduler.Enable(_node);
            Assert.AreEqual(2, _node.OnEnableCallCount);
            Assert.AreEqual(1, _node.OnDisableCallCount);

            _scheduler.Disable(_node);
            Assert.AreEqual(2, _node.OnEnableCallCount);
            Assert.AreEqual(2, _node.OnDisableCallCount, "两次启用配两次停用");
        }

        [Test]
        public void RegisterTwice_DoesNotAnnounceTwice()
        {
            // 重新注册（换档位/轴/排序号）时节点本来就在集合里，再宣告一次会吐出没有 OnDisable
            // 配对的连续两次 OnEnable，把配对计数打乱
            _scheduler.Register(_node, order: 0);
            _scheduler.Register(_node, order: 5, initialTier: UpdateTier.Tier3);

            Assert.AreEqual(1, _node.OnEnableCallCount, "重新注册不重复宣告");
            Assert.AreEqual(1, _scheduler.TotalCount, "仍然只有一条条目");
        }

        [Test]
        public void RegisterWhileDisabled_DoesNotAnnounce()
        {
            // 禁用态下重新注册只刷新归位信息，节点仍是禁用态——宣告启用就是谎报
            _scheduler.Register(_node, order: 0);
            _scheduler.Disable(_node);
            Assert.AreEqual(1, _node.OnDisableCallCount);

            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier2);

            Assert.AreEqual(1, _node.OnEnableCallCount, "仍是禁用态，不宣告启用");
            Assert.AreEqual(1, _scheduler.DisabledCount);
            Assert.IsFalse(_scheduler.IsEnabled(_node));
        }

        [Test]
        public void Register_DuringDispatch_AnnouncementFollowsTheBuffer()
        {
            // 派发中注册 → 宣告随缓冲在收尾时发生。提前回调等于在别人的回调栈里跑用户代码，
            // 那正是 Disable/Enable 一直避免的事
            var late = new TestUpdateable();
            bool announcedBeforeFrameEnd = false;
            var caller = new ScriptedNode(_scheduler);
            caller.Script = (scheduler, self) =>
            {
                scheduler.Register(late, order: 1);
                announcedBeforeFrameEnd = late.OnEnableCallCount > 0;
            };
            _scheduler.Register(caller, order: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.IsFalse(announcedBeforeFrameEnd, "宣告不得早于帧末");
            Assert.AreEqual(1, late.OnEnableCallCount, "收尾时补上");
            Assert.AreEqual(0, late.OnUpdateCallCount, "被缓冲意味着本帧不派发");
        }

        [Test]
        public void Disable_TwiceWithoutEnable_FiresOnce()
        {
            // 第二次 Disable 没有表迁移（节点已在禁用表里），因此不回调
            _scheduler.Register(_node, order: 0);
            _scheduler.Disable(_node);
            _scheduler.Disable(_node);

            Assert.AreEqual(1, _node.OnDisableCallCount);
            Assert.AreEqual(1, _node.OnEnableCallCount);
        }

        [Test]
        public void IsEnabled_ReturnsCorrectStatus()
        {
            _scheduler.Register(_node, order: 0);
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
        public void ProcessImmediate_ExecutesAndAdjustsTier()
        {
            _scheduler.Register(_node, order: 0);
            _node.ReturnTier = UpdateTier.Tier4;

            _scheduler.ProcessImmediate(_node, deltaTime: 0.5f, time: 10.0f);

            Assert.AreEqual(1, _node.OnUpdateCallCount);
            Assert.AreEqual(0.5f, _node.DeltaTimes[0], 1e-6f);
            Assert.AreEqual(10.0f, _node.Times[0], 1e-6f);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier4));
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier0));
        }

        [Test]
        public void ProcessImmediate_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _scheduler.ProcessImmediate(null, 0f, 0f));
        }

        [Test]
        public void Clear_EmptiesAllBuckets()
        {
            _scheduler.Register(_node, order: 0);
            var node2 = new TestUpdateable();
            _scheduler.Register(node2, order: 1);

            _scheduler.Disable(node2);
            Assert.AreEqual(1, _scheduler.DisabledCount);

            _scheduler.Clear();
            Assert.AreEqual(0, _scheduler.TotalCount);
            Assert.AreEqual(0, _scheduler.DisabledCount);
        }

        [Test]
        public void Clear_DoesNotInvokeLifecycleCallbacks()
        {
            // 与 Unregister 一致：Clear 的主要使用者是测试隔离，在隔离点触发用户回调会让 fixture
            // 的收尾去执行业务代码——那里往往引用了已拆掉的管理器
            var disabled = new TestUpdateable();
            _scheduler.Register(_node, order: 0);
            _scheduler.Register(disabled, order: 1);
            _scheduler.Disable(disabled);
            Assert.AreEqual(1, disabled.OnDisableCallCount, "前提：Disable 本身是会回调的");

            // 基线在 Clear 之前取：注册本身会宣告一次 OnEnable，不能用 0 当基线
            int enableBefore = _node.OnEnableCallCount;
            int disableBefore = _node.OnDisableCallCount;

            _scheduler.Clear();

            Assert.AreEqual(disableBefore, _node.OnDisableCallCount, "桶里的条目不被回调");
            Assert.AreEqual(1, disabled.OnDisableCallCount, "禁用表里的条目同样不被回调");
            Assert.AreEqual(enableBefore, _node.OnEnableCallCount, "也不会反向回调 OnEnable");
        }

        [Test]
        public void LateUpdateTiming_DispatchesOnLateUpdate()
        {
            // 时机由调度器实例固定，条目只持有 IUpdateLifecycle，派发时按实例转型调用。这条路径此前
            // 只在 PlayerLoop 集成用例里被覆盖（那边验的是次序），这里把「同一对象在两个时机上各自
            // 被转发到对应方法」钉在调度器级
            var lateScheduler = new UpdateScheduler(UpdateTiming.LateUpdate);
            var node = new BothTimingsCounter();
            lateScheduler.Register(node, order: 0);

            lateScheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, node.LateCount, "LateUpdate 时机应调用 OnLateUpdate");
            Assert.AreEqual(0, node.UpdateCount, "不该走 OnUpdate");
            Assert.AreEqual(0, node.FixedCount);

            var updateScheduler = new UpdateScheduler(UpdateTiming.Update);
            updateScheduler.Register(node, order: 0);
            updateScheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, node.UpdateCount, "Update 时机应调用 OnUpdate");
            Assert.AreEqual(1, node.LateCount, "不能串到另一个时机的方法上");
        }

        [Test]
        public void OutOfRangeTier_IsClampedToBuckets()
        {
            // 档位由节点返回值给出，越界值必须被钳进 [Tier0, Max]——否则就是桶下标越界。
            // 高档位落在最大档、负档位落在 Tier0，两条都不丢条目
            var tooHigh = new TestUpdateable { ReturnTier = (UpdateTier)99 };
            var tooLow = new TestUpdateable { ReturnTier = (UpdateTier)(-3) };
            _scheduler.Register(tooHigh, order: 0);
            _scheduler.Register(tooLow, order: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(2, _scheduler.TotalCount, "两条条目都还在");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Max), "越界的高档位钳到最大档");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier0), "负档位钳到 Tier0");
        }

        [Test]
        public void Tick_Exception_UnregistersNode()
        {
            _scheduler.Register(_node, order: 0);
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
        public void DisableDuringTick_OnDisableThrows_RestOfFrameStillApplies()
        {
            // 生命周期回调抛异常曾直接穿出 FlushPending —— 循环中断且缓冲不清空，于是排在后面的
            // 操作本帧全部失效；而下一帧是「先派发、后 flush」，已请求禁用的节点会再多派发一次，
            // 当帧 IsEnabled 又仍报「未禁用」（与 README 的承诺相反）
            var thrower = new TestUpdateable { ThrowOnDisable = true };
            var victim = new TestUpdateable();
            var caller = new ScriptedNode(_scheduler);
            caller.Script = (scheduler, self) =>
            {
                scheduler.Disable(thrower);
                scheduler.Disable(victim);
            };

            _scheduler.Register(caller, order: 0);
            _scheduler.Register(thrower, order: 0);
            _scheduler.Register(victim, order: 0);

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[UpdateScheduler] TestUpdateable.OnDisable threw exception: System.Exception: Test exception")));

            Assert.DoesNotThrow(() => _scheduler.Tick(time: 1.0f),
                "用户回调的异常应被隔离——它不该从 Tick 里抛出去打断整个调度");

            Assert.AreEqual(2, _scheduler.DisabledCount, "排在抛异常那条之后的操作同样要在本帧落地");
            Assert.IsFalse(_scheduler.IsEnabled(thrower));
            Assert.IsFalse(_scheduler.IsEnabled(victim), "IsEnabled 必须与帧末状态一致");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, thrower.OnUpdateCallCount, "已禁用：只在第一帧被派发过一次");
            Assert.AreEqual(1, victim.OnUpdateCallCount);
        }

        [Test]
        public void EnableDuringTick_OnEnableThrows_RestOfFrameStillApplies()
        {
            // 与上一条对称，覆盖 Enable 那条回调路径。
            // 先注册再打开 ThrowOnEnable：注册也会宣告一次 OnEnable，若此时就会抛，抛点会落在
            // LogAssert.Expect 之前（那不是本用例要覆盖的那条路径）
            var thrower = new TestUpdateable();
            var victim = new TestUpdateable();
            _scheduler.Register(thrower, order: 0);
            _scheduler.Register(victim, order: 0);
            thrower.ThrowOnEnable = true;
            _scheduler.Disable(thrower);
            _scheduler.Disable(victim);

            var caller = new ScriptedNode(_scheduler);
            caller.Script = (scheduler, self) =>
            {
                scheduler.Enable(thrower);
                scheduler.Enable(victim);
            };
            _scheduler.Register(caller, order: 0);

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[UpdateScheduler] TestUpdateable.OnEnable threw exception: System.Exception: Test exception")));

            Assert.DoesNotThrow(() => _scheduler.Tick(time: 1.0f));

            Assert.AreEqual(3, _scheduler.TotalCount, "两条启用都要落地，抛异常的那条也不例外（caller + 两个被启用者）");
            Assert.AreEqual(0, _scheduler.DisabledCount);
            Assert.IsTrue(_scheduler.IsEnabled(thrower), "启用已生效，异常只影响回调本身");
            Assert.IsTrue(_scheduler.IsEnabled(victim));

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, thrower.OnUpdateCallCount, "启用后的下一帧开始参与派发");
            Assert.AreEqual(1, victim.OnUpdateCallCount);
        }

        [Test]
        public void Order_InsertSorted_MaintainsOrder()
        {
            var log = new List<string>(3);

            _scheduler.Register(new OrderRecorder(log, "o3"), order: 3);
            _scheduler.Register(new OrderRecorder(log, "o0"), order: 0);
            _scheduler.Register(new OrderRecorder(log, "o1"), order: 1);

            _scheduler.Tick(time: 1.0f);

            CollectionAssert.AreEqual(new[] { "o0", "o1", "o3" }, log,
                "Tier0 桶每帧全量派发，次序即 order 升序");
        }

        [Test]
        public void Order_SameValue_FollowsRegistrationOrder()
        {
            var log = new List<string>(3);

            _scheduler.Register(new OrderRecorder(log, "a"), order: 0);
            _scheduler.Register(new OrderRecorder(log, "b"), order: 0);
            _scheduler.Register(new OrderRecorder(log, "c"), order: 0);

            _scheduler.Tick(time: 1.0f);

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, log, "同 order 时按注册先后");
        }

        [Test]
        public void Order_SmallerValueRegisteredLast_RunsFirst()
        {
            var log = new List<string>(2);

            _scheduler.Register(new OrderRecorder(log, "o5"), order: 5);
            _scheduler.Register(new OrderRecorder(log, "o0"), order: 0);

            _scheduler.Tick(time: 1.0f);

            CollectionAssert.AreEqual(new[] { "o0", "o5" }, log, "order 优先于注册先后");
        }

        [Test]
        public void GetCount_ReturnsCorrectCount()
        {
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier0));

            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier0);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier0));

            _scheduler.Register(new TestUpdateable(), order: 0, initialTier: UpdateTier.Tier3);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier3));
        }

        [Test]
        public void Register_WithInitialTier_UsesCorrectBucket()
        {
            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier5);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateTier.Tier5));
            Assert.AreEqual(0, _scheduler.GetCount(UpdateTier.Tier0));
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
        public void TotalCount_ReturnsSumOfAllTiers()
        {
            Assert.AreEqual(0, _scheduler.TotalCount);

            _scheduler.Register(_node, order: 0, initialTier: UpdateTier.Tier0);
            _scheduler.Register(new TestUpdateable(), order: 0, initialTier: UpdateTier.Tier1);
            _scheduler.Register(new TestUpdateable(), order: 0, initialTier: UpdateTier.Tier3);

            Assert.AreEqual(3, _scheduler.TotalCount);
        }

        /// <summary>
        /// Test helper that registers another node during OnUpdate.
        /// </summary>
        private sealed class RegistratorNode : IUpdateable
        {
            private readonly UpdateScheduler _scheduler;
            private readonly IUpdateable _target;
            private readonly int _order;
            private bool _hasRegistered;

            public int OnUpdateCallCount { get; private set; }

            public RegistratorNode(UpdateScheduler scheduler, IUpdateable target, int order)
            {
                _scheduler = scheduler;
                _target = target;
                _order = order;
            }

            public void OnEnable() { }
            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                if (!_hasRegistered)
                {
                    _hasRegistered = true;
                    _scheduler.Register(_target, _order);
                }
                return UpdateTier.Tier0;
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

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                if (!_hasUnregistered)
                {
                    _hasUnregistered = true;
                    _scheduler.Unregister(_target);
                }
                return UpdateTier.Tier0;
            }
        }

        /// <summary>
        /// 同时实现两个时机接口的测试替身，用于确认各自只回调自己那个方法。
        /// </summary>
        private sealed class FixedUpdateNode : IUpdateable, IFixedUpdateable
        {
            /// <summary>下一次派发返回的档位。默认 Tier0，即留在每帧桶。</summary>
            public UpdateTier NextTier { get; set; } = UpdateTier.Tier0;

            public int OnUpdateCallCount { get; private set; }
            public int OnFixedUpdateCallCount { get; private set; }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                return UpdateTier.Tier0;
            }

            public UpdateTier OnFixedUpdate(float deltaTime, float fixedTime)
            {
                OnFixedUpdateCallCount++;
                return NextTier;
            }
        }

        /// <summary>
        /// 每次派发都换一个档位的测试替身，用于制造持续的迁桶抖动。
        /// </summary>
        private sealed class CyclingTierNode : IUpdateable
        {
            private static readonly UpdateTier[] Cycle =
            {
                UpdateTier.Tier0, UpdateTier.Tier1, UpdateTier.Tier2, UpdateTier.Tier3,
            };

            private readonly int _offset;
            private int _calls;

            public CyclingTierNode(int offset)
            {
                _offset = offset;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                _calls++;
                return Cycle[(_offset + _calls) % Cycle.Length];
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

            public UpdateTier NextTier { get; set; } = UpdateTier.Tier0;
            public System.Action<UpdateScheduler, IUpdateable> Script { get; set; }

            /// <summary>
            /// 在第几次被派发时执行脚本（默认 1，即首次派发）。要构造「脚本发生在第二次派发」这类
            /// 时序时改它——只做首派的那一版表达不了「先让目标派发过一次，再对它动手」。
            /// </summary>
            public int RunAtDispatchCount { get; set; } = 1;
            public int OnUpdateCallCount { get; private set; }
            public int OnEnableCallCount { get; private set; }
            public int OnDisableCallCount { get; private set; }

            public ScriptedNode(UpdateScheduler scheduler)
            {
                _scheduler = scheduler;
            }

            public void OnEnable() => OnEnableCallCount++;

            public void OnDisable() => OnDisableCallCount++;

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                if (!_hasRunScript && OnUpdateCallCount >= RunAtDispatchCount)
                {
                    _hasRunScript = true;
                    Script?.Invoke(_scheduler, this);
                }
                return NextTier;
            }
        }

        /// <summary>
        /// 把每次派发按顺序记进共享列表的替身，用于断言「谁先被派发」。
        /// <para>既有的 <see cref="TestUpdateable"/> 只计数、不记录次序，而次序恰恰是
        /// <c>order</c> 唯一可观察的效果。恒定返回 Tier0，故整桶每帧全量派发。</para>
        /// </summary>
        private sealed class OrderRecorder : IUpdateable
        {
            private readonly List<string> _log;
            private readonly string _name;

            public OrderRecorder(List<string> log, string name)
            {
                _log = log;
                _name = name;
            }

            public void OnEnable() { }
            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                _log.Add(_name);
                return UpdateTier.Tier0;
            }
        }

        /// <summary>
        /// 三个时机都实现、各自计数的替身，用于锁定「调度器按自己的时机转发到对应方法」。
        /// </summary>
        private sealed class BothTimingsCounter : IUpdateable, ILateUpdateable, IFixedUpdateable
        {
            public int UpdateCount { get; private set; }
            public int LateCount { get; private set; }
            public int FixedCount { get; private set; }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                UpdateCount++;
                return UpdateTier.Tier0;
            }

            public UpdateTier OnLateUpdate(float deltaTime, float time)
            {
                LateCount++;
                return UpdateTier.Tier0;
            }

            public UpdateTier OnFixedUpdate(float deltaTime, float fixedTime)
            {
                FixedCount++;
                return UpdateTier.Tier0;
            }
        }

        /// <summary>
        /// 值类型节点，用于锁定「注册处直接拒绝值类型并留痕」。
        /// </summary>
        private struct ValueTypeNode : IUpdateable
        {
            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                return UpdateTier.Tier0;
            }
        }

        /// <summary>
        /// 按 Id 值相等的引用类型节点，用于锁定「身份判定按引用而非 <c>Equals</c>」。
        /// </summary>
        private sealed class EqualsByValueNode : IUpdateable
        {
            private readonly int _id;

            public EqualsByValueNode(int id)
            {
                _id = id;
            }

            public int OnUpdateCallCount { get; private set; }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateTier OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                return UpdateTier.Tier0;
            }

            public override bool Equals(object obj)
            {
                return obj is EqualsByValueNode other && other._id == _id;
            }

            public override int GetHashCode()
            {
                return _id;
            }
        }
    }
}