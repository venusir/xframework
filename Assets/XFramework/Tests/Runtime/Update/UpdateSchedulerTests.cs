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
        public UpdateLOD ReturnLOD { get; set; } = UpdateLOD.Tier0;
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
            ReturnLOD = UpdateLOD.Tier0;
            ThrowException = false;
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

            // _node 返回 Tier5：本次 Tick 里它会被迁到 Tier5 桶（也走 pending 路径）
            _node.ReturnLOD = UpdateLOD.Tier5;

            // registrator 在自己的 OnUpdate 里注册 lateNode——此时调度器正在迭代，该注册应被缓冲
            var registrator = new RegistratorNode(_scheduler, lateNode, depth: 1);
            _scheduler.Register(registrator, depth: 0);

            _scheduler.Tick(time: 1.0f);

            // 缓冲的操作在 Tick 结束时统一生效：
            // 桶 0 = [registrator, lateNode]（lateNode 用默认 Tier0 注册），桶 5 = [_node]
            Assert.AreEqual(3, _scheduler.TotalCount, "Tick 期间发起的注册与 LOD 迁移都应在结束时生效");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier5), "_node 返回 Tier5 后应已迁入该桶");
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
            var node = new ScriptedNode(_scheduler) { NextLOD = UpdateLOD.Tier3 };
            node.Script = (scheduler, self) => scheduler.Unregister(self);
            _scheduler.Register(node, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(0, _scheduler.TotalCount, "注销后不应残留任何条目");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier3), "迁移必须作废，不能把节点插进新桶");

            _scheduler.Tick(time: 2.0f);
            Assert.AreEqual(1, node.OnUpdateCallCount, "注销后不应再被派发");
        }

        [Test]
        public void UnregisterByOther_AfterTargetDispatched_MoveIsDiscarded()
        {
            var target = new TestUpdateable { ReturnLOD = UpdateLOD.Tier3 };
            var unregistrator = new ScriptedNode(_scheduler);
            unregistrator.Script = (scheduler, self) => scheduler.Unregister(target);

            // 同深度时按注册顺序派发：unregistrator 先跑，注销操作排在 target 的迁移操作之前
            _scheduler.Register(unregistrator, depth: 0);
            _scheduler.Register(target, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(1, target.OnUpdateCallCount, "注销被缓冲，目标本帧仍应被派发一次");
            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 unregistrator");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier3), "迁移必须作废");

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
                scheduler.Register(self, depth: 0, initialLOD: UpdateLOD.Tier2);
            };
            _scheduler.Register(node, depth: 0);

            float time = 0f;
            _scheduler.Tick(time += FrameSeconds);

            Assert.AreEqual(1, _scheduler.TotalCount, "先注销再注册应恰好剩一条");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier2), "重新注册应使用新的 LOD");

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
            // 同一帧内既迁移 LOD 又禁用自己：迁移是条件操作，节点已被移入禁用表后必须作废
            var node = new ScriptedNode(_scheduler) { NextLOD = UpdateLOD.Tier3 };
            node.Script = (scheduler, self) => scheduler.Disable(self);
            _scheduler.Register(node, depth: 0);

            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(0, _scheduler.TotalCount);
            Assert.AreEqual(1, _scheduler.DisabledCount);
            Assert.IsFalse(_scheduler.IsEnabled(node));
            Assert.AreEqual(1, node.OnDisableCallCount);
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier3),
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
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier0), "桶里只有 a 一个");
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
            var node = new ScriptedNode(_scheduler) { NextLOD = UpdateLOD.Tier1 };
            node.Script = (scheduler, self) => scheduler.Clear();
            _scheduler.Register(node, depth: 0, initialLOD: UpdateLOD.Tier1);

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
            var a = new ScriptedNode(_scheduler) { NextLOD = UpdateLOD.Tier3 };
            var b = new TestUpdateable();
            a.Script = (scheduler, self) => scheduler.Unregister(self);

            _scheduler.Register(a, depth: 0);
            _scheduler.Register(b, depth: 0);

            _scheduler.ProcessImmediate(a, deltaTime: 0.5f, time: 1.0f);

            Assert.AreEqual(1, _scheduler.TotalCount, "只剩 b");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier0), "b 仍在原桶");
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier3), "迁移必须作废");

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
        public void SlicedBucket_SpreadsLoadEvenly_NoIdleFrames()
        {
            // 17 个条目、8 个切片：区间切片派发成 3,3,3,3,3,2,0,0（后两格白跑一遍循环），
            // 步长切片摊成 3,2,2,2,2,2,2,2——每格都干活且每格只差 1
            const int nodeCount = 17;
            var nodes = new TestUpdateable[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                nodes[i] = new TestUpdateable { ReturnLOD = UpdateLOD.Tier3 };
                _scheduler.Register(nodes[i], depth: 0, initialLOD: UpdateLOD.Tier3);
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
        public void SlicedNode_ReceivesAccumulatedDelta()
        {
            // 降频不导致时间失真：被跳过的帧应累积进下一次的 deltaTime——这是本调度器
            // 相对「固定步长 + 累加器」方案的核心取舍，此前零覆盖
            var node = new TestUpdateable { ReturnLOD = UpdateLOD.Tier3 };
            _scheduler.Register(node, depth: 0, initialLOD: UpdateLOD.Tier3);

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
            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Tier0);
            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Tier0);

            Assert.AreEqual(1, _scheduler.TotalCount);

            _scheduler.Tick(time: 1.0f);
            Assert.AreEqual(1, _node.OnUpdateCallCount, "每帧恰好派发一次");

            _scheduler.Unregister(_node);
            Assert.AreEqual(0, _scheduler.TotalCount, "注销后不应留下第二条条目");
        }

        [Test]
        public void RegisterTwice_DifferentLOD_MovesToLatestBucket()
        {
            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Tier3);
            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Tier1);

            Assert.AreEqual(1, _scheduler.TotalCount);
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier3), "旧桶不应残留条目");
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier1));
        }

        [Test]
        public void BucketIndex_StaysConsistentUnderLodChurn()
        {
            // 桶索引只在 ApplyOp 一处维护：任何一条路径漏写，后续的迁移/禁用/注销就会找不到节点。
            // 先制造持续的迁桶抖动，再逐一对全部节点做禁用/启用/注销——索引一旦与桶内容脱节，
            // 这些操作就会「找不到人」，表现为计数不减（幽灵条目）或启用后回不来
            const int nodeCount = 50;
            var nodes = new CyclingLodNode[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                nodes[i] = new CyclingLodNode(i);
                _scheduler.Register(nodes[i], depth: i % 3, initialLOD: UpdateLOD.Tier0);
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
            var scaled = new TestUpdateable { ReturnLOD = UpdateLOD.Tier0 };
            var unscaled = new TestUpdateable { ReturnLOD = UpdateLOD.Tier0 };

            _scheduler.Register(scaled, depth: 0);
            _scheduler.Register(unscaled, depth: 0, timeMode: UpdateTimeMode.Unscaled);

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
            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Tier2);
            _scheduler.Register(new TestUpdateable(), depth: 0, initialLOD: UpdateLOD.Tier2,
                timeMode: UpdateTimeMode.Unscaled);

            Assert.AreEqual(2, _scheduler.TotalCount);
            Assert.AreEqual(2, _scheduler.GetCount(UpdateLOD.Tier2), "查询应跨时间轴聚合");
        }

        [Test]
        public void UnscaledSlicedNode_AccumulatesUnscaledDelta()
        {
            // 每条轴有独立的节拍与切片相位：墙钟轴的周期由它自己推进，
            // 逻辑时刻在本用例里全程不动
            var node = new TestUpdateable { ReturnLOD = UpdateLOD.Tier3 };
            _scheduler.Register(node, depth: 0, initialLOD: UpdateLOD.Tier3,
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
            var node = new TestUpdateable { ReturnLOD = UpdateLOD.Tier2 };
            _scheduler.Register(node, depth: 0, initialLOD: UpdateLOD.Tier2);

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
            var node = new TestUpdateable { ReturnLOD = UpdateLOD.Tier0 };
            _scheduler.Register(node, depth: 0);

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
        public void TimeScaleZero_FreezesScaledAxisOnly()
        {
            // timeScale = 0 时 Time.time 冻结、Time.unscaledTime 照走：
            // 逻辑轴停摆，墙钟轴（暂停菜单、UI 动画、振动到期）继续运行
            var scaled = new TestUpdateable { ReturnLOD = UpdateLOD.Tier0 };
            var unscaled = new TestUpdateable { ReturnLOD = UpdateLOD.Tier0 };
            _scheduler.Register(scaled, depth: 0);
            _scheduler.Register(unscaled, depth: 0, timeMode: UpdateTimeMode.Unscaled);

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
            var scaledNode = new TestUpdateable { ReturnLOD = UpdateLOD.Tier3 };
            var unscaledNode = new TestUpdateable { ReturnLOD = UpdateLOD.Tier3 };
            _scheduler.Register(scaledNode, depth: 0, initialLOD: UpdateLOD.Tier3);
            _scheduler.Register(unscaledNode, depth: 1, initialLOD: UpdateLOD.Tier3,
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
            _node.ReturnLOD = UpdateLOD.Tier3;
            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Tier3);

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
            var node = new FixedUpdateNode { NextLOD = UpdateLOD.Tier3 };
            fixedScheduler.Register(node, depth: 0, initialLOD: UpdateLOD.Tier3);

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
            _scheduler.Register(_node, depth: 0);   // Tier0：每帧派发，delta 应恒为帧步长

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
            var scaledNode = new TestUpdateable { ReturnLOD = UpdateLOD.Tier0 };
            var unscaledNode = new TestUpdateable { ReturnLOD = UpdateLOD.Tier0 };

            _scheduler.Tick(new UpdateClock(time: 100f, unscaledTime: 10000f));   // 首帧只锚定
            _scheduler.Register(scaledNode, depth: 0);
            _scheduler.Register(unscaledNode, depth: 1, timeMode: UpdateTimeMode.Unscaled);

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
            _scheduler.Register(_node, depth: 0);
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
            var tier6 = new TestUpdateable { ReturnLOD = UpdateLOD.Tier6 };
            var tier7 = new TestUpdateable { ReturnLOD = UpdateLOD.Tier7 };
            _scheduler.Register(tier6, depth: 0, initialLOD: UpdateLOD.Tier6);
            _scheduler.Register(tier7, depth: 1, initialLOD: UpdateLOD.Tier7);

            float time = 0f;
            for (int i = 0; i < 128; i++)
            {
                _scheduler.Tick(time += FrameSeconds);
            }

            // 128 格窗口内：Tier6（64 格）在第 0、64 格各一次，Tier7（128 格）只在第 0 格
            Assert.AreEqual(2, tier6.OnUpdateCallCount, "Tier6 每 64 格轮到一次");
            Assert.AreEqual(1, tier7.OnUpdateCallCount, "Tier7 每 128 格轮到一次");

            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier6));
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier7));
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
                nodes[i] = new TestUpdateable { ReturnLOD = UpdateLOD.Tier5 };
                _scheduler.Register(nodes[i], depth: 0, initialLOD: UpdateLOD.Tier5);
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
            var node = new TestUpdateable { ReturnLOD = UpdateLOD.Tier3 };
            scheduler.Register(node, depth: 0, initialLOD: UpdateLOD.Tier3);

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
            var node = new TestUpdateable { ReturnLOD = UpdateLOD.Tier0 };
            _scheduler.Register(node, depth: 0);

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
            fixedScheduler.Register(node, depth: 0);

            fixedScheduler.Tick(0.02f);
            fixedScheduler.Tick(0.04f);

            Assert.AreEqual(2, node.OnFixedUpdateCallCount);
            Assert.AreEqual(0, node.OnUpdateCallCount);

            fixedScheduler.Clear();
        }

        [Test]
        public void OnUpdate_ReturnsDifferentLOD_MovesBucket()
        {
            _scheduler.Register(_node, depth: 0);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier0));
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier3));

            // Move to LOD Tier3
            _node.ReturnLOD = UpdateLOD.Tier3;
            _scheduler.Tick(time: 1.0f);

            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier0));
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier3));
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
            _node.ReturnLOD = UpdateLOD.Tier4;

            _scheduler.ProcessImmediate(_node, deltaTime: 0.5f, time: 10.0f);

            Assert.AreEqual(1, _node.OnUpdateCallCount);
            Assert.AreEqual(0.5f, _node.DeltaTimes[0], 1e-6f);
            Assert.AreEqual(10.0f, _node.Times[0], 1e-6f);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier4));
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier0));
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
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier0));

            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Tier0);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier0));

            _scheduler.Register(new TestUpdateable(), depth: 0, initialLOD: UpdateLOD.Tier3);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier3));
        }

        [Test]
        public void Register_WithInitialLOD_UsesCorrectBucket()
        {
            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Tier5);
            Assert.AreEqual(1, _scheduler.GetCount(UpdateLOD.Tier5));
            Assert.AreEqual(0, _scheduler.GetCount(UpdateLOD.Tier0));
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

            _scheduler.Register(_node, depth: 0, initialLOD: UpdateLOD.Tier0);
            _scheduler.Register(new TestUpdateable(), depth: 0, initialLOD: UpdateLOD.Tier1);
            _scheduler.Register(new TestUpdateable(), depth: 0, initialLOD: UpdateLOD.Tier3);

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
                return UpdateLOD.Tier0;
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
                return UpdateLOD.Tier0;
            }
        }

        /// <summary>
        /// 同时实现两个时机接口的测试替身，用于确认各自只回调自己那个方法。
        /// </summary>
        private sealed class FixedUpdateNode : IUpdateable, IFixedUpdateable
        {
            /// <summary>下一次派发返回的档位。默认 Tier0，即留在每帧桶。</summary>
            public UpdateLOD NextLOD { get; set; } = UpdateLOD.Tier0;

            public int OnUpdateCallCount { get; private set; }
            public int OnFixedUpdateCallCount { get; private set; }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
            {
                OnUpdateCallCount++;
                return UpdateLOD.Tier0;
            }

            public UpdateLOD OnFixedUpdate(float deltaTime, float fixedTime)
            {
                OnFixedUpdateCallCount++;
                return NextLOD;
            }
        }

        /// <summary>
        /// 每次派发都换一个 LOD 的测试替身，用于制造持续的迁桶抖动。
        /// </summary>
        private sealed class CyclingLodNode : IUpdateable
        {
            private static readonly UpdateLOD[] Cycle =
            {
                UpdateLOD.Tier0, UpdateLOD.Tier1, UpdateLOD.Tier2, UpdateLOD.Tier3,
            };

            private readonly int _offset;
            private int _calls;

            public CyclingLodNode(int offset)
            {
                _offset = offset;
            }

            public void OnEnable() { }

            public void OnDisable() { }

            public UpdateLOD OnUpdate(float deltaTime, float time)
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

            public UpdateLOD NextLOD { get; set; } = UpdateLOD.Tier0;
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