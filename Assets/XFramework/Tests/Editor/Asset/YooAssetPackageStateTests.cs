using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using YooAsset;
using XFramework.XAsset;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 热更入口对「包存在但初始化失败」的守卫（R4-AST-003）。
    /// <para>用反射注入一个未初始化 / 初始化失败的 <see cref="ResourcePackage"/>（其 internal 构造函数
    /// 只接收包名），不走真实 YooAsset 初始化。改前三个入口会落到上游
    /// <c>DebugCheckInitialize</c>（<c>[Conditional("DEBUG")]</c>）抛裸 <see cref="Exception"/>；
    /// 改后统一抛带包名的 <see cref="InvalidOperationException"/>。</para>
    /// </summary>
    [TestFixture]
    public class YooAssetPackageStateTests
    {
        private const string PackageName = "R4TestPackage";

        private YooAssetManagerImpl _impl;

        [SetUp]
        public void SetUp()
        {
            _impl = new YooAssetManagerImpl(PackageName);
        }

        [TearDown]
        public void TearDown()
        {
            _impl?.Destroy();
            _impl = null;
        }

        private static ResourcePackage CreatePackage(EOperationStatus status)
        {
            var ctor = typeof(ResourcePackage).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            Assert.IsNotNull(ctor, "ResourcePackage(string) 的 internal 构造函数不存在（上游改名了？）");

            var package = (ResourcePackage)ctor.Invoke(new object[] { PackageName });

            var statusField = typeof(ResourcePackage).GetField(
                "_initializeStatus", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(statusField, "_initializeStatus 字段不存在（上游改名了？）");
            statusField.SetValue(package, status);
            return package;
        }

        private void InjectPackage(EOperationStatus status)
        {
            var package = CreatePackage(status);
            var packagesField = typeof(YooAssetManagerImpl).GetField(
                "_packages", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(packagesField, "_packages 字段不存在（实现改名了？）");

            var packages = (Dictionary<string, ResourcePackage>)packagesField.GetValue(_impl);
            packages[PackageName] = package;
        }

        [Test]
        public void RequestPackageVersionAsync_PackageNotInitialized_ThrowsWithPackageName()
        {
            InjectPackage(EOperationStatus.None);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _impl.RequestPackageVersionAsync().GetAwaiter().GetResult());

            StringAssert.Contains(PackageName, ex.Message);
            StringAssert.Contains("not initialized", ex.Message);
            StringAssert.Contains("None", ex.Message);
        }

        [Test]
        public void UpdatePackageManifestAsync_PackageFailed_ThrowsWithPackageName()
        {
            InjectPackage(EOperationStatus.Failed);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _impl.UpdatePackageManifestAsync("1.0.0").GetAwaiter().GetResult());

            StringAssert.Contains(PackageName, ex.Message);
            StringAssert.Contains("Failed", ex.Message);
        }

        [Test]
        public void PreDownloadContentAsync_PackageNotInitialized_ThrowsWithPackageName()
        {
            InjectPackage(EOperationStatus.None);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _impl.PreDownloadContentAsync("1.0.0").GetAwaiter().GetResult());

            StringAssert.Contains(PackageName, ex.Message);
            StringAssert.Contains("not initialized", ex.Message);
        }
    }
}
