using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XPipeline;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="Pipeline.BuildPhaseGroups"/> 测试:按相位分组装配(相位升序/组内并行/组间串行)、
    /// 空输入与参数防御。
    /// <para>全部用例经管线装配执行——EditMode 无 PlayerLoop 泵,挂起用 <see cref="UniTaskCompletionSource"/>
    /// 内联续体推进;无挂起子阶段整体同步执行,执行序由 ExecutionLog 精确断言。</para>
    /// </summary>
    class PhaseStageGroupTests
    {
        #region 分组装配

        [Test]
        public void GroupsByPhaseAscending_PhaseGroupsInInputOrder()
        {
            var log = new List<string>();
            var a0 = new FakePhaseStage { Name = "a0", Phase = 0, ExecutionLog = log };
            var b4 = new FakePhaseStage { Name = "b4", Phase = 4, ExecutionLog = log };
            var c3 = new FakePhaseStage { Name = "c3", Phase = 3, ExecutionLog = log };
            var d0 = new FakePhaseStage { Name = "d0", Phase = 0, ExecutionLog = log };

            var groups = Pipeline.BuildPhaseGroups(new IPhaseStage[] { a0, b4, c3, d0 });

            Assert.AreEqual(3, groups.Count, "不同相位应各装配一组");
            Assert.AreEqual("Phase-0", groups[0].Name, "组名应为默认格式 Phase-{0}");
            Assert.AreEqual("Phase-3", groups[1].Name, "相位组应按相位号升序排列");
            Assert.AreEqual("Phase-4", groups[2].Name);

            // 无挂起子阶段全部同步执行:执行序 = 组间相位升序 × 组内输入顺序
            var pipeline = Pipeline.Create();
            for (int i = 0; i < groups.Count; i++) pipeline.AddStage(groups[i]);
            pipeline.RunAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "a0", "d0", "c3", "b4" }, log,
                "组内保持输入顺序,组间按相位升序串行");
        }

        [Test]
        public void EmptyInput_ReturnsEmptyList()
        {
            var groups = Pipeline.BuildPhaseGroups(new IPhaseStage[0]);
            Assert.AreEqual(0, groups.Count, "空输入应返回空清单,不抛");
        }

        [Test]
        public void CustomNameFormat_IsApplied()
        {
            var a = new FakePhaseStage { Phase = 2 };
            var groups = Pipeline.BuildPhaseGroups(new IPhaseStage[] { a }, "Load-{0}");
            Assert.AreEqual("Load-2", groups[0].Name, "自定义 nameFormat 应作用于相位组名");
        }

        [Test]
        public void GroupWeight_IsSumOfChildWeights()
        {
            var a = new FakePhaseStage { Phase = 0, Weight = 2f };
            var b = new FakePhaseStage { Phase = 0, Weight = 3f };
            var groups = Pipeline.BuildPhaseGroups(new IPhaseStage[] { a, b });
            Assert.AreEqual(5f, groups[0].Weight, 0.001f, "相位组权重应为组内子阶段声明权重之和");
        }

        #endregion

        #region 并行与串行语义

        [Test]
        public void SamePhase_ExecutesInParallel()
        {
            var a = new FakePhaseStage { Name = "a", Phase = 1, Gate = new UniTaskCompletionSource() };
            var b = new FakePhaseStage { Name = "b", Phase = 1, Gate = new UniTaskCompletionSource() };
            var groups = Pipeline.BuildPhaseGroups(new IPhaseStage[] { a, b });

            bool completed = false;
            var pipeline = Pipeline.Create();
            pipeline.OnCompleted += () => completed = true;
            for (int i = 0; i < groups.Count; i++) pipeline.AddStage(groups[i]);

            var task = pipeline.RunAsync();

            Assert.AreEqual(1, a.ExecuteCount, "同相位子阶段应并行启动");
            Assert.AreEqual(1, b.ExecuteCount, "同相位子阶段不被兄弟挂起阻塞");

            a.Gate.TrySetResult();
            b.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
            Assert.IsTrue(completed, "全部放行应收敛到完成");
        }

        [Test]
        public void DifferentPhases_RunSerially()
        {
            var log = new List<string>();
            var first = new FakePhaseStage { Name = "first", Phase = 0, Gate = new UniTaskCompletionSource(), ExecutionLog = log };
            var second = new FakePhaseStage { Name = "second", Phase = 1, ExecutionLog = log };

            var groups = Pipeline.BuildPhaseGroups(new IPhaseStage[] { second, first });

            var pipeline = Pipeline.Create();
            for (int i = 0; i < groups.Count; i++) pipeline.AddStage(groups[i]);

            var task = pipeline.RunAsync();

            Assert.AreEqual(0, second.ExecuteCount, "后相位阶段不得先于前相位阶段启动");

            first.Gate.TrySetResult();
            task.GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "first", "second" }, log, "前相位完成后才启动后相位");
        }

        #endregion

        #region 参数防御

        [Test]
        public void NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Pipeline.BuildPhaseGroups(null));
            Assert.Throws<ArgumentNullException>(() => Pipeline.BuildPhaseGroups(new IPhaseStage[0], null));
        }

        [Test]
        public void NullElement_Throws()
        {
            Assert.Throws<ArgumentException>(() => Pipeline.BuildPhaseGroups(new IPhaseStage[] { null }));
        }

        #endregion
    }
}
