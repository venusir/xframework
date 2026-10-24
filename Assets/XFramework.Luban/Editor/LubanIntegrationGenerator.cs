using System;
using System.Collections.Generic;
using System.IO;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// 接入代码生成结果。
    /// </summary>
    internal sealed class LubanIntegrationResult
    {
        internal bool Succeeded;
        internal LubanGeneratedModel Model;
        internal List<string> Errors { get; } = new();
        internal List<string> Warnings { get; } = new();
        internal List<string> Files { get; } = new();
        internal string AdapterPath;
    }

    /// <summary>
    /// 解析 → 校验 → 生成行接口实现/适配器 → 写盘（或只校验）。
    /// </summary>
    internal static class LubanIntegrationGenerator
    {
        internal static LubanIntegrationResult Generate(
            LubanGeneratorSettings settings,
            string projectRoot,
            bool write)
        {
            var result = new LubanIntegrationResult();

            var settingsErrors = LubanSettingsValidator.Validate(settings, projectRoot);
            if (settingsErrors.Count > 0)
            {
                result.Errors.AddRange(settingsErrors);
                return result;
            }

            try
            {
                string codeDir = ResolvePath(settings.CodeOutputDir, projectRoot);
                if (!Directory.Exists(codeDir))
                {
                    result.Errors.Add("代码输出目录不存在：" + codeDir);
                    return result;
                }

                var model = LubanCodeParser.Parse(codeDir, settings.TablesClassName);
                result.Model = model;

                var errors = LubanProjectValidator.ValidateForIntegration(settings, model, projectRoot);
                if (errors.Count > 0)
                {
                    result.Errors.AddRange(errors);
                    return result;
                }

                if (!settings.GenerateRowInterfaces)
                {
                    result.Warnings.Add(
                        "GenerateRowInterfaces=false：请确认与生成代码同程序集的手写行接口实现已存在。");
                }

                var files = LubanIntegrationEmitter.Emit(model, settings);
                var keys = new List<string>(files.Keys);
                keys.Sort(StringComparer.Ordinal);
                result.Files.AddRange(keys);
                result.AdapterPath = "Adapter/" + settings.AdapterClassName + ".g.cs";

                if (write)
                {
                    LubanIntegrationWriter.Write(
                        ResolvePath(settings.IntegrationOutputDir, projectRoot),
                        files,
                        BuildTableLines(model));
                }

                result.Succeeded = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                return result;
            }
        }

        internal static string BuildSummary(LubanIntegrationResult result, bool write)
        {
            if (!result.Succeeded)
            {
                return "失败：" + string.Join("\n", result.Errors);
            }

            var counts = new Dictionary<LubanTableShape, int>();
            foreach (var table in result.Model.Tables)
            {
                counts.TryGetValue(table.Shape, out int count);
                counts[table.Shape] = count + 1;
            }

            string verb = write ? "已生成" : "校验通过（未写盘）";
            string summary = verb + "：" +
                "Map " + Get(counts, LubanTableShape.Map) +
                "，单行 " + Get(counts, LubanTableShape.SingleRow) +
                "，带索引 list " + Get(counts, LubanTableShape.IndexedList) +
                "，纯列表（跳过）" + Get(counts, LubanTableShape.PureList) + "。";

            if (write)
            {
                summary += "\n文件：" + result.Files.Count + " 个，适配器：" + result.AdapterPath;
            }

            if (result.Warnings.Count > 0)
            {
                summary += "\n警告：" + string.Join("\n", result.Warnings);
            }

            return summary;
        }

        private static int Get(Dictionary<LubanTableShape, int> counts, LubanTableShape shape)
        {
            return counts.TryGetValue(shape, out int count) ? count : 0;
        }

        private static List<string> BuildTableLines(LubanGeneratedModel model)
        {
            var lines = new List<string>(model.Tables.Count);
            foreach (var table in model.Tables)
            {
                lines.Add(
                    table.OutputDataName + "|" +
                    table.Shape + "|" +
                    table.RowTypeFullName + "|" +
                    (table.KeyTypeText ?? string.Empty));
            }

            return lines;
        }

        private static string ResolvePath(string path, string projectRoot)
        {
            return Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectRoot, path));
        }
    }
}
