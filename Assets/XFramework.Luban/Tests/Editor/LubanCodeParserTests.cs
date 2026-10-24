using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// 用真实 Luban 5.1.0 生成物验证解析器（Bin / Json 两种 target、四种表形状）。
    /// </summary>
    [TestFixture]
    public sealed class LubanCodeParserTests
    {
        [Test]
        public void Parse_BinFixture_ReturnsExpectedModel()
        {
            var model = LubanCodeParser.Parse(
                Path.Combine(LubanTestPaths.FixturesRoot, "Bin"), "Tables");

            Assert.That(model.LoaderKind, Is.EqualTo(LubanLoaderKind.ByteBuf));
            AssertModel(model);
        }

        [Test]
        public void Parse_JsonFixture_ReturnsExpectedModel()
        {
            var model = LubanCodeParser.Parse(
                Path.Combine(LubanTestPaths.FixturesRoot, "Json"), "Tables");

            Assert.That(model.LoaderKind, Is.EqualTo(LubanLoaderKind.JsonNode));
            AssertModel(model);
        }

        [Test]
        public void Parse_MissingTablesClass_Throws()
        {
            string dir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "Foo.cs"), "public class Foo { }");
                Assert.Throws<LubanGenerationException>(
                    () => LubanCodeParser.Parse(dir, "Tables"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Parse_MultipleSeparateIndexes_Throws()
        {
            string dir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(dir, "Tables.cs"),
                    "using Luban;\n" +
                    "namespace cfg\n" +
                    "{\n" +
                    "public partial class Tables\n" +
                    "{\n" +
                    "    public demo.TbX TbX {get; }\n" +
                    "    public Tables(System.Func<string, ByteBuf> loader)\n" +
                    "    {\n" +
                    "        TbX = new demo.TbX(loader(\"demo_tbx\"));\n" +
                    "    }\n" +
                    "}\n" +
                    "}\n");

                string demo = Path.Combine(dir, "demo");
                Directory.CreateDirectory(demo);
                File.WriteAllText(Path.Combine(demo, "X.cs"),
                    "namespace cfg.demo\n" +
                    "{\n" +
                    "public sealed partial class X : Luban.BeanBase\n" +
                    "{\n" +
                    "    public readonly int A;\n" +
                    "    public readonly int B;\n" +
                    "}\n" +
                    "}\n");
                File.WriteAllText(Path.Combine(demo, "TbX.cs"),
                    "namespace cfg.demo\n" +
                    "{\n" +
                    "public partial class TbX\n" +
                    "{\n" +
                    "    private System.Collections.Generic.Dictionary<int, demo.X> _dataMap_a;\n" +
                    "    private System.Collections.Generic.Dictionary<int, demo.X> _dataMap_b;\n" +
                    "    public System.Collections.Generic.IReadOnlyList<demo.X> DataList => null;\n" +
                    "}\n" +
                    "}\n");

                var ex = Assert.Throws<LubanGenerationException>(
                    () => LubanCodeParser.Parse(dir, "Tables"));
                Assert.That(ex.Message, Does.Contain("多个独立索引"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void AssertModel(LubanGeneratedModel model)
        {
            Assert.That(model.TablesNamespace, Is.EqualTo("cfg"));
            Assert.That(model.TablesClassName, Is.EqualTo("Tables"));
            Assert.That(model.TablesFullName, Is.EqualTo("cfg.Tables"));
            Assert.That(model.Tables.Count, Is.EqualTo(5));

            var item = Find(model, "TbItem");
            Assert.That(item.Shape, Is.EqualTo(LubanTableShape.Map));
            Assert.That(item.OutputDataName, Is.EqualTo("demo_tbitem"));
            Assert.That(item.RowTypeFullName, Is.EqualTo("cfg.demo.Item"));
            Assert.That(item.KeyTypeText, Is.EqualTo("int"));
            Assert.That(item.IdExpression, Is.EqualTo("Id"));
            Assert.That(item.RowExpression, Is.EqualTo("row.Id"));
            Assert.That(item.RowIsClass, Is.True);

            var global = Find(model, "TbGlobal");
            Assert.That(global.Shape, Is.EqualTo(LubanTableShape.SingleRow));
            Assert.That(global.RowTypeFullName, Is.EqualTo("cfg.demo.Global"));
            Assert.That(global.KeyTypeText, Is.Null);

            var spawn = Find(model, "TbSpawnPoint");
            Assert.That(spawn.Shape, Is.EqualTo(LubanTableShape.PureList));
            Assert.That(spawn.RowTypeFullName, Is.EqualTo("cfg.demo.SpawnPoint"));
            Assert.That(spawn.KeyTypeText, Is.Null);

            var skill = Find(model, "TbSkillLevel");
            Assert.That(skill.Shape, Is.EqualTo(LubanTableShape.IndexedList));
            Assert.That(skill.RowTypeFullName, Is.EqualTo("cfg.demo.SkillLevel"));
            Assert.That(skill.KeyTypeText, Is.EqualTo("(int, int)"));
            Assert.That(skill.IdExpression, Is.EqualTo("(SkillId, Level)"));
            Assert.That(skill.RowExpression, Is.EqualTo("(row.SkillId, row.Level)"));

            var monster = Find(model, "TbMonster");
            Assert.That(monster.Shape, Is.EqualTo(LubanTableShape.IndexedList));
            Assert.That(monster.RowTypeFullName, Is.EqualTo("cfg.demo.Monster"));
            Assert.That(monster.KeyTypeText, Is.EqualTo("int"));
            Assert.That(monster.IdExpression, Is.EqualTo("Id"));
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

        private static string CreateTempDir()
        {
            string dir = Path.Combine(
                Path.GetTempPath(), "xfw-luban-parser-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
