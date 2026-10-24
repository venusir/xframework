using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor.Tests
{
    /// <summary>
    /// 同时兼容仓内 <c>Assets/XFramework.Luban</c>、embedded 包与消费方
    /// <c>Library/PackageCache</c> 三种布局。
    /// </summary>
    internal static class LubanTestPaths
    {
        internal static string PackageRoot => FindPackageRoot();

        internal static string FixturesRoot =>
            Path.Combine(PackageRoot, "Tests", "Editor", "Fixtures~");

        internal static string FindPackageRoot()
        {
            PackageInfo info = PackageInfo.FindForAssembly(typeof(LubanConfig).Assembly);
            if (info != null &&
                !string.IsNullOrEmpty(info.resolvedPath) &&
                Directory.Exists(info.resolvedPath))
            {
                return info.resolvedPath;
            }

            return FindPackageRoot(
                Path.GetDirectoryName(Application.dataPath), Application.dataPath);
        }

        internal static string FindPackageRoot(string projectRoot, string assetsPath)
        {
            var candidates = new List<string>();

            string underAssets = Path.Combine(assetsPath, "XFramework.Luban");
            candidates.Add(underAssets);
            if (Directory.Exists(underAssets))
            {
                return underAssets;
            }

            string embedded = Path.Combine(
                projectRoot, "Packages", "com.venusy609.xframework.luban");
            candidates.Add(embedded);
            if (Directory.Exists(embedded))
            {
                return embedded;
            }

            string cacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
            candidates.Add(cacheRoot);
            if (Directory.Exists(cacheRoot))
            {
                string[] directories = Directory.GetDirectories(
                    cacheRoot, "com.venusy609.xframework.luban*");
                Array.Sort(directories, StringComparer.Ordinal);
                for (int i = 0; i < directories.Length; i++)
                {
                    candidates.Add(directories[i]);
                    if (File.Exists(Path.Combine(directories[i], "package.json")) &&
                        File.Exists(Path.Combine(
                            directories[i], "Runtime", "ILubanConfigAdapter.cs")))
                    {
                        return directories[i];
                    }
                }
            }

            throw new DirectoryNotFoundException(
                "未找到 XFramework.Luban 包目录。已检查：" + string.Join("；", candidates));
        }
    }
}
