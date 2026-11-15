using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XBootstrap;
using XFramework.XPipeline;

namespace XFramework.XBootstrap.Tests
{
    /// <summary>
    /// <see cref="GameLauncher"/> 的底座契约：<c>ConfigureStages</c> 抽象、底座不预设任何阶段、无序列化字段。
    /// <para><b>为什么在 PlayMode</b>：<c>Awake</c> 是 Unity 生命周期回调，只有真的 <c>AddComponent</c> 才会跑。
    /// 各例都在断言后立刻销毁对象——<c>Start</c> 在下一帧才执行 <c>RunAsync</c>，绝不让它跑起来
    /// （否则会真去初始化 YooAsset）；<c>OnDestroy</c> 里的 <c>Bootstrap.Shutdown</c> 在未初始化态是安全空转。</para>
    /// </summary>
    class GameLauncherTests
    {
        #region Test Doubles

        /// <summary>探针：只实现 <c>ConfigureStages</c>，登记一个自己的阶段。</summary>
        private sealed class BareLauncher : GameLauncher
        {
            protected override void ConfigureStages()
            {
                Bootstrap.Register(new BareStage());
            }
        }

        /// <summary>探针：什么都不登记。</summary>
        private sealed class SilentLauncher : GameLauncher
        {
            protected override void ConfigureStages()
            {
            }
        }

        private sealed class BareStage : IBootstrapStage
        {
            public int Phase => 42;

            public string Name => nameof(BareStage);

            public float Weight => 1f;

            public UniTask ExecuteAsync(PipelineStageContext context, CancellationToken cancellationToken)
                => UniTask.CompletedTask;

            public void Shutdown()
            {
            }
        }

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            Bootstrap.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Clear();
        }

        #endregion

        #region ConfigureStages

        /// <summary>底座与子类都不预设阶段：空覆写留下空登记表。</summary>
        [Test]
        public void Awake_SubclassRegistersNothing_LeavesRegistryEmpty()
        {
            var go = new GameObject("launcher-silent");
            try
            {
                go.AddComponent<SilentLauncher>();   // Awake 同步跑

                Assert.AreEqual(0, Bootstrap.Stages.Count, "底座与子类都不预设任何阶段");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 底座自身**不预设任何阶段**：登记表恰好是子类声明的那一个（「组合归使用方」的底线）。
        /// </summary>
        [Test]
        public void Awake_BaseClass_RegistersOnlyWhatTheSubclassDeclares()
        {
            var go = new GameObject("launcher-bare");
            try
            {
                go.AddComponent<BareLauncher>();   // Awake 同步跑

                Assert.AreEqual(1, Bootstrap.Stages.Count, "底座不得自己登记任何阶段");
                Assert.IsInstanceOf<BareStage>(Bootstrap.Stages[0], "登记表应恰好是子类声明的那一个");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region 字段面

        /// <summary>
        /// 底座**不带序列化字段**：Inspector 干净（Unity 无法隐藏继承来的序列化字段，
        /// 所以「不带字段」只能靠类型层次来保证）。
        /// <para>判据是**序列化面**而不是「任何实例字段」：非序列化状态不进 Inspector、
        /// 不影响这条契约；把断言写成「零实例字段」会过度约束实现。</para>
        /// </summary>
        [Test]
        public void BaseClass_HasNoSerializedFields()
        {
            Assert.AreEqual(0, CountSerializedFields(typeof(GameLauncher)),
                "抽象底座不得携带任何序列化字段（Inspector 干净）");
        }

        /// <summary>统计类型自身声明的序列化字段（public 或带 <see cref="SerializeField"/>）。</summary>
        private static int CountSerializedFields(System.Type type)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            int count = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].IsPublic || fields[i].IsDefined(typeof(SerializeField), inherit: false))
                    count++;
            }

            return count;
        }

        #endregion
    }
}
