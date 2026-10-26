using System.IO;
using UnityEngine;
using XFramework.XAudio.Fmod;
#if UNITY_EDITOR
using UnityEditor.PackageManager;
#endif

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// 兼容三种布局的包根定位：仓库内 <c>Assets/XFramework.Fmod</c>、embedded <c>Packages/</c>、
    /// UPM <c>Library/PackageCache</c>（经 <c>PackageInfo</c>）。找不到时返回 <c>null</c>，守卫据此
    /// <c>Assert.Ignore</c>。
    /// </summary>
    internal static class FmodTestPaths
    {
        /// <summary>包根绝对路径；找不到为 <c>null</c>。</summary>
        internal static string PackageRoot
        {
            get
            {
                string repoRoot = Path.Combine(Application.dataPath, "XFramework.Fmod");
                if (Directory.Exists(repoRoot))
                    return repoRoot;

#if UNITY_EDITOR
                var info = PackageInfo.FindForAssembly(typeof(FmodAudioSettings).Assembly);
                if (info != null && Directory.Exists(info.resolvedPath))
                    return info.resolvedPath;
#endif

                return null;
            }
        }

        /// <summary>包根下的相对路径；包根不存在时返回 <c>null</c>。</summary>
        internal static string Combine(string relativePath)
        {
            string root = PackageRoot;
            return root == null ? null : Path.Combine(root, relativePath);
        }

        /// <summary>读取包内文本文件；包根不存在时返回 <c>null</c>。</summary>
        internal static string ReadText(string relativePath)
        {
            string path = Combine(relativePath);
            return path != null && File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
