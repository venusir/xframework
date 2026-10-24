using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// 设置的结构校验（不访问磁盘，便于单元测试）。
    /// </summary>
    internal static class LubanSettingsValidator
    {
        private static readonly Regex IdentifierRegex =
            new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);

        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate",
            "do", "double", "else", "enum", "event", "explicit", "extern", "false",
            "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
            "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
            "new", "null", "object", "operator", "out", "override", "params", "private",
            "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        };

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

        internal static List<string> Validate(LubanGeneratorSettings settings, string projectRoot = null)
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
            Require(settings.TablesClassName, "Tables 类名", errors);
            Require(settings.CodeOutputDir, "代码输出目录", errors);
            Require(settings.DataOutputDir, "数据输出目录", errors);
            Require(settings.IntegrationOutputDir, "接入代码输出目录", errors);
            Require(settings.AdapterNamespace, "适配器命名空间", errors);
            Require(settings.AdapterClassName, "适配器类名", errors);

            ValidateIdentifier(settings.TablesClassName, "Tables 类名", errors);
            ValidateIdentifier(settings.AdapterClassName, "适配器类名", errors);
            ValidateNamespace(settings.AdapterNamespace, "适配器命名空间", errors);

            if (!IsPaired(settings.CodeTarget, settings.DataTarget))
            {
                errors.Add("CodeTarget 与 DataTarget 不配套：cs-bin ↔ bin，cs-simple-json ↔ json。");
            }

            ValidateProjectPath(settings.ConfPath, "luban.conf 路径", requireAssets: false, errors: errors, allowAbsolute: true);
            ValidateProjectPath(settings.CodeOutputDir, "代码输出目录", requireAssets: true, errors);
            ValidateProjectPath(settings.DataOutputDir, "数据输出目录", requireAssets: true, errors);
            ValidateProjectPath(settings.IntegrationOutputDir, "接入代码输出目录", requireAssets: true, errors);

            if (!string.IsNullOrEmpty(settings.LubanDllPath))
            {
                ValidateProjectPath(settings.LubanDllPath, "Luban.dll 路径", requireAssets: false, errors: errors, allowAbsolute: true);
            }

            if (IsNonEmpty(settings.CodeOutputDir) &&
                IsNonEmpty(settings.DataOutputDir) &&
                IsNonEmpty(settings.IntegrationOutputDir))
            {
                string code = Normalize(settings.CodeOutputDir);
                string data = Normalize(settings.DataOutputDir);
                string integration = Normalize(settings.IntegrationOutputDir);

                if (IsSubPath(integration, code))
                {
                    errors.Add("接入代码输出目录不能位于代码输出目录内（Luban 会清理代码输出目录）。");
                }

                if (IsSubPath(integration, data))
                {
                    errors.Add("接入代码输出目录不能位于数据输出目录内（Luban 会清理数据输出目录）。");
                }

                if (IsSubPath(code, integration) || IsSubPath(data, integration))
                {
                    errors.Add("代码/数据输出目录不能位于接入代码输出目录内。");
                }

                if (string.Equals(code, integration, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(data, integration, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("代码/数据输出目录不能与接入代码输出目录相同。");
                }
            }

            if (IsNonEmpty(settings.CodeOutputDir) && IsNonEmpty(settings.DataOutputDir))
            {
                string code = Normalize(settings.CodeOutputDir);
                string data = Normalize(settings.DataOutputDir);

                if (string.Equals(code, data, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("代码输出目录不能与数据输出目录相同。");
                }
                else if (IsSubPath(code, data))
                {
                    errors.Add("代码输出目录不能位于数据输出目录内（Luban 会清理数据输出目录）。");
                }
                else if (IsSubPath(data, code))
                {
                    errors.Add("数据输出目录不能位于代码输出目录内（Luban 会清理代码输出目录）。");
                }
            }

            ValidateToolInputOverlap(
                settings.LubanDllPath, "Luban.dll 路径",
                settings.CodeOutputDir, "代码输出目录", projectRoot, errors);
            ValidateToolInputOverlap(
                settings.LubanDllPath, "Luban.dll 路径",
                settings.DataOutputDir, "数据输出目录", projectRoot, errors);
            ValidateToolInputOverlap(
                settings.ConfPath, "luban.conf 路径",
                settings.CodeOutputDir, "代码输出目录", projectRoot, errors);
            ValidateToolInputOverlap(
                settings.ConfPath, "luban.conf 路径",
                settings.DataOutputDir, "数据输出目录", projectRoot, errors);
            ValidateToolInputOverlap(
                settings.DotnetPath, "dotnet 路径",
                settings.CodeOutputDir, "代码输出目录", projectRoot, errors);
            ValidateToolInputOverlap(
                settings.DotnetPath, "dotnet 路径",
                settings.DataOutputDir, "数据输出目录", projectRoot, errors);

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
            while (normalized.Contains("//"))
            {
                normalized = normalized.Replace("//", "/");
            }

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

        private static void ValidateIdentifier(string value, string name, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!IdentifierRegex.IsMatch(value))
            {
                errors.Add(name + " 不是合法的 C# 标识符。");
            }
            else if (Keywords.Contains(value))
            {
                errors.Add(name + " 不能是 C# 关键字。");
            }
        }

        private static void ValidateNamespace(string value, string name, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var segment in value.Split('.'))
            {
                ValidateIdentifier(segment, name, errors);
            }
        }

        private static void ValidateProjectPath(
            string path,
            string name,
            bool requireAssets,
            List<string> errors,
            bool allowAbsolute = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string normalized = Normalize(path);

            if (Path.IsPathRooted(normalized))
            {
                if (!allowAbsolute)
                {
                    errors.Add(name + " 必须是项目相对路径，不能是绝对路径。");
                }

                // 绝对路径既不适用受保护前缀，也不要求位于 Assets/ 下；
                // 与输出目录的重叠由 Validate 的全路径比较兜住。
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
                if (string.Equals(normalized, prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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

        private static void ValidateToolInputOverlap(
            string toolPath,
            string toolName,
            string outputDir,
            string outputName,
            string projectRoot,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(toolPath) || string.IsNullOrWhiteSpace(outputDir))
            {
                return;
            }

            // 裸工具名（如 dotnet）不是文件路径，不参与重叠检查。
            if (!Path.IsPathRooted(toolPath) &&
                toolPath.IndexOf('/') < 0 &&
                toolPath.IndexOf('\\') < 0)
            {
                return;
            }

            string tool = ToComparisonPath(toolPath, projectRoot);
            string output = ToComparisonPath(outputDir, projectRoot);
            if (IsSameOrSubPath(tool, output))
            {
                errors.Add(toolName + " 不能位于" + outputName + "内（Luban 会清理" + outputName + "）。");
            }
        }

        private static string ToComparisonPath(string path, string projectRoot)
        {
            string normalized = Normalize(path);
            if (Path.IsPathRooted(normalized))
            {
                return NormalizeAbsolute(Path.GetFullPath(normalized));
            }

            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                return NormalizeAbsolute(Path.GetFullPath(Path.Combine(projectRoot, normalized)));
            }

            return normalized;
        }

        private static string NormalizeAbsolute(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static bool IsSameOrSubPath(string candidate, string parent)
        {
            return string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase) ||
                   IsSubPath(candidate, parent);
        }

        private static bool IsSubPath(string candidate, string parent)
        {
            string prefix = parent + "/";
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
