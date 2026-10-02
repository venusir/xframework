using NUnit.Framework;
using UnityEngine;
using XFramework.XBootstrap;
using XFramework.XSave;

namespace XFramework.XBootstrap.Tests
{
    /// <summary>
    /// <see cref="GameLauncher"/> 的扩展点：<c>ConfigureStages</c> 覆写后登记什么由子类决定。
    /// <para><b>为什么在 PlayMode</b>：<c>Awake</c> 是 Unity 生命周期回调，只有真的 <c>AddComponent</c> 才会跑。
    /// 三例都在断言后立刻销毁对象——<c>Start</c> 在下一帧才执行 <c>RunAsync</c>，绝不让它跑起来
    /// （否则会真去初始化 YooAsset）；<c>OnDestroy</c> 里的 <c>Bootstrap.Shutdown</c> 在未初始化态是安全空转。</para>
    /// </summary>
    class GameLauncherTests
    {
        #region Test Doubles

        private enum ProbeMode
        {
            /// <summary>覆写但不调 base：默认组合完全不登记。</summary>
            Takeover,

            /// <summary>覆写调 base 后替换内置 Save 阶段。</summary>
            ReplaceSave,
        }

        /// <summary>探针：覆写 <c>ConfigureStages</c> 的两种姿势。</summary>
        private sealed class ProbeLauncher : GameLauncher
        {
            public static ProbeMode Mode;
            public static SaveBootstrapStage Replacement;

            protected override void ConfigureStages()
            {
                if (Mode == ProbeMode.Takeover)
                    return; // 不调 base：登记什么完全由子类决定（这里刻意什么都不登记）

                base.ConfigureStages();
                Bootstrap.Unregister<SaveBootstrapStage>();
                Replacement = new SaveBootstrapStage();
                Bootstrap.Register(Replacement);
            }
        }

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            Bootstrap.Clear();
            ProbeLauncher.Mode = ProbeMode.Takeover;
            ProbeLauncher.Replacement = null;
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Clear();
        }

        #endregion

        #region ConfigureStages

        [Test]
        public void Awake_DefaultImplementation_RegistersBuiltInStages()
        {
            var go = new GameObject("launcher-default");
            try
            {
                // AddComponent 同步跑 Awake；Start 在下一帧——本例在它之前销毁，不让 RunAsync 真跑起来
                go.AddComponent<GameLauncher>();

                Assert.AreEqual(3, Bootstrap.Stages.Count);
                Assert.AreEqual(BootstrapPhases.Asset, Bootstrap.Stages[0].Phase);
                Assert.AreEqual(BootstrapPhases.Data, Bootstrap.Stages[1].Phase);
                Assert.AreEqual(BootstrapPhases.Save, Bootstrap.Stages[2].Phase);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Awake_OverrideWithoutBase_RegistersNothing()
        {
            ProbeLauncher.Mode = ProbeMode.Takeover;
            var go = new GameObject("launcher-takeover");
            try
            {
                go.AddComponent<ProbeLauncher>();

                Assert.AreEqual(0, Bootstrap.Stages.Count, "不调 base 即完全不登记默认组合");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Awake_OverrideWithBase_CanReplaceBuiltInStage()
        {
            ProbeLauncher.Mode = ProbeMode.ReplaceSave;
            var go = new GameObject("launcher-replace");
            try
            {
                go.AddComponent<ProbeLauncher>();

                int saveCount = 0;
                IBootstrapStage found = null;
                for (int i = 0; i < Bootstrap.Stages.Count; i++)
                {
                    if (Bootstrap.Stages[i] is SaveBootstrapStage)
                    {
                        saveCount++;
                        found = Bootstrap.Stages[i];
                    }
                }

                Assert.AreEqual(3, Bootstrap.Stages.Count, "替换后仍是三件（Asset + Data + 自定义 Save）");
                Assert.AreEqual(1, saveCount, "同类型只应剩自定义那一个");
                Assert.AreSame(ProbeLauncher.Replacement, found);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion
    }
}
