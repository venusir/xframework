using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// Luban 导表与接入代码生成的菜单入口。
    /// </summary>
    internal static class LubanMenu
    {
        internal const string MenuRoot = "XFramework/Luban/";
        internal const string GenerateMenu = MenuRoot + "生成配置表";
        internal const string RefreshIntegrationMenu = MenuRoot + "仅重新生成接入代码";
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
            RunIntegration(settings, projectRoot, write: true);
        }

        [MenuItem(RefreshIntegrationMenu, priority = 101)]
        private static void RefreshIntegration()
        {
            var settings = LubanGeneratorSettings.instance;
            var errors = LubanSettingsValidator.Validate(settings);
            if (errors.Count > 0)
            {
                ShowErrors("设置不完整", errors);
                return;
            }

            RunIntegration(settings, GetProjectRoot(), write: true);
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

            RunIntegration(settings, GetProjectRoot(), write: false);
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
            projectRoot = GetProjectRoot();
            var errors = LubanSettingsValidator.Validate(settings, projectRoot);
            if (errors.Count > 0)
            {
                return errors;
            }

            string lubanDll;
            string conf;
            try
            {
                lubanDll = ResolveProjectPath(settings.LubanDllPath, projectRoot);
                conf = ResolveProjectPath(settings.ConfPath, projectRoot);
            }
            catch (Exception ex)
            {
                errors.Add("路径解析失败：" + ex.Message);
                return errors;
            }

            if (!File.Exists(lubanDll))
            {
                errors.Add("Luban.dll 不存在：" + lubanDll);
            }

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

        internal static string GetProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }

        internal static void ShowDialog(string title, string message)
        {
            if (Application.isBatchMode)
            {
                Debug.LogWarning("[Luban] " + title + Environment.NewLine + message);
                return;
            }

            EditorUtility.DisplayDialog("XFramework Luban", title + "\n\n" + message, "OK");
        }

        internal static string DescribeTimeout(int milliseconds)
        {
            int seconds = Math.Max(1, milliseconds / 1000);
            return "Luban 运行超时（" + seconds + " 秒）并被终止。";
        }

        internal static void ShowErrors(string title, IEnumerable<string> errors)
        {
            string message = string.Join("\n", errors);
            Debug.LogError("[Luban] " + title + Environment.NewLine + message);
            ShowDialog(title, message);
        }

        internal static void RunIntegration(
            LubanGeneratorSettings settings,
            string projectRoot,
            bool write)
        {
            var result = LubanIntegrationGenerator.Generate(settings, projectRoot, write);
            if (!result.Succeeded)
            {
                ShowErrors(write ? "接入代码生成失败" : "接入校验失败", result.Errors);
                return;
            }

            if (write)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }

            string summary = LubanIntegrationGenerator.BuildSummary(result, write);
            Debug.Log("[Luban] " + summary.Replace("\n", Environment.NewLine));
            ShowDialog("XFramework Luban", summary);
        }

        private static void ShowProcessFailure(LubanProcessResult result)
        {
            string reason = result.TimedOut
                ? DescribeTimeout(LubanProcessRunner.DefaultTimeoutMilliseconds)
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
            ShowDialog("XFramework Luban", message);
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
