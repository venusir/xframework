using System;
using System.Collections.Generic;
using System.IO;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// 设置的结构校验（不访问磁盘，便于单元测试）。
    /// </summary>
    internal static class LubanSettingsValidator
    {
        private static readonly string[] ForbiddenPrefixes =
        {
            "Packages/",
            "Library/",
            "ProjectSettings/",
            "Temp/",
            "Logs/",
            "Assets/XFramework/",
            "Assets/XFramework.Luban/",
        };

        internal static List<string> Validate(LubanGeneratorSettings settings)
        {
            var errors = new List<string>();
            if (settings == null)
            {
                errors.Add("设置对象为 null。");
                return errors;
            }

            Require(settings.DotnetPath, "dotnet 路径", errors);
            Require(settings.LubanDllPath, "Luban.dll 路径", errors);
            Require(settings.ConfPath, "luban.conf 路径", errors);
            Require(settings.TargetName, "target 名", errors);
            Require(settings.CodeOutputDir, "代码输出目录", errors);
            Require(settings.DataOutputDir, "数据输出目录", errors);
            Require(settings.GlueOutputDir, "胶水输出目录", errors);
            Require(settings.AdapterNamespace, "适配器命名空间", errors);
            Require(settings.AdapterClassName, "适配器类名", errors);

            if (!IsPaired(settings.CodeTarget, settings.DataTarget))
            {
                errors.Add("CodeTarget 与 DataTarget 不配套：cs-bin ↔ bin，cs-simple-json ↔ json。");
            }

            ValidateProjectPath(settings.ConfPath, "luban.conf 路径", requireAssets: false, errors);
            ValidateProjectPath(settings.CodeOutputDir, "代码输出目录", requireAssets: true, errors);
            ValidateProjectPath(settings.DataOutputDir, "数据输出目录", requireAssets: true, errors);
            ValidateProjectPath(settings.GlueOutputDir, "胶水输出目录", requireAssets: true, errors);

            if (!string.IsNullOrEmpty(settings.LubanDllPath))
            {
                ValidateProjectPath(settings.LubanDllPath, "Luban.dll 路径", requireAssets: false, errors);
            }

            if (IsNonEmpty(settings.CodeOutputDir) &&
                IsNonEmpty(settings.DataOutputDir) &&
                IsNonEmpty(settings.GlueOutputDir))
            {
                string code = Normalize(settings.CodeOutputDir);
                string data = Normalize(settings.DataOutputDir);
                string glue = Normalize(settings.GlueOutputDir);

                if (IsSubPath(glue, code))
                {
                    errors.Add("胶水输出目录不能位于代码输出目录内（Luban 会清理代码输出目录）。");
                }

                if (IsSubPath(glue, data))
                {
                    errors.Add("胶水输出目录不能位于数据输出目录内（Luban 会清理数据输出目录）。");
                }

                if (IsSubPath(code, glue) || IsSubPath(data, glue))
                {
                    errors.Add("代码/数据输出目录不能位于胶水输出目录内。");
                }

                if (string.Equals(code, glue, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(data, glue, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("代码/数据输出目录不能与胶水输出目录相同。");
                }
            }

            return errors;
        }

        internal static bool IsPaired(LubanCodeTarget codeTarget, LubanDataTarget dataTarget)
        {
            return codeTarget switch
            {
                LubanCodeTarget.CsBin => dataTarget == LubanDataTarget.Bin,
                LubanCodeTarget.CsSimpleJson => dataTarget == LubanDataTarget.Json,
                _ => false,
            };
        }

        internal static string Normalize(string path)
        {
            string normalized = path.Replace('\\', '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            return normalized.TrimEnd('/');
        }

        private static void Require(string value, string name, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(name + " 不能为空。");
            }
        }

        private static void ValidateProjectPath(
            string path,
            string name,
            bool requireAssets,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string normalized = Normalize(path);

            if (Path.IsPathRooted(normalized))
            {
                errors.Add(name + " 必须是项目相对路径，不能是绝对路径。");
                return;
            }

            foreach (var segment in normalized.Split('/'))
            {
                if (segment == "..")
                {
                    errors.Add(name + " 不能包含 '..'。");
                    return;
                }
            }

            foreach (var prefix in ForbiddenPrefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(name + " 不能位于受保护目录 '" + prefix + "' 下。");
                    return;
                }
            }

            if (requireAssets &&
                !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(name + " 必须位于 Assets/ 下。");
            }
        }

        private static bool IsNonEmpty(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool IsSubPath(string candidate, string parent)
        {
            string prefix = parent + "/";
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
