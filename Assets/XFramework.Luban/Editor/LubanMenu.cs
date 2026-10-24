using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// Luban 导表与胶水生成的菜单入口。
    /// </summary>
    internal static class LubanMenu
    {
        internal const string MenuRoot = "XFramework/Luban/";
        internal const string GenerateMenu = MenuRoot + "生成配置表";
        internal const string RefreshGlueMenu = MenuRoot + "仅刷新胶水";
        internal const string ValidateMenu = MenuRoot + "校验接入";
        internal const string SettingsMenu = MenuRoot + "设置...";

        [MenuItem(GenerateMenu, priority = 100)]
        private static void Generate()
        {
            var settings = LubanGeneratorSettings.instance;
            var errors = ValidateForRun(settings, out string projectRoot);
            if (errors.Count > 0)
            {
                ShowErrors("设置不完整", errors);
                return;
            }

            if (!LubanCommandBuilder.TryBuild(
                    settings, projectRoot, out var command, out string error))
            {
                ShowErrors("无法构建命令", new[] { error });
                return;
            }

            Debug.Log("[Luban] " + command);

            LubanProcessResult result;
            EditorUtility.DisplayProgressBar("XFramework Luban", "Running Luban...", 0.5f);
            try
            {
                result = LubanProcessRunner.Run(command);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!result.Succeeded)
            {
                ShowProcessFailure(result);
                return;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log("[Luban] Generation finished." + Environment.NewLine + result.StandardOutput);
            RunGlue(settings, projectRoot, write: true);
        }

        [MenuItem(RefreshGlueMenu, priority = 101)]
        private static void RefreshGlue()
        {
            var settings = LubanGeneratorSettings.instance;
            var errors = LubanSettingsValidator.Validate(settings);
            if (errors.Count > 0)
            {
                ShowErrors("设置不完整", errors);
                return;
            }

            RunGlue(settings, Directory.GetCurrentDirectory(), write: true);
        }

        [MenuItem(ValidateMenu, priority = 102)]
        private static void Validate()
        {
            var settings = LubanGeneratorSettings.instance;
            var errors = LubanSettingsValidator.Validate(settings);
            if (errors.Count > 0)
            {
                ShowErrors("设置不完整", errors);
                return;
            }

            RunGlue(settings, Directory.GetCurrentDirectory(), write: false);
        }

        [MenuItem(SettingsMenu, priority = 120)]
        private static void OpenSettings()
        {
            SettingsService.OpenProjectSettings("Project/XFramework/Luban");
        }

        internal static List<string> ValidateForRun(
            LubanGeneratorSettings settings,
            out string projectRoot)
        {
            projectRoot = Directory.GetCurrentDirectory();
            var errors = LubanSettingsValidator.Validate(settings);
            if (errors.Count > 0)
            {
                return errors;
            }

            string lubanDll = ResolveProjectPath(settings.LubanDllPath, projectRoot);
            if (!File.Exists(lubanDll))
            {
                errors.Add("Luban.dll 不存在：" + lubanDll);
            }

            string conf = ResolveProjectPath(settings.ConfPath, projectRoot);
            if (!File.Exists(conf))
            {
                errors.Add("luban.conf 不存在：" + conf);
            }

            return errors;
        }

        internal static string ResolveProjectPath(string path, string projectRoot)
        {
            return Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectRoot, path));
        }

        internal static void ShowErrors(string title, IEnumerable<string> errors)
        {
            string message = string.Join("\n", errors);
            Debug.LogError("[Luban] " + title + Environment.NewLine + message);
            EditorUtility.DisplayDialog("XFramework Luban", title + "\n\n" + message, "OK");
        }

        internal static void RunGlue(
            LubanGeneratorSettings settings,
            string projectRoot,
            bool write)
        {
            var result = LubanGlueGenerator.Generate(settings, projectRoot, write);
            if (!result.Succeeded)
            {
                ShowErrors(write ? "胶水生成失败" : "接入校验失败", result.Errors);
                return;
            }

            if (write)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }

            string summary = LubanGlueGenerator.BuildSummary(result, write);
            Debug.Log("[Luban] " + summary.Replace("\n", Environment.NewLine));
            EditorUtility.DisplayDialog("XFramework Luban", summary, "OK");
        }

        private static void ShowProcessFailure(LubanProcessResult result)
        {
            string reason = result.TimedOut
                ? "Luban 运行超时（5 分钟）并被终止。"
                : "Luban 退出码：" + result.ExitCode;

            string output = Tail(result.StandardError, 4000);
            string standardOutput = Tail(result.StandardOutput, 2000);
            string message = reason;
            if (!string.IsNullOrEmpty(output))
            {
                message += "\n\nstderr:\n" + output;
            }

            if (!string.IsNullOrEmpty(standardOutput))
            {
                message += "\n\nstdout:\n" + standardOutput;
            }

            Debug.LogError("[Luban] " + message);
            EditorUtility.DisplayDialog("XFramework Luban", message, "OK");
        }

        private static string Tail(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return "..." + value.Substring(value.Length - maxLength);
        }
    }
}
