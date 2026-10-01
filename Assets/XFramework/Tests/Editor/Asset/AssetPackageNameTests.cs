using NUnit.Framework;
using XFramework.XAsset;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 主包名解析：<c>AssetInitOptions.PackageName</c> 非空时用它，否则回落默认包。
    /// <para><b>为什么单独一个 fixture</b>：这条解析决定「初始化哪个包」与「加载族作用于哪个包」是否
    /// 同一个名字。两者一旦不一致，表现是「初始化成功、之后每次加载失败」——而它此前**零覆盖**，
    /// 因为还涉及真实 YooAsset 环境。提成纯函数后，这条语义可以在这里钉死。</para>
    /// </summary>
    [TestFixture]
    public class AssetPackageNameTests
    {
        [Test]
        public void NullOptions_FallsBackToDefaultPackage()
        {
            Assert.AreEqual("DefaultPackage", AssetManagerImpl.ResolveDefaultPackageName(null));
        }

        [Test]
        public void EmptyOrWhitespaceName_FallsBackToDefaultPackage()
        {
            Assert.AreEqual("DefaultPackage",
                AssetManagerImpl.ResolveDefaultPackageName(new AssetInitOptions { PackageName = null }));
            Assert.AreEqual("DefaultPackage",
                AssetManagerImpl.ResolveDefaultPackageName(new AssetInitOptions { PackageName = "" }));
            Assert.AreEqual("DefaultPackage",
                AssetManagerImpl.ResolveDefaultPackageName(new AssetInitOptions { PackageName = "   " }));
        }

        [Test]
        public void NonEmptyName_Wins()
        {
            Assert.AreEqual("MyPack",
                AssetManagerImpl.ResolveDefaultPackageName(new AssetInitOptions { PackageName = "MyPack" }));
        }
    }
}
