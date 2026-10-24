using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// 接缝/适配器文本生成用例（基于真实 5.1.0 Bin fixtures）。
    /// </summary>
    [TestFixture]
    public sealed class LubanGlueEmitterTests
    {
        private LubanGeneratedModel _model;
        private LubanGeneratorSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _model = LubanCodeParser.Parse(
                Path.Combine(LubanTestPaths.FixturesRoot, "Bin"), "Tables");
            _settings = ScriptableObject.CreateInstance<LubanGeneratorSettings>();
            _settings.CodeTarget = LubanCodeTarget.CsBin;
            _settings.DataTarget = LubanDataTarget.Bin;
            _settings.AdapterNamespace = "XFramework.XConfig.Luban.Generated";
            _settings.AdapterClassName = "GeneratedLubanConfigAdapter";
            _settings.GenerateSeams = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);
        }

        [Test]
        public void EmitSeams_OnlyKeyedTables()
        {
            var files = LubanGlueEmitter.Emit(_model, _settings);

            Assert.That(files.ContainsKey("Seams/cfg_demo_Item.g.cs"), Is.True);
            Assert.That(files.ContainsKey("Seams/cfg_demo_Monster.g.cs"), Is.True);
            Assert.That(files.ContainsKey("Seams/cfg_demo_SkillLevel.g.cs"), Is.True);
            Assert.That(files.ContainsKey("Seams/cfg_demo_Global.g.cs"), Is.False);
            Assert.That(files.ContainsKey("Seams/cfg_demo_SpawnPoint.g.cs"), Is.False);

            string item = files["Seams/cfg_demo_Item.g.cs"];
            Assert.That(item, Does.Contain("partial class Item : global::XFramework.XConfig.IConfigRow<int>"));
            Assert.That(item, Does.Contain("=> Id;"));

            string skill = files["Seams/cfg_demo_SkillLevel.g.cs"];
            Assert.That(skill, Does.Contain("IConfigRow<(int, int)>"));
            Assert.That(skill, Does.Contain("=> (SkillId, Level);"));
        }

        [Test]
        public void EmitAdapter_Bin_ContainsExpectedRegistration()
        {
            string adapter = LubanGlueEmitter.EmitAdapter(_model, _settings);

            Assert.That(adapter, Does.Contain("public sealed class GeneratedLubanConfigAdapter"));
            Assert.That(adapter, Does.Contain("DataFileExtension => \".bytes\""));
            Assert.That(adapter, Does.Contain(
                "new global::cfg.Tables(name => new global::Luban.ByteBuf(loadBytes(name)))"));
            Assert.That(adapter, Does.Contain("RegisterTable(t.TbItem.DataMap);"));
            Assert.That(adapter, Does.Contain("RegisterGlobal(t.TbGlobal.Data);"));
            Assert.That(adapter, Does.Contain(
                "RegisterTable(t.TbMonster.DataList, row => row.Id);"));
            Assert.That(adapter, Does.Contain(
                "RegisterTable(t.TbSkillLevel.DataList, row => (row.SkillId, row.Level));"));
            Assert.That(adapter, Does.Contain(
                "// TbSpawnPoint：纯列表表，无主键，未注册到 ConfigManager"));
            Assert.That(adapter, Does.Contain("Unload<global::cfg.demo.Item>();"));
        }

        [Test]
        public void EmitAdapter_Json_UsesSimpleJson()
        {
            _settings.CodeTarget = LubanCodeTarget.CsSimpleJson;
            _settings.DataTarget = LubanDataTarget.Json;

            string adapter = LubanGlueEmitter.EmitAdapter(_model, _settings);

            Assert.That(adapter, Does.Contain("DataFileExtension => \".json\""));
            Assert.That(adapter, Does.Contain(
                "global::Luban.SimpleJSON.JSON.Parse(" +
                "global::System.Text.Encoding.UTF8.GetString(loadBytes(name)))"));
        }

        [Test]
        public void Emit_IsDeterministic()
        {
            var first = LubanGlueEmitter.Emit(_model, _settings);
            var second = LubanGlueEmitter.Emit(_model, _settings);

            Assert.That(second.Count, Is.EqualTo(first.Count));
            foreach (var pair in first)
            {
                Assert.That(second.ContainsKey(pair.Key), Is.True);
                Assert.That(second[pair.Key], Is.EqualTo(pair.Value));
            }
        }

        [Test]
        public void Emit_GenerateSeamsFalse_OnlyAdapter()
        {
            _settings.GenerateSeams = false;

            var files = LubanGlueEmitter.Emit(_model, _settings);

            Assert.That(files.Count, Is.EqualTo(1));
            Assert.That(files.ContainsKey("Adapter/GeneratedLubanConfigAdapter.g.cs"), Is.True);
        }
    }
}
