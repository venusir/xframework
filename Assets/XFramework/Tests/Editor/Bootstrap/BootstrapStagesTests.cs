using System;
using System.Collections.Generic;
using System.Reflection;
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

        #region FromProfile

        [Test]
        public void FromProfile_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => BootstrapStages.FromProfile(null, null));
        }

        [Test]
        public void FromProfile_EmptyProfile_RegistersAssetDataSaveOnly()
        {
            var profile = ScriptableObject.CreateInstance<BootstrapProfile>();
            try
            {
                BootstrapStages.FromProfile(profile, null);

                Assert.AreEqual(3, Bootstrap.Stages.Count, "空 Profile + 无 UI 根 = 默认三件");
                Assert.AreEqual(BootstrapPhases.Asset, Bootstrap.Stages[0].Phase);
                Assert.AreEqual(BootstrapPhases.Data, Bootstrap.Stages[1].Phase);
                Assert.AreEqual(BootstrapPhases.Save, Bootstrap.Stages[2].Phase);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FromProfile_FullProfile_RegistersConditionalStagesAndInjectsValues()
        {
            var uiGo = new GameObject("ui-root", typeof(RectTransform));
            var inputOptions = ScriptableObject.CreateInstance<InputSystemOptionsAsset>();
            var profile = ScriptableObject.CreateInstance<BootstrapProfile>();
            try
            {
                SetProfileField(profile, "_assetPackageName", "Pack");
                SetProfileField(profile, "_saveVersion", 3);
                SetProfileField(profile, "_inputOptions", inputOptions);
                SetProfileField(profile, "_defaultLanguage", "en");

                BootstrapStages.FromProfile(profile, uiGo.transform);

                Assert.AreEqual(6, Bootstrap.Stages.Count,
                    "Asset / Data / Save + UI / Input / Localization");

                var asset = (AssetBootstrapStage)Bootstrap.Stages[0];
                Assert.AreEqual("Pack", asset.Options.PackageName);
                Assert.AreEqual(3, ((SaveBootstrapStage)Bootstrap.Stages[2]).Options.CurrentVersion);

                bool hasUI = false;
                bool hasInput = false;
                bool hasLocalization = false;
                for (int i = 0; i < Bootstrap.Stages.Count; i++)
                {
                    if (Bootstrap.Stages[i] is UIBootstrapStage) hasUI = true;
                    if (Bootstrap.Stages[i] is InputBootstrapStage) hasInput = true;
                    if (Bootstrap.Stages[i] is LocalizationBootstrapStage) hasLocalization = true;
                }

                Assert.IsTrue(hasUI);
                Assert.IsTrue(hasInput);
                Assert.IsTrue(hasLocalization);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(uiGo);
                UnityEngine.Object.DestroyImmediate(inputOptions);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        /// <summary>经反射写 Profile 的私有字段（模拟 Inspector 里填资产）。</summary>
        private static void SetProfileField(BootstrapProfile profile, string fieldName, object value)
        {
            var field = typeof(BootstrapProfile).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"找不到字段 {fieldName}——它被改名了吗？改名请同步本夹具");
            field.SetValue(profile, value);
        }

        #endregion
    }
}
