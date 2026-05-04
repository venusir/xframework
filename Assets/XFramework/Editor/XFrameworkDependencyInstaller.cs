// -----------------------------------------------------------------------------
// XFrameworkDependencyInstaller
// 
// 当第三方项目通过 Git URL 引入 XFramework 时，package.json 中的 dependencies
// 不支持 Git URL 格式（Unity 要求 Semantic Versioning）。因此 XFramework 的
// package.json 中不声明第三方依赖，而是通过此 Editor 脚本提供一键安装功能。
//
// 使用方式: 菜单栏 -> XFramework -> Install Dependencies
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

namespace Venusy609.Xframework.Editor
{
    /// <summary>
    /// 提供一键安装 XFramework 所需第三方依赖的 Editor 工具。
    /// 将依赖写入项目根目录的 Packages/manifest.json 中。
    /// </summary>
    public static class XFrameworkDependencyInstaller
    {
        private static readonly Dictionary<string, string> RequiredDependencies = new Dictionary<string, string>
        {
            { "com.cysharp.unitask", "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask" },
            { "com.tuyoogame.yooasset", "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset" },
            { "com.github-glitchenzo.nugetforunity", "https://github.com/GlitchEnzo/NuGetForUnity.git?path=src/NuGetForUnity" },
        };

        private const string ManifestPath = "Packages/manifest.json";

        [MenuItem("XFramework/Install Dependencies")]
        private static void InstallDependencies()
        {
            string fullPath = Path.GetFullPath(ManifestPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"找不到 manifest.json 文件: {fullPath}");
                return;
            }

            string json = File.ReadAllText(fullPath);
            bool changed = false;

            foreach (var kvp in RequiredDependencies)
            {
                string packageId = kvp.Key;
                string packageUrl = kvp.Value;

                // 检查是否已存在该依赖
                if (json.Contains($"\"{packageId}\""))
                {
                    Debug.Log($"[XFramework] 依赖已存在: {packageId}");
                    continue;
                }

                // 在 "dependencies": { 之后插入新的依赖
                string searchPattern = "\"dependencies\": {";
                int insertIndex = json.IndexOf(searchPattern);
                if (insertIndex < 0)
                {
                    Debug.LogError($"manifest.json 格式异常，找不到 dependencies 节点");
                    return;
                }

                insertIndex += searchPattern.Length;

                // 检查 dependencies 是否为空
                string afterDependencies = json.Substring(insertIndex).TrimStart();
                string indent = "\n    ";
                string newEntry;

                if (afterDependencies.StartsWith("}"))
                {
                    // 空的 dependencies
                    newEntry = $"{indent}\"{packageId}\": \"{packageUrl}\"";
                }
                else
                {
                    // 已有其他依赖，在最后一个条目后添加逗号和新条目
                    newEntry = $",\n    \"{packageId}\": \"{packageUrl}\"";
                }

                json = json.Insert(insertIndex, newEntry);
                changed = true;
                Debug.Log($"[XFramework] 已添加依赖: {packageId} -> {packageUrl}");
            }

            if (changed)
            {
                File.WriteAllText(fullPath, json);
                AssetDatabase.Refresh();
                Debug.Log("[XFramework] 依赖安装完成！请等待 Unity 解析包。");
            }
            else
            {
                Debug.Log("[XFramework] 所有依赖已存在，无需操作。");
            }
        }

        [MenuItem("XFramework/Install Dependencies", true)]
        private static bool ValidateInstallDependencies()
        {
            return true;
        }
    }
}
