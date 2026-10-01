// -----------------------------------------------------------------------------
// XFrameworkDependencyInstaller
//
// 当第三方项目通过 Git URL 引入 XFramework 时，package.json 中的 dependencies
// 不支持 Git URL 格式（Unity 要求 Semantic Versioning）。因此 XFramework 的
// package.json 中不声明第三方依赖，而是通过此 Editor 脚本提供一键安装功能。
//
// 本脚本处理 UPM 包依赖（UniTask、YooAsset）→ 写入 Packages/manifest.json
//
// 使用方式: 菜单栏 -> XFramework -> Install Dependencies
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using XFramework.XLog;
using UnityEngine;
using System.IO;

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
        };

        private const string ManifestPath = "Packages/manifest.json";

        #endregion

        [MenuItem("XFramework/Install Dependencies")]
        private static void InstallDependencies()
        {
            InstallUPMDependencies();
            AssetDatabase.Refresh();
            LogManager.Info(LogCategories.XFramework, "依赖安装完成！请等待 Unity 解析包。");
        }

        #region UPM Installation

        private static void InstallUPMDependencies()
        {
            string fullPath = Path.GetFullPath(ManifestPath);
            if (!File.Exists(fullPath))
            {
                // 这一条与下面那条原本没有 [模块] 前缀（全仓唯二违例）；按「迁移逐字保真」本轮保持原样，
                // 是否补前缀见 Documentation/Modules/Log.md 的未决项。
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
                    LogManager.Info(LogCategories.XFramework, "UPM 依赖已存在: {0}", packageId);
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
                LogManager.Info(LogCategories.XFramework, "已添加 UPM 依赖: {0} -> {1}", packageId, packageUrl);
            }

            if (changed)
            {
                File.WriteAllText(fullPath, json);
            }
            else
            {
                LogManager.Info(LogCategories.XFramework, "所有 UPM 依赖已存在，无需操作。");
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
