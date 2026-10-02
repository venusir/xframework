using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XBootstrap;
using XFramework.XInput;
using XFramework.XInput.Default;
using XFramework.XLocalization;
using XFramework.XPipeline;
using XFramework.XSave;
using XFramework.XUI;

namespace XFramework.XBootstrap.Tests
{
    /// <summary>
    /// <see cref="DefaultGameLauncher"/> 的扩展点：<c>ConfigureStages</c> 覆写后登记什么由子类决定。
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
        private sealed class ProbeLauncher : DefaultGameLauncher
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
                go.AddComponent<DefaultGameLauncher>();

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

        #region 字段面（Inspector 上声明的配置）

        /// <summary>
        /// 经反射写 <c>DefaultGameLauncher</c> 的私有序列化字段——为不改生产面的可见性，夹具接受这点成本。
        /// 字段改名会让本夹具直接报「找不到字段」，而不是静默失效。
        /// </summary>
        private static void SetField(DefaultGameLauncher launcher, string fieldName, object value)
        {
            var field = typeof(DefaultGameLauncher).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"找不到字段 {fieldName}——它被改名了吗？改名请同步本夹具");
            field.SetValue(launcher, value);
        }

        private static T FindStage<T>() where T : class, IBootstrapStage
        {
            for (int i = 0; i < Bootstrap.Stages.Count; i++)
            {
                if (Bootstrap.Stages[i] is T match)
                    return match;
            }

            return null;
        }

        /// <summary>
        /// 字段填了就注入到对应模块：包名/存档版本进 options，UI/Input/Localization 各自登记一个阶段。
        /// <para>做法：先让 GameObject <b>保持未激活</b>再 <c>AddComponent</c>——未激活时 Awake 不跑，
        /// 于是有窗口把字段设好，再 <c>SetActive(true)</c> 触发 Awake。</para>
        /// </summary>
        [Test]
        public void Awake_WithFields_InjectsThemIntoModules()
        {
            var go = new GameObject("launcher-configured");
            go.SetActive(false);
            var uiRoot = new GameObject("ui-root", typeof(RectTransform));
            var inputOptions = ScriptableObject.CreateInstance<InputSystemOptionsAsset>();
            try
            {
                var launcher = go.AddComponent<DefaultGameLauncher>();
                SetField(launcher, "_assetPackageName", "MyPack");
                SetField(launcher, "_saveVersion", 7);
                SetField(launcher, "_uiRoot", uiRoot.transform);
                SetField(launcher, "_inputOptions", inputOptions);
                SetField(launcher, "_defaultLanguage", "en");

                go.SetActive(true);   // 此刻 Awake 才跑

                Assert.AreEqual(6, Bootstrap.Stages.Count,
                    "默认三件 + UI + Input + Localization");

                var asset = FindStage<AssetBootstrapStage>();
                Assert.IsNotNull(asset, "Asset 阶段应被字段面重建（替换掉内置那份）");
                Assert.AreEqual("MyPack", asset.Options.PackageName, "包名应注入 Asset 阶段");
                Assert.AreEqual(7, FindStage<SaveBootstrapStage>()?.Options.CurrentVersion,
                    "存档版本应注入 Save 阶段");
                Assert.AreEqual(BootstrapPhases.UI, FindStage<UIBootstrapStage>()?.Phase);
                Assert.AreEqual(BootstrapPhases.Input, FindStage<InputBootstrapStage>()?.Phase);
                Assert.AreEqual(BootstrapPhases.Localization, FindStage<LocalizationBootstrapStage>()?.Phase);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(uiRoot);
                Object.DestroyImmediate(inputOptions);
            }
        }

        /// <summary>留空的字段不登记阶段——与不用本组件时一致（不替使用方初始化他没要的模块）。</summary>
        [Test]
        public void Awake_WithOnlyUiRoot_RegistersOnlyTheUiStage()
        {
            var go = new GameObject("launcher-ui-only");
            go.SetActive(false);
            var uiRoot = new GameObject("ui-root", typeof(RectTransform));
            try
            {
                var launcher = go.AddComponent<DefaultGameLauncher>();
                SetField(launcher, "_uiRoot", uiRoot.transform);

                go.SetActive(true);

                Assert.AreEqual(4, Bootstrap.Stages.Count, "默认三件 + UI");
                Assert.IsNotNull(FindStage<UIBootstrapStage>());
                Assert.IsNull(FindStage<InputBootstrapStage>(), "输入资产留空就不该登记 Input 阶段");
                Assert.IsNull(FindStage<LocalizationBootstrapStage>(), "语言留空就不该登记本地化阶段");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(uiRoot);
            }
        }

        #endregion

        #region 抽象底座（完全自控）

        /// <summary>「完全自控」的最小实现：只实现底座上的 <c>ConfigureStages</c>。</summary>
        private sealed class BareLauncher : GameLauncher
        {
            protected override void ConfigureStages()
            {
                Bootstrap.Register(new BareStage());
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

        /// <summary>
        /// 底座自身**不预设任何阶段**：登记表恰好是子类声明的那一个（这条是拆分的目的之一——
        /// 「完全自控」的人不该被默认组合牵连）。
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

        /// <summary>
        /// 底座**不带序列化字段**、字段面全在默认实现上——这是这次拆分的另一半目的：
        /// 完全自控的人在 Inspector 里应当是干净的（Unity 无法隐藏继承来的序列化字段，
        /// 所以「不带字段」只能靠类型层次来保证）。
        /// </summary>
        [Test]
        public void BaseClass_HasNoFields_WhileDefaultCarriesTheSurface()
        {
            var baseFields = typeof(GameLauncher).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsEmpty(baseFields, "抽象底座不得携带任何实例字段");

            var defaultFields = typeof(DefaultGameLauncher).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.AreEqual(8, defaultFields.Length, "字段面（Asset×3 / Save×1 / UI×1 / Input×1 / Localization×2）应都在默认实现上");
        }

        #endregion
    }
}
