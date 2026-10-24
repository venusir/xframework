using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// 行类型唯一性、单行 struct 与 asmdef 同域/引用校验用例。
    /// </summary>
    [TestFixture]
    public sealed class LubanProjectValidatorTests
    {
        private LubanGeneratorSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<LubanGeneratorSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void ValidateModel_DuplicateRows_ReportsError()
        {
            var model = new LubanGeneratedModel();
            model.Tables.Add(MapTable("TbA", "cfg.Row"));
            model.Tables.Add(MapTable("TbB", "cfg.Row"));

            Assert.That(LubanProjectValidator.ValidateModel(model), Is.Not.Empty);
        }

        [Test]
        public void ValidateModel_SingleRowStruct_ReportsError()
        {
            var model = new LubanGeneratedModel();
            model.Tables.Add(new LubanGeneratedTable
            {
                PropertyName = "TbA",
                Shape = LubanTableShape.SingleRow,
                RowTypeFullName = "cfg.Row",
                RowIsClass = false,
            });

            Assert.That(LubanProjectValidator.ValidateModel(model), Is.Not.Empty);
        }

        [Test]
        public void ValidateAsmdefScope_MissingReferences_ReportsErrors()
        {
            string root = CreateTempProject(
                "{\"name\":\"Game\",\"references\":[],\"includePlatforms\":[]}");

            try
            {
                var errors = LubanProjectValidator.ValidateAsmdefScope(_settings, root);
                string joined = string.Join("\n", errors);

                Assert.That(joined, Does.Contain("Venusy609.Xframework"));
                Assert.That(joined, Does.Contain("Venusy609.Xframework.Luban"));
                Assert.That(joined, Does.Contain("Luban.Runtime"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ValidateAsmdefScope_ValidReferences_NoErrors()
        {
            string root = CreateTempProject(
                "{\"name\":\"Game\",\"references\":[" +
                "\"Venusy609.Xframework\",\"Venusy609.Xframework.Luban\",\"Luban.Runtime\"]," +
                "\"includePlatforms\":[]}");

            try
            {
                Assert.That(LubanProjectValidator.ValidateAsmdefScope(_settings, root), Is.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ValidateAsmdefScope_EditorOnly_ReportsError()
        {
            string root = CreateTempProject(
                "{\"name\":\"Game\",\"references\":[" +
                "\"Venusy609.Xframework\",\"Venusy609.Xframework.Luban\",\"Luban.Runtime\"]," +
                "\"includePlatforms\":[\"Editor\"]}");

            try
            {
                Assert.That(LubanProjectValidator.ValidateAsmdefScope(_settings, root), Is.Not.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ValidateAsmdefScope_DualPlatform_NoErrors()
        {
            string root = CreateTempProject(
                "{\"name\":\"Game\",\"references\":[" +
                "\"Venusy609.Xframework\",\"Venusy609.Xframework.Luban\",\"Luban.Runtime\"]," +
                "\"includePlatforms\":[\"Editor\",\"StandaloneWindows64\"]}");

            try
            {
                Assert.That(
                    LubanProjectValidator.ValidateAsmdefScope(_settings, root), Is.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ValidateAsmdefScope_ExcludePlatforms_Warns()
        {
            string root = CreateTempProject(
                "{\"name\":\"Game\",\"references\":[" +
                "\"Venusy609.Xframework\",\"Venusy609.Xframework.Luban\",\"Luban.Runtime\"]," +
                "\"includePlatforms\":[],\"excludePlatforms\":[\"StandaloneWindows64\"]}");

            try
            {
                var warnings = new List<string>();
                var errors = LubanProjectValidator.ValidateAsmdefScope(_settings, root, warnings);

                Assert.That(errors, Is.Empty);
                Assert.That(warnings, Has.Some.Contains("excludePlatforms"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ValidateAsmdefScope_DefineConstraints_Warns()
        {
            string root = CreateTempProject(
                "{\"name\":\"Game\",\"references\":[" +
                "\"Venusy609.Xframework\",\"Venusy609.Xframework.Luban\",\"Luban.Runtime\"]," +
                "\"includePlatforms\":[],\"defineConstraints\":[\"DEV_BUILD\"]}");

            try
            {
                var warnings = new List<string>();
                var errors = LubanProjectValidator.ValidateAsmdefScope(_settings, root, warnings);

                Assert.That(errors, Is.Empty);
                Assert.That(warnings, Has.Some.Contains("defineConstraints"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ValidateAsmdefScope_DifferentAsmdefs_ReportsError()
        {
            string root = CreateTempProject(null);
            try
            {
                string codeDir = Path.Combine(root, "Assets", "Gen", "Code");
                string integrationDir = Path.Combine(root, "Assets", "Gen", "Integration");
                Directory.CreateDirectory(codeDir);
                Directory.CreateDirectory(integrationDir);
                File.WriteAllText(Path.Combine(codeDir, "Code.asmdef"),
                    "{\"name\":\"Code\",\"references\":[]}");
                File.WriteAllText(Path.Combine(integrationDir, "Integration.asmdef"),
                    "{\"name\":\"Integration\",\"references\":[]}");

                Assert.That(LubanProjectValidator.ValidateAsmdefScope(_settings, root), Is.Not.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private string CreateTempProject(string asmdefContent)
        {
            string root = Path.Combine(
                Path.GetTempPath(), "xfw-luban-asmdef-" + Guid.NewGuid().ToString("N"));
            string codeDir = Path.Combine(root, "Assets", "Gen", "Code");
            string integrationDir = Path.Combine(root, "Assets", "Gen", "Integration");
            Directory.CreateDirectory(codeDir);
            Directory.CreateDirectory(integrationDir);

            if (asmdefContent != null)
            {
                File.WriteAllText(Path.Combine(root, "Assets", "Gen", "Game.asmdef"), asmdefContent);
            }

            _settings.CodeOutputDir = "Assets/Gen/Code";
            _settings.DataOutputDir = "Assets/Gen/Data";
            _settings.IntegrationOutputDir = "Assets/Gen/Integration";
            return root;
        }

        private static LubanGeneratedTable MapTable(string property, string row)
        {
            return new LubanGeneratedTable
            {
                PropertyName = property,
                Shape = LubanTableShape.Map,
                RowTypeFullName = row,
                RowIsClass = true,
                KeyTypeText = "int",
            };
        }
    }
}
