// -----------------------------------------------------------------------------
// XFrameworkDependencyInstaller
//
// 当第三方项目通过 Git URL 引入 XFramework 时，package.json 中的 dependencies
// 不支持 Git URL 格式（Unity 要求 Semantic Versioning）。因此 XFramework 的
// package.json 中不声明第三方依赖，而是通过此 Editor 脚本提供一键安装功能。
//
// 本脚本处理 UPM 包依赖（UniTask、YooAsset、Newtonsoft.Json）→ 写入 Packages/manifest.json
//
// 使用方式: 菜单栏 -> XFramework -> Install Dependencies
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using XFramework.XLog;

namespace Venusy609.Xframework.Editor
{
    /// <summary>
    /// 提供一键安装 XFramework 所需第三方依赖的 Editor 工具。
    /// </summary>
    public static class XFrameworkDependencyInstaller
    {
        #region UPM Dependencies (写入 Packages/manifest.json)

        // 值的写法对两类包同形：Git URL 包写 URL，Unity 官方注册表包写版本号。
        // 这张清单的真相来源是 Assets/XFramework/README.md 的「第三方依赖」表，两处必须一起长——
        // Newtonsoft.Json 就曾漏在这里：清单写于它入仓之前，之后没跟着补。
        // 自 2026-10-11 起这条不再只靠人记：Tests/Editor/Architecture/DependencyDeclarationTests
        // 断言每条依赖在 README 与 Packages/manifest.json 里逐字出现，任一处漂移即红。
        //
        // Git URL 必须钉 #tag，且 ?path= 写在 # 之前。不写修订时包管理器会取**上游默认分支的最新提交**，
        // 而上游已经前进过：YooAsset 3.x 把 RawFileHandle 等类型移进了默认不编译的
        // #if YOOASSET_LEGACY_API 兼容层——README 有实测记录：裸 URL 装出来的工程 51 处
        // error CS0246/CS0234，钉住后退出码 0。
        private static readonly Dictionary<string, string> RequiredUPMDependencies = new Dictionary<string, string>
        {
            { "com.cysharp.unitask", "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11" },
            { "com.tuyoogame.yooasset", "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset#2.3.18" },
            { "com.unity.nuget.newtonsoft-json", "3.2.1" },
        };

        private const string ManifestPath = "Packages/manifest.json";

        #endregion

        [MenuItem("XFramework/Install Dependencies")]
        private static void InstallDependencies()
        {
            int mismatchCount = InstallUPMDependencies();
            AssetDatabase.Refresh();

            // 出错时上面已经报过，不能再补一条成功文案——那正是被修掉的假成功信号
            //（旧实现无论做了什么、有没有做全都打印「依赖安装完成！」）。
            if (mismatchCount < 0)
                return;

            if (mismatchCount == 0)
                LogManager.Info(LogCategories.XFramework, "依赖安装完成！请等待 Unity 解析包。");
            else
                LogManager.Warning(LogCategories.XFramework,
                    "依赖已处理，但有 {0} 条与 XFramework 期望的版本不一致，未自动改动（见上方逐条告警）。",
                    mismatchCount);
        }

        #region UPM Installation

        /// <summary>
        /// 把 <c>Packages/manifest.json</c> 的依赖补齐到 <see cref="RequiredUPMDependencies"/> 声明的状态。
        /// </summary>
        /// <returns>需要人工确认的条数；manifest 缺失或格式异常时返回 -1（已报错，调用方不得再报成功）。</returns>
        private static int InstallUPMDependencies()
        {
            string fullPath = Path.GetFullPath(ManifestPath);
            if (!File.Exists(fullPath))
            {
                // 这一条与下面那条原本没有 [模块] 前缀（全仓唯二违例）；按「迁移逐字保真」保持原样，
                // 是否补前缀见 Documentation/Modules/Log.md 的未决项。
                Debug.LogError($"找不到 manifest.json 文件: {fullPath}");
                return -1;
            }

            string json = File.ReadAllText(fullPath);
            bool changed = false;
            int mismatchCount = 0;

            foreach (var kvp in RequiredUPMDependencies)
            {
                string packageId = kvp.Key;
                string packageUrl = kvp.Value;

                // 判存在性要连**值**一起读：旧实现只判键名（json.Contains("\"包名\"")），于是
                // 照旧版 README 装过**裸 URL** 的消费方被永久跳过——此后无论怎么点菜单都不会被纠正
                // （见 Documentation/FrameworkReview.md §4.3 第 1 条）。
                Match entry = Regex.Match(json, $"\"{Regex.Escape(packageId)}\"\\s*:\\s*\"([^\"]*)\"");
                if (entry.Success)
                {
                    string current = entry.Groups[1].Value;
                    if (current == packageUrl)
                    {
                        LogManager.Info(LogCategories.XFramework, "UPM 依赖已存在: {0}", packageId);
                        continue;
                    }

                    // 值不同时，只有「已实测会装坏的形态」才自动纠正：无 # 修订的 Git URL。
                    if (IsUnpinnedGitUrl(current))
                    {
                        json = json.Remove(entry.Groups[1].Index, entry.Groups[1].Length)
                                   .Insert(entry.Groups[1].Index, packageUrl);
                        changed = true;
                        LogManager.Warning(LogCategories.XFramework,
                            "UPM 依赖 {0} 此前写入的是未钉修订的 Git URL，已更新为钉住版本 {1}——" +
                            "不钉修订时包管理器会取上游默认分支，Asset 模块会直接编译不过。",
                            packageId, packageUrl);
                        continue;
                    }

                    // 其余不一致一律不动：那可能是使用方有意的钉法，替人做决定比留一条告警更糟。
                    mismatchCount++;
                    LogManager.Warning(LogCategories.XFramework,
                        "UPM 依赖 {0} 的现值与本包期望不一致，未自动改动。现值：{1}，期望：{2}。" +
                        "若现值是你有意钉的版本可忽略本条，否则请手工改为期望值。",
                        packageId, current, packageUrl);
                    continue;
                }

                string searchPattern = "\"dependencies\": {";
                int insertIndex = json.IndexOf(searchPattern);
                if (insertIndex < 0)
                {
                    Debug.LogError($"manifest.json 格式异常，找不到 dependencies 节点");
                    return -1;
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
            else if (mismatchCount == 0)
            {
                LogManager.Info(LogCategories.XFramework, "所有 UPM 依赖已存在，无需操作。");
            }

            return mismatchCount;
        }

        /// <summary>
        /// Git URL 是否**没有钉修订**（<c>#</c> 之后为空/缺失）。这正是 README 实测会装坏的那一种：
        /// 注册表包的版本号（如 <c>3.2.1</c>）不含协议前缀，不会误判。
        /// </summary>
        private static bool IsUnpinnedGitUrl(string value)
        {
            return value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                   && value.IndexOf('#') < 0;
        }

        #endregion

        [MenuItem("XFramework/Install Dependencies", true)]
        private static bool ValidateInstallDependencies()
        {
            return true;
        }
    }
}
