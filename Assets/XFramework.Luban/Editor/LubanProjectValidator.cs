using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// 接入代码落盘前的结构校验：行类型唯一性、单行表 class、行接口实现与生成代码同 asmdef、引用完整。
    /// </summary>
    internal static class LubanProjectValidator
    {
        private static readonly string[] RequiredReferences =
        {
            "Venusy609.Xframework",
            "Venusy609.Xframework.Luban",
            "Luban.Runtime",
        };

        internal static List<string> ValidateModel(LubanGeneratedModel model)
        {
            var errors = new List<string>();
            var registeredRows = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var table in model.Tables)
            {
                if (table.Shape == LubanTableShape.PureList)
                {
                    continue;
                }

                if (table.Shape == LubanTableShape.SingleRow && !table.RowIsClass)
                {
                    errors.Add("单行表 '" + table.PropertyName + "' 的行类型 '" +
                               table.RowTypeFullName + "' 是 struct，无法注册为 Global。");
                }

                if (table.Shape != LubanTableShape.SingleRow &&
                    string.IsNullOrEmpty(table.KeyTypeText))
                {
                    errors.Add("表 '" + table.PropertyName + "' 缺少主键类型。");
                }

                if (registeredRows.TryGetValue(table.RowTypeFullName, out string other))
                {
                    errors.Add("行类型 '" + table.RowTypeFullName + "' 同时被表 '" +
                               other + "' 与 '" + table.PropertyName +
                               "' 使用；ConfigManager 按行类型索引，无法区分。");
                }
                else
                {
                    registeredRows.Add(table.RowTypeFullName, table.PropertyName);
                }
            }

            return errors;
        }

        internal static List<string> ValidateAsmdefScope(
            LubanGeneratorSettings settings,
            string projectRoot)
        {
            var errors = new List<string>();
            if (settings == null)
            {
                errors.Add("设置对象为 null。");
                return errors;
            }

            string codeDir = ResolvePath(settings.CodeOutputDir, projectRoot);
            string integrationDir = ResolvePath(settings.IntegrationOutputDir, projectRoot);

            string codeAsmdef = FindNearestAsmdef(codeDir, projectRoot, errors);
            string integrationAsmdef = FindNearestAsmdef(integrationDir, projectRoot, errors);

            if (!string.Equals(codeAsmdef, integrationAsmdef, PathComparison))
            {
                errors.Add(
                    "行接口实现与 Luban 生成代码必须处于同一个 asmdef 作用域：" +
                    "代码目录=" + (codeAsmdef ?? "Assembly-CSharp") +
                    "，接入代码目录=" + (integrationAsmdef ?? "Assembly-CSharp") + "。");
                return errors;
            }

            if (codeAsmdef == null)
            {
                return errors;
            }

            AsmdefInfo info = ReadAsmdef(codeAsmdef, errors);
            if (info == null)
            {
                return errors;
            }

            if (info.includePlatforms != null && Array.IndexOf(info.includePlatforms, "Editor") >= 0)
            {
                errors.Add("生成代码所在 asmdef '" + info.name +
                           "' 是 Editor-only；Luban 配置必须在运行时可用。");
            }

            var references = ResolveReferences(info, errors);
            foreach (var required in RequiredReferences)
            {
                if (!references.Contains(required))
                {
                    errors.Add("asmdef '" + (info.name ?? codeAsmdef) +
                               "' 缺少直接引用 '" + required + "'。");
                }
            }

            return errors;
        }

        internal static List<string> ValidateForIntegration(
            LubanGeneratorSettings settings,
            LubanGeneratedModel model,
            string projectRoot,
            List<string> warnings = null)
        {
            var errors = ValidateModel(model);
            errors.AddRange(ValidateAsmdefScope(settings, projectRoot));
            errors.AddRange(ValidateLoaderAndData(settings, model, projectRoot, warnings));
            return errors;
        }

        private static List<string> ValidateLoaderAndData(
            LubanGeneratorSettings settings,
            LubanGeneratedModel model,
            string projectRoot,
            List<string> warnings)
        {
            var errors = new List<string>();
            if (settings == null)
            {
                return errors;
            }

            LubanLoaderKind expected = settings.CodeTarget == LubanCodeTarget.CsBin
                ? LubanLoaderKind.ByteBuf
                : LubanLoaderKind.JsonNode;
            if (model.LoaderKind != LubanLoaderKind.Unknown && model.LoaderKind != expected)
            {
                errors.Add(
                    "CodeTarget 与生成代码的 loader 类型不匹配：CodeTarget=" +
                    (settings.CodeTarget == LubanCodeTarget.CsBin ? "cs-bin" : "cs-simple-json") +
                    "，Tables 的 loader 是 '" + LoaderName(model.LoaderKind) +
                    "'。请重新生成代码或修改 CodeTarget。");
            }

            if (string.IsNullOrWhiteSpace(settings.DataOutputDir))
            {
                return errors;
            }

            string dataDir;
            try
            {
                dataDir = ResolvePath(settings.DataOutputDir, projectRoot);
            }
            catch (Exception ex)
            {
                errors.Add("数据输出目录路径无效：" + ex.Message);
                return errors;
            }

            if (!Directory.Exists(dataDir))
            {
                warnings?.Add("数据输出目录不存在，跳过数据文件校验：" + dataDir);
                return errors;
            }

            string extension = settings.DataTarget == LubanDataTarget.Bin ? ".bytes" : ".json";
            for (int i = 0; i < model.Tables.Count; i++)
            {
                var table = model.Tables[i];
                string path = Path.Combine(dataDir, table.OutputDataName + extension);
                if (!File.Exists(path))
                {
                    errors.Add("表 '" + table.PropertyName + "' 的数据文件不存在：" + path);
                }
            }

            return errors;
        }

        private static string LoaderName(LubanLoaderKind kind)
        {
            return kind switch
            {
                LubanLoaderKind.ByteBuf => "ByteBuf",
                LubanLoaderKind.JsonNode => "JSONNode",
                _ => "Unknown",
            };
        }

        private static string FindNearestAsmdef(
            string directory,
            string projectRoot,
            List<string> errors)
        {
            string root = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = Path.GetFullPath(directory);

            if (!IsUnderOrEqual(current, root))
            {
                errors.Add("目录不在项目根目录内：" + directory);
                return null;
            }

            while (current != null)
            {
                if (Directory.Exists(current))
                {
                    string[] asmdefs = Directory.GetFiles(
                        current, "*.asmdef", SearchOption.TopDirectoryOnly);

                    if (asmdefs.Length > 1)
                    {
                        errors.Add("目录下有多个 asmdef，无法确定所属程序集：" + current);
                        return null;
                    }

                    if (asmdefs.Length == 1)
                    {
                        return asmdefs[0];
                    }
                }

                if (string.Equals(current, root, PathComparison))
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
            }

            return null;
        }

        private static AsmdefInfo ReadAsmdef(string path, List<string> errors)
        {
            try
            {
                var info = JsonUtility.FromJson<AsmdefInfo>(File.ReadAllText(path));
                if (info == null)
                {
                    errors.Add("无法读取 asmdef：" + path);
                }

                return info;
            }
            catch (Exception ex)
            {
                errors.Add("无法解析 asmdef '" + path + "'：" + ex.Message);
                return null;
            }
        }

        private static HashSet<string> ResolveReferences(
            AsmdefInfo info,
            List<string> errors)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (info.references == null)
            {
                return names;
            }

            foreach (var reference in info.references)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                if (!reference.StartsWith("GUID:", StringComparison.Ordinal))
                {
                    names.Add(reference);
                    continue;
                }

                string guid = reference.Substring(5);
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    errors.Add("无法解析 asmdef 的 GUID 引用：" + reference);
                    continue;
                }

                var referenced = ReadAsmdef(path, errors);
                if (referenced != null && !string.IsNullOrEmpty(referenced.name))
                {
                    names.Add(referenced.name);
                }
            }

            return names;
        }

        private static string ResolvePath(string path, string projectRoot)
        {
            return Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectRoot, path));
        }

        private static bool IsUnderOrEqual(string path, string root)
        {
            if (string.Equals(path, root, PathComparison))
            {
                return true;
            }

            string prefix = root + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, PathComparison);
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        [Serializable]
        internal sealed class AsmdefInfo
        {
            public string name;
            public string[] references;
            public string[] includePlatforms;
        }
    }
}
