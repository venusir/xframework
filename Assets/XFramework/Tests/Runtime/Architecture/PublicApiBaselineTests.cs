using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 公开 API 基线守卫：Runtime 程序集的公开面**不许悄悄变**——变了而基线未变即红。
    /// <para><b>它不判断变更该不该做</b>（那是人的事，且按 <c>CLAUDE.md</c> 的「提交拆分与授权」，
    /// 公开 API 变更与破坏性变更**须单独请用户确认**）；它只负责让「变了」这件事不可能滑过去，
    /// 并把「该做什么」写进失败消息——失败消息是唯一一个一定会被人读到的位置。</para>
    /// <para><b>为什么放 Runtime 侧</b>：<c>Workflow.md</c> §二 明写带 <c>-Filter</c> 且未指定平台时
    /// 只跑 PlayMode；放 <c>Tests/Editor/</c> 会被最常见的开发回路静默跳过（与 <c>AutoInitTests</c> /
    /// <c>NamingConventionTests</c> 同一条教训）。<b>Editor 程序集</b>的守卫是同目录的
    /// <see cref="EditorPublicApiBaselineTests"/>：它在 PlayMode 运行时按名取编辑器程序集
    /// （Runtime 测试程序集在编译期看不到它，但运行时能取到），因此同样不会被 <c>-Filter</c> 回路跳过。</para>
    /// <para><b>为什么基线可以被反写、以及反写的入口只有一个</b>：这是本仓首次出现「测试在显式开关下
    /// 写仓库文件」。开关是环境变量 <c>XFW_UPDATE_API_BASELINE</c>（只认 <c>"1"</c>），只由
    /// <c>pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline</c> 设置；更新模式**写完后一律判失败**并打印
    /// 相对旧基线的差异——绿灯只能由随后一次不带开关的重跑给出。理由：若更新模式写完就绿，唯一的依靠是
    /// 「人记得 <c>git diff</c>」，而这道守卫存在的理由恰恰是「人会忘」。</para>
    /// <para><b>为什么基线缺失即失败</b>：能静默重建的守卫等于没有守卫——首次生成那一次「人来审」不可省。</para>
    /// <para><b>没有 SetUp/TearDown 是有意的</b>（照 <c>LogFacadeCompletenessTests</c> 先例）：纯反射，
    /// 不 Configure、不 Shutdown、不写任何静态字段，故没有需要复位的东西。</para>
    /// </summary>
    [TestFixture]
    public class PublicApiBaselineTests
    {
        #region Constants

        /// <summary>射程内的程序集——断言一次，防将来有人顺手换成 Editor 程序集。</summary>
        private const string ExpectedAssemblyName = "Venusy609.Xframework";

        /// <summary>
        /// 基线文件。用 <see cref="SourceScanner.PackageRoot"/>——它是「测试壳的 Assets 是 junction、
        /// 故 <c>Application.dataPath/XFramework</c> 即真包根」这条推论的唯一定义处。
        /// </summary>
        private static string BaselinePath => Path.Combine(
            SourceScanner.PackageRoot, "Tests", "Runtime", "Architecture", "PublicApiBaseline.txt");

        /// <summary>完整差异的落盘位置（测试运行目录，已被 .gitignore 忽略）。</summary>
        private static string DiffPath => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "TestResults", "PublicApiBaseline.diff"));

        #endregion

        #region Test

        [Test]
        public void PublicApi_MatchesBaseline()
        {
            Assembly assembly = typeof(XFramework.XLog.LogManager).Assembly;
            Assert.AreEqual(ExpectedAssemblyName, assembly.GetName().Name,
                "本 fixture 只守 Runtime 程序集——Editor 程序集由 EditorPublicApiBaselineTests 守");

            PublicApiBaselineCheck.Verify(
                assembly, BaselinePath, DiffPath, PublicApiSnapshot.Header, "Runtime");
        }

        #endregion
    }
}
