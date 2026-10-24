using System;
using System.IO;
using System.Text;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// 一次 Luban 子进程调用的描述。
    /// </summary>
    internal readonly struct LubanCommand
    {
        internal string FileName { get; }

        internal string Arguments { get; }

        internal string WorkingDirectory { get; }

        internal LubanCommand(string fileName, string arguments, string workingDirectory)
        {
            FileName = fileName;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
        }

        public override string ToString()
        {
            return FileName + " " + Arguments;
        }
    }

    /// <summary>
    /// 把设置拼成 <c>dotnet &lt;Luban.dll&gt; ...</c> 命令行。纯函数，不访问磁盘。
    /// </summary>
    internal static class LubanCommandBuilder
    {
        internal static bool TryBuild(
            LubanGeneratorSettings settings,
            string projectRoot,
            out LubanCommand command,
            out string error)
        {
            command = default;
            error = null;

            if (settings == null)
            {
                error = "设置对象为 null。";
                return false;
            }

            if (!LubanSettingsValidator.IsPaired(settings.CodeTarget, settings.DataTarget))
            {
                error = "CodeTarget 与 DataTarget 不配套：cs-bin ↔ bin，cs-simple-json ↔ json。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                error = "项目根目录为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.LubanDllPath) ||
                string.IsNullOrWhiteSpace(settings.ConfPath) ||
                string.IsNullOrWhiteSpace(settings.CodeOutputDir) ||
                string.IsNullOrWhiteSpace(settings.DataOutputDir))
            {
                error = "Luban.dll / luban.conf / 输出目录不能为空。";
                return false;
            }

            bool windows = Path.DirectorySeparatorChar == '\\';
            string dotnet;
            string lubanDll;
            string conf;
            string codeDir;
            string dataDir;
            string workingDirectory;
            try
            {
                dotnet = ResolveTool(settings.DotnetPath, projectRoot);
                lubanDll = ResolvePath(settings.LubanDllPath, projectRoot);
                conf = ResolvePath(settings.ConfPath, projectRoot);
                codeDir = ResolvePath(settings.CodeOutputDir, projectRoot);
                dataDir = ResolvePath(settings.DataOutputDir, projectRoot);

                workingDirectory = Path.GetDirectoryName(conf);
                if (string.IsNullOrEmpty(workingDirectory))
                {
                    workingDirectory = projectRoot;
                }
            }
            catch (Exception ex)
            {
                error = "路径解析失败：" + ex.Message;
                return false;
            }

            var arguments = new StringBuilder();
            arguments.Append(Quote(lubanDll, windows));
            AppendOption(arguments, "-t", settings.TargetName, windows);
            arguments.Append(" -c ");
            arguments.Append(settings.CodeTarget == LubanCodeTarget.CsBin
                ? "cs-bin"
                : "cs-simple-json");
            arguments.Append(" -d ");
            arguments.Append(settings.DataTarget == LubanDataTarget.Bin ? "bin" : "json");
            AppendOption(arguments, "--conf", conf, windows);
            AppendOption(arguments, "-x", "outputCodeDir=" + codeDir, windows);
            AppendOption(arguments, "-x", "outputDataDir=" + dataDir, windows);

            if (!string.IsNullOrWhiteSpace(settings.ExtraArguments))
            {
                arguments.Append(' ');
                arguments.Append(settings.ExtraArguments.Trim());
            }

            command = new LubanCommand(dotnet, arguments.ToString(), workingDirectory);
            return true;
        }

        /// <summary>
        /// 平台相关的参数引号。Windows 按 CommandLineToArgvW 规则，Unix 用单引号。
        /// </summary>
        internal static string Quote(string value, bool windows)
        {
            if (value == null)
            {
                return "\"\"";
            }

            if (value.Length == 0)
            {
                return "\"\"";
            }

            if (windows)
            {
                return QuoteWindows(value);
            }

            return QuoteUnix(value);
        }

        private static string QuoteWindows(string value)
        {
            bool needsQuotes = false;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == ' ' || value[i] == '\t' || value[i] == '"')
                {
                    needsQuotes = true;
                    break;
                }
            }

            if (!needsQuotes)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');

            int index = 0;
            while (index < value.Length)
            {
                int backslashes = 0;
                while (index < value.Length && value[index] == '\\')
                {
                    backslashes++;
                    index++;
                }

                if (index == value.Length)
                {
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (value[index] == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(value[index]);
                }

                index++;
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static string QuoteUnix(string value)
        {
            if (value.IndexOfAny(new[] { ' ', '\t', '\'', '"', '$', '`', '\\', '!', '&', '|', ';', '<', '>', '(', ')', '*', '?', '[', ']', '#', '~' }) < 0)
            {
                return value;
            }

            return "'" + value.Replace("'", "'\\''") + "'";
        }

        private static void AppendOption(
            StringBuilder builder,
            string name,
            string value,
            bool windows)
        {
            builder.Append(' ');
            builder.Append(name);
            builder.Append(' ');
            builder.Append(Quote(value, windows));
        }

        private static string ResolvePath(string path, string projectRoot)
        {
            return Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectRoot, path));
        }

        private static string ResolveTool(string toolPath, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(toolPath))
            {
                return "dotnet";
            }

            if (Path.IsPathRooted(toolPath))
            {
                return toolPath;
            }

            return toolPath.IndexOf('/') >= 0 || toolPath.IndexOf('\\') >= 0
                ? Path.GetFullPath(Path.Combine(projectRoot, toolPath))
                : toolPath;
        }
    }
}
