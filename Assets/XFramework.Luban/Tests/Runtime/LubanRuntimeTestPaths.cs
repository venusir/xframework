using System.IO;
using UnityEngine;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 同时兼容仓内 <c>Assets/XFramework.Luban</c> 与消费方 PackageCache 两种布局。
    /// </summary>
    internal static class LubanRuntimeTestPaths
    {
        internal static string PackageRoot => FindPackageRoot();

        internal static string DataRoot =>
            Path.Combine(PackageRoot, "Tests", "Runtime", "Fixtures~", "Data");

        private static string FindPackageRoot()
        {
            string assets = Application.dataPath;
            string underAssets = Path.Combine(assets, "XFramework.Luban");
            if (Directory.Exists(underAssets))
            {
                return underAssets;
            }

            string projectRoot = Path.GetDirectoryName(assets);
            string packageCache = Path.Combine(
                projectRoot, "Packages", "com.venusy609.xframework.luban");
            if (Directory.Exists(packageCache))
            {
                return packageCache;
            }

            throw new DirectoryNotFoundException(
                "未找到 XFramework.Luban 包目录（已检查 Assets/ 与 Packages/）。");
        }
    }
}
