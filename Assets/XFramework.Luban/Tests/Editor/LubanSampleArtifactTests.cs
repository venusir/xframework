using System.IO;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// 示例生成物防腐：用与生成器相同的解析器读取 Samples~/Luban，断言表集合与形状。
    /// 示例不参与 Unity 编译，这是它唯一的机械守卫。
    /// </summary>
    [TestFixture]
    public sealed class LubanSampleArtifactTests
    {
        [Test]
        public void SampleGeneratedCode_MatchesExpectedModel()
        {
            string codeDir = Path.Combine(
                LubanTestPaths.PackageRoot, "Samples~", "Luban", "Generated", "Code");

            var model = LubanCodeParser.Parse(codeDir, "Tables");

            Assert.That(model.TablesNamespace, Is.EqualTo("cfg"));
            Assert.That(model.Tables.Count, Is.EqualTo(3));

            var item = Find(model, "TbItem");
            Assert.That(item.Shape, Is.EqualTo(LubanTableShape.Map));
            Assert.That(item.RowTypeFullName, Is.EqualTo("cfg.demo.Item"));
            Assert.That(item.KeyTypeText, Is.EqualTo("int"));

            var global = Find(model, "TbGlobal");
            Assert.That(global.Shape, Is.EqualTo(LubanTableShape.SingleRow));

            var spawn = Find(model, "TbSpawnPoint");
            Assert.That(spawn.Shape, Is.EqualTo(LubanTableShape.PureList));
        }

        [Test]
        public void SampleIntegration_FilesExist()
        {
            string integrationDir = Path.Combine(
                LubanTestPaths.PackageRoot, "Samples~", "Luban", "Generated", "Integration");

            string adapter = Path.Combine(integrationDir, "Adapter", "LubanSampleConfigAdapter.g.cs");
            string rowInterface = Path.Combine(integrationDir, "RowInterfaces", "cfg_demo_Item.g.cs");

            Assert.That(File.Exists(adapter), Is.True);
            Assert.That(File.ReadAllText(adapter), Does.Contain("LubanSampleConfigAdapter"));
            Assert.That(File.ReadAllText(adapter), Does.Contain("DataFileExtension => \".json\""));
            Assert.That(File.Exists(rowInterface), Is.True);
        }

        private static LubanGeneratedTable Find(LubanGeneratedModel model, string property)
        {
            foreach (var table in model.Tables)
            {
                if (table.PropertyName == property)
                {
                    return table;
                }
            }

            Assert.Fail("未找到表属性 " + property);
            return null;
        }
    }
}
