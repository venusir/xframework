using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XBootstrap;
using XFramework.XData;
using XFramework.XInput;
using XFramework.XInput.Default;
using XFramework.XLocalization;
using XFramework.XSave;
using XFramework.XUI;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="BootstrapStages"/> 目录：每个入口产出的类型 / 相位，以及 options 透传。
    /// <para>只构造阶段、不执行——模块初始化由各阶段自己的夹具覆盖。</para>
    /// </summary>
    class BootstrapStagesTests
    {
        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            Bootstrap.Clear();
            Bootstrap.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Clear();
            Bootstrap.ResetForNewSession();
        }

        #endregion

        [Test]
        public void Catalog_ProducesBuiltInStagesWithExpectedPhases()
        {
            var uiGo = new GameObject("ui-root", typeof(RectTransform));
            try
            {
                Assert.IsInstanceOf<AssetBootstrapStage>(BootstrapStages.Asset());
                Assert.IsInstanceOf<DataBootstrapStage>(BootstrapStages.Data());
                Assert.IsInstanceOf<SaveBootstrapStage>(BootstrapStages.Save());
                Assert.IsInstanceOf<UIBootstrapStage>(BootstrapStages.UI(uiGo.transform));
                Assert.IsInstanceOf<InputBootstrapStage>(BootstrapStages.Input(new InputSystemOptions()));
                Assert.IsInstanceOf<LocalizationBootstrapStage>(
                    BootstrapStages.Localization("en", new Dictionary<string, string>()));
                Assert.IsInstanceOf<LocalizationBootstrapStage>(
                    BootstrapStages.Localization("en", (TextAsset)null));

                Assert.AreEqual(BootstrapPhases.Asset, BootstrapStages.Asset().Phase);
                Assert.AreEqual(BootstrapPhases.Data, BootstrapStages.Data().Phase);
                Assert.AreEqual(BootstrapPhases.Save, BootstrapStages.Save().Phase);
                Assert.AreEqual(BootstrapPhases.UI, BootstrapStages.UI(uiGo.transform).Phase);
                Assert.AreEqual(BootstrapPhases.Input, BootstrapStages.Input(new InputSystemOptions()).Phase);
                Assert.AreEqual(BootstrapPhases.Localization, BootstrapStages.Localization("en").Phase);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(uiGo);
            }
        }

        [Test]
        public void Catalog_AssetAndSave_PassOptionsThrough()
        {
            var assetOptions = new AssetInitOptions { PackageName = "MyPack" };
            var saveOptions = new SaveOptions { CurrentVersion = 9 };

            var asset = (AssetBootstrapStage)BootstrapStages.Asset(assetOptions);
            var save = (SaveBootstrapStage)BootstrapStages.Save(saveOptions);

            Assert.AreSame(assetOptions, asset.Options, "Asset 目录应透传同一个 options 实例");
            Assert.AreSame(saveOptions, save.Options, "Save 目录应透传同一个 options 实例");
        }

    }
}
