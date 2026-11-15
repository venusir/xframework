using NUnit.Framework;
using XFramework.XAsset;
using XFramework.XBootstrap;
using XFramework.XData;
using XFramework.XSave;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 内置相位不变量：把「注释里的约定」变成机器校验。
    /// <para>这些用例纯读常量 / 阶段实例，不初始化任何模块。</para>
    /// </summary>
    class BootstrapInvariantTests
    {
        [Test]
        public void BuiltInPhases_PreserveModuleDependencies()
        {
            Assert.Greater(new SaveBootstrapStage().Phase, new DataBootstrapStage().Phase,
                "Save 运行期经 DataManager 收集/应用数据块快照，必须晚于 Data");
            Assert.Greater(new DataBootstrapStage().Phase, new AssetBootstrapStage().Phase,
                "Data / Save 的恢复与落盘依赖资源系统已就绪");
        }

        [Test]
        public void UserStart_EqualsLocalization_IsIntentional()
        {
            Assert.AreEqual(BootstrapPhases.Localization, BootstrapPhases.UserStart,
                "同值 = 同相位并行；依赖本地化的业务阶段应取更大的值（见 README 的相位约定）");
        }

        [Test]
        public void BuiltInPhases_DoNotOccupyTheReservedGap()
        {
            // 5–89 是「现状空闲」而非保留承诺；内置阶段落在其中会让相位表与实况悄悄不符。
            int[] builtIn =
            {
                BootstrapPhases.Asset,
                BootstrapPhases.Input,
                BootstrapPhases.UI,
                BootstrapPhases.Data,
                BootstrapPhases.Save,
                BootstrapPhases.Localization,
            };

            for (int i = 0; i < builtIn.Length; i++)
            {
                Assert.IsFalse(builtIn[i] >= 5 && builtIn[i] <= 89,
                    $"内置相位 {builtIn[i]} 落在 5–89 的空闲区间——若确要占用，请同步更新 README 的相位表与保留说明");
            }
        }
    }
}
