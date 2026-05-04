// -----------------------------------------------------------------------------
// XFrameworkDependencyInstaller
// 
// 当第三方项目通过 Git URL 引入 XFramework 时，package.json 中的 dependencies
// 不支持 Git URL 格式（Unity 要求 Semantic Versioning）。因此 XFramework 的
// package.json 中不声明第三方依赖，而是通过此 Editor 脚本提供一键安装功能。
//
// 本脚本处理两类依赖：
// 1. UPM 包（UniTask、YooAsset、NuGetForUnity）→ 写入 Packages/manifest.json
// 2. NuGet 包（R3）→ 写入 Assets/packages.config
//
// 使用方式: 菜单栏 -> XFramework -> Install Dependencies
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Xml.Linq;

namespace Venusy609.Xframework.Editor
{
    /// <summary>
    /// 提供一键安装 XFramework 所需第三方依赖的 Editor 工具。
    /// </summary>
    public static class XFrameworkDependencyInstaller
    {
        #region UPM Dependencies (写入 Packages/manifest.json)

        private static readonly Dictionary<string, string> RequiredUPMDependencies = new Dictionary<string, string>
        {
            { "com.cysharp.unitask", "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask" },
            { "com.tuyoogame.yooasset", "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset" },
            { "com.github-glitchenzo.nugetforunity", "https://github.com/GlitchEnzo/NuGetForUnity.git?path=src/NuGetForUnity" },
        };

        private const string ManifestPath = "Packages/manifest.json";

        #endregion

        #region NuGet Dependencies (写入 Assets/packages.config)

        /// <summary>
        /// R3 及其依赖的 NuGet 包列表。
        /// 通过 NuGetForUnity 安装，写入 Assets/packages.config。
        /// </summary>
        private static readonly Dictionary<string, string> RequiredNuGetDependencies = new Dictionary<string, string>
        {
            { "R3", "1.2.9" },
            { "Microsoft.Bcl.AsyncInterfaces", "6.0.0" },
            { "Microsoft.Bcl.TimeProvider", "8.0.0" },
            { "System.ComponentModel.Annotations", "5.0.0" },
            { "System.Runtime.CompilerServices.Unsafe", "6.0.0" },
            { "System.Threading.Channels", "8.0.0" },
        };

        private const string PackagesConfigPath = "Assets/packages.config";

        #endregion

        [MenuItem("XFramework/Install Dependencies")]
        private static void InstallDependencies()
        {
            InstallUPMDependencies();
            InstallNuGetDependencies();
            AssetDatabase.Refresh();
            Debug.Log("[XFramework] 依赖安装完成！请等待 Unity 解析包。");
        }

        #region UPM Installation

        private static void InstallUPMDependencies()
        {
            string fullPath = Path.GetFullPath(ManifestPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"找不到 manifest.json 文件: {fullPath}");
                return;
            }

            string json = File.ReadAllText(fullPath);
            bool changed = false;

            foreach (var kvp in RequiredUPMDependencies)
            {
                string packageId = kvp.Key;
                string packageUrl = kvp.Value;

                if (json.Contains($"\"{packageId}\""))
                {
                    Debug.Log($"[XFramework] UPM 依赖已存在: {packageId}");
                    continue;
                }

                string searchPattern = "\"dependencies\": {";
                int insertIndex = json.IndexOf(searchPattern);
                if (insertIndex < 0)
                {
                    Debug.LogError($"manifest.json 格式异常，找不到 dependencies 节点");
                    return;
                }

                insertIndex += searchPattern.Length;

                string afterDependencies = json.Substring(insertIndex).TrimStart();
                string indent = "\n    ";
                string newEntry;

                if (afterDependencies.StartsWith("}"))
                {
                    newEntry = $"{indent}\"{packageId}\": \"{packageUrl}\"";
                }
                else
                {
                    newEntry = $",\n    \"{packageId}\": \"{packageUrl}\"";
                }

                json = json.Insert(insertIndex, newEntry);
                changed = true;
                Debug.Log($"[XFramework] 已添加 UPM 依赖: {packageId} -> {packageUrl}");
            }

            if (changed)
            {
                File.WriteAllText(fullPath, json);
            }
            else
            {
                Debug.Log("[XFramework] 所有 UPM 依赖已存在，无需操作。");
            }
        }

        #endregion

        #region NuGet Installation

        private static void InstallNuGetDependencies()
        {
            string fullPath = Path.GetFullPath(PackagesConfigPath);

            // 加载或创建 packages.config
            XDocument doc;
            XElement packagesElement;

            if (File.Exists(fullPath))
            {
                doc = XDocument.Load(fullPath);
                packagesElement = doc.Root;
            }
            else
            {
                doc = new XDocument(new XElement("packages"));
                packagesElement = doc.Root;
            }

            bool changed = false;

            foreach (var kvp in RequiredNuGetDependencies)
            {
                string packageId = kvp.Key;
                string version = kvp.Value;

                // 检查是否已存在
                bool exists = false;
                foreach (var pkg in packagesElement.Elements("package"))
                {
                    string idAttr = pkg.Attribute("id")?.Value;
                    if (idAttr == packageId)
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    Debug.Log($"[XFramework] NuGet 依赖已存在: {packageId}");
                    continue;
                }

                var newPackage = new XElement("package",
                    new XAttribute("id", packageId),
                    new XAttribute("version", version),
                    new XAttribute("manuallyInstalled", packageId == "R3" ? "true" : "false")
                );
                packagesElement.Add(newPackage);
                changed = true;
                Debug.Log($"[XFramework] 已添加 NuGet 依赖: {packageId} v{version}");
            }

            if (changed)
            {
                doc.Save(fullPath);
                Debug.Log("[XFramework] NuGet 依赖已写入 packages.config。请确保已安装 NuGetForUnity，然后点击菜单栏 NuGet → Restore 来下载 R3。");
            }
            else
            {
                Debug.Log("[XFramework] 所有 NuGet 依赖已存在，无需操作。");
            }
        }

        #endregion

        [MenuItem("XFramework/Install Dependencies", true)]
        private static bool ValidateInstallDependencies()
        {
            return true;
        }
    }
}
