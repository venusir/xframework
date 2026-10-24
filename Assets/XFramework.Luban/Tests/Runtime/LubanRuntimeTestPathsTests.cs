using System;
using System.IO;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// <see cref="LubanRuntimeTestPaths"/> 的布局探测用例（临时目录）。
    /// </summary>
    [TestFixture]
    public sealed class LubanRuntimeTestPathsTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(
                Path.GetTempPath(), "xfw-luban-runtime-paths-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        [Test]
        public void FindPackageRoot_InRepoLayout_ReturnsAssetsPackage()
        {
            string assets = Path.Combine(_projectRoot, "Assets");
            string package = Path.Combine(assets, "XFramework.Luban");
            Directory.CreateDirectory(package);

            Assert.That(LubanRuntimeTestPaths.FindPackageRoot(_projectRoot, assets),
                Is.EqualTo(package));
        }

        [Test]
        public void FindPackageRoot_EmbeddedPackage_ReturnsPackagesPath()
        {
            string assets = Path.Combine(_projectRoot, "Assets");
            Directory.CreateDirectory(assets);
            string package = Path.Combine(
                _projectRoot, "Packages", "com.venusy609.xframework.luban");
            Directory.CreateDirectory(package);

            Assert.That(LubanRuntimeTestPaths.FindPackageRoot(_projectRoot, assets),
                Is.EqualTo(package));
        }

        [Test]
        public void FindPackageRoot_HashedPackageCache_ReturnsMatchingDirectory()
        {
            string assets = Path.Combine(_projectRoot, "Assets");
            Directory.CreateDirectory(assets);
            string package = Path.Combine(
                _projectRoot, "Library", "PackageCache",
                "com.venusy609.xframework.luban@abc123");
            Directory.CreateDirectory(Path.Combine(package, "Runtime"));
            File.WriteAllText(Path.Combine(package, "package.json"), "{}");
            File.WriteAllText(
                Path.Combine(package, "Runtime", "ILubanConfigAdapter.cs"), string.Empty);

            Assert.That(LubanRuntimeTestPaths.FindPackageRoot(_projectRoot, assets),
                Is.EqualTo(package));
        }

        [Test]
        public void FindPackageRoot_Missing_ThrowsWithCheckedPaths()
        {
            string assets = Path.Combine(_projectRoot, "Assets");
            Directory.CreateDirectory(assets);

            var exception = Assert.Throws<DirectoryNotFoundException>(
                () => LubanRuntimeTestPaths.FindPackageRoot(_projectRoot, assets));

            Assert.That(exception.Message, Does.Contain("Assets"));
            Assert.That(exception.Message, Does.Contain("Packages"));
            Assert.That(exception.Message, Does.Contain(
                Path.Combine("Library", "PackageCache")));
        }
    }
}
