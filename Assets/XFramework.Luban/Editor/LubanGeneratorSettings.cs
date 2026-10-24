using UnityEditor;
using UnityEngine;

namespace XFramework.XConfig.Luban.Editor
{
    /// <summary>
    /// Luban 导表与胶水生成的项目级设置。存放在 <c>ProjectSettings/</c>，
    /// 不写入 UPM 包目录（包在消费方是只读的）。
    /// </summary>
    public sealed class LubanGeneratorSettings : ScriptableSingleton<LubanGeneratorSettings>
    {
        /// <summary>dotnet 可执行文件；默认从 PATH 查找。</summary>
        public string DotnetPath = "dotnet";

        /// <summary>Luban.dll 路径（项目相对或绝对）。</summary>
        public string LubanDllPath = "";

        /// <summary>luban.conf 路径（项目相对或绝对）。</summary>
        public string ConfPath = "";

        /// <summary>Luban target 名（如 <c>client</c> / <c>all</c>）。</summary>
        public string TargetName = "client";

        /// <summary>Luban 生成的整包入口类名（默认 <c>Tables</c>）。</summary>
        public string TablesClassName = "Tables";

        /// <summary>Luban 生成的 C# 代码目录（必须在 Assets/ 下）。</summary>
        public string CodeOutputDir = "";

        /// <summary>Luban 生成的数据目录（必须在 Assets/ 下）。</summary>
        public string DataOutputDir = "";

        /// <summary>接缝与适配器目录（必须在 Assets/ 下，且与本设置不重叠）。</summary>
        public string GlueOutputDir = "";

        /// <summary>代码目标；必须与 <see cref="DataTarget"/> 配套。</summary>
        public LubanCodeTarget CodeTarget = LubanCodeTarget.CsBin;

        /// <summary>数据目标；必须与 <see cref="CodeTarget"/> 配套。</summary>
        public LubanDataTarget DataTarget = LubanDataTarget.Bin;

        /// <summary>生成的适配器命名空间。</summary>
        public string AdapterNamespace = "XFramework.XConfig.Luban.Generated";

        /// <summary>生成的适配器类名。</summary>
        public string AdapterClassName = "GeneratedLubanConfigAdapter";

        /// <summary>附加到 Luban 命令行的原始参数（由使用方自行保证引号正确）。</summary>
        public string ExtraArguments = "";

        /// <summary>是否生成 <c>IConfigRow&lt;TKey&gt;</c> 接缝；关闭时由使用方手写。</summary>
        public bool GenerateSeams = true;

        /// <summary>
        /// 保存到 <c>ProjectSettings/</c>。<c>ScriptableSingleton.Save</c> 是 protected，故在此包装。
        /// </summary>
        internal void SaveSettings()
        {
            Save(true);
        }
    }
}
