using System.IO;
using UnityEngine;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 包根定位：支持仓内 Assets、embedded Packages 与 UPM PackageCache 三种布局。
    /// </summary>
    internal static class UIAuthoringTestPaths
    {
        internal const string PackageFolder = "XFramework.UIAuthoring";
        internal const string PackageName = "com.venusy609.xframework.ui.authoring";

        internal static string PackageRoot { get; } = Resolve();

        internal static bool IsRepoLayout
        {
            get
            {
                if (PackageRoot == null)
                    return false;
                string normalized = PackageRoot.Replace('\\', '/');
                return normalized.Contains("/Assets/" + PackageFolder);
            }
        }

        private static string Resolve()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            string assets = Path.Combine(projectRoot, "Assets", PackageFolder);
            if (Directory.Exists(assets))
                return assets;

            string embedded = Path.Combine(projectRoot, "Packages", PackageName);
            if (Directory.Exists(embedded))
                return embedded;

            string cache = Path.Combine(projectRoot, "Library", "PackageCache");
            if (Directory.Exists(cache))
            {
                string[] matches = Directory.GetDirectories(cache, PackageName + "@*");
                if (matches.Length > 0)
                    return matches[0];
            }

            return null;
        }
    }
}
