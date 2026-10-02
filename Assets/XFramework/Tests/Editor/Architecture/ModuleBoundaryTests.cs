using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 模块边界自查:生产代码不得引用**其它模块**的 <c>Internal</c> 命名空间。
    /// <para>
    /// 为什么需要它:Runtime 全树共用一个 asmdef,<c>internal</c> <b>不构成编译边界</b>——跨模块引用实现细节
    /// 既没有编译器约束,也没有成文约定。2026-09-27 之前,Reactive ×2 / Input / Settings ×2 共 5 个文件
    /// 直接引用了 <c>XMessage.Internal</c>,这件事是「三代理 + 人工核对」查出来的;本 fixture 把同一次审计
    /// 变成每次全量门禁都会跑的自查。
    /// </para>
    /// <para>
    /// 三条断言分工:<see cref="ProductionCode_DoesNotReferenceOtherModulesInternalNamespaces"/> 扫源码文本
    /// (含全限定名,不只看 <c>using</c>);<see cref="PublicApi_DoesNotExposeInternalTypes"/> 扫反射后的公开签名
    /// ——后者是本模块边界的**实质**:实现可以藏在 internal 里,但公开面不能漏出它;
    /// <see cref="UnityInputTypes_StayInsideTheDefaultNamespace"/> 守的是另一类边界——**可选包的 Unity 类型**
    /// 只能待在各自的 <c>Default</c> 命名空间里(插件中立承诺)。
    /// </para>
    /// <para>
    /// 测试树整体不受此限:<c>InternalsVisibleTo</c> 是设计的一部分(见 Runtime/AssemblyInfo.cs)。
    /// </para>
    /// </summary>
    [TestFixture]
    public class ModuleBoundaryTests
    {
        #region 源码扫描

        /// <summary>`using X…Internal;` 指令。</summary>
        private static readonly Regex UsingDirective =
            new Regex(@"^\s*using\s+(XFramework\.[A-Za-z0-9_.]*\.Internal)\s*;", RegexOptions.Multiline);

        /// <summary>全限定引用(如 <c>XMessage.Internal.EventStream&lt;T&gt;</c>)——只扫 <c>using</c> 会让它绕过。</summary>
        private static readonly Regex QualifiedReference =
            new Regex(@"XFramework\.[A-Za-z0-9_.]*\.Internal(?=[\s.;,)<])", RegexOptions.Multiline);

        [Test]
        public void ProductionCode_DoesNotReferenceOtherModulesInternalNamespaces()
        {
            var packageRoot = Path.Combine(Application.dataPath, "XFramework");
            var scanRoots = new List<string> { Path.Combine(packageRoot, "Editor") };
            var runtimeRoot = Path.Combine(packageRoot, "Runtime");
            Assert.IsTrue(Directory.Exists(runtimeRoot), $"找不到 Runtime 目录:{runtimeRoot}(测试壳的 Assets 是否为 junction?)");

            // 模块根 = Runtime 的直接子目录(Message / Event / Reactive / …);直接躺在 Runtime 下的文件归 Runtime 这一层
            scanRoots.Add(runtimeRoot);
            foreach (var dir in Directory.GetDirectories(runtimeRoot))
                scanRoots.Add(dir);

            var violations = new List<string>();
            foreach (var root in scanRoots)
            {
                if (!Directory.Exists(root))
                    continue;

                var owned = CollectOwnedInternalNamespaces(root);
                foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    foreach (var referenced in CollectReferencedInternalNamespaces(file))
                    {
                        if (!owned.Contains(referenced))
                            violations.Add($"{Relative(packageRoot, file)} 引用了 {referenced}(该命名空间不属于本模块)");
                    }
                }
            }

            Assert.IsEmpty(violations,
                "生产代码不得引用其它模块的 Internal 命名空间——跨模块共享的基础设施应抽为独立模块并给公开面" +
                "(先例:XEvent ← Message/Reactive/Input/Settings;XPipeline ← Bootstrap)。违规:\n  " +
                string.Join("\n  ", violations));
        }

        /// <summary>本模块(root 及其子目录)自己声明的 Internal 命名空间集合。</summary>
        private static HashSet<string> CollectOwnedInternalNamespaces(string root)
        {
            var owned = new HashSet<string>();
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match m in Regex.Matches(ReadCodeOnly(file), @"namespace\s+(XFramework\.[A-Za-z0-9_.]*\.Internal)"))
                    owned.Add(m.Groups[1].Value);
            }
            return owned;
        }

        /// <summary>文件里引用到的 Internal 命名空间(using 与全限定两种形态,去重)。</summary>
        private static HashSet<string> CollectReferencedInternalNamespaces(string file)
        {
            var code = ReadCodeOnly(file);
            var referenced = new HashSet<string>();

            foreach (Match m in UsingDirective.Matches(code))
                referenced.Add(m.Groups[1].Value);

            foreach (Match m in QualifiedReference.Matches(code))
                referenced.Add(m.Value);

            return referenced;
        }

        /// <summary>
        /// 读出「只有代码」的文本:注释行按原行数置空,故行号仍与源文件一致。
        /// <para>
        /// 必须做这一步——文档注释里出现 <c>XFramework.XMessage.Internal</c> 这类**说明性文字**不该被判为引用
        /// (例如 XEvent 的两份池注释就明写了「与 Message 的同名池是两份、不合并」)。
        /// </para>
        /// </summary>
        private static string ReadCodeOnly(string file)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    lines[i] = string.Empty;
                }
            }
            return string.Join("\n", lines);
        }

        private static string Relative(string root, string path)
            => path.Substring(root.Length + 1).Replace('\\', '/');

        #endregion

        #region 可选包类型边界（Unity 输入）

        /// <summary>`using UnityEngine.InputSystem*;` 指令（含子命名空间）。</summary>
        private static readonly Regex UnityInputUsing =
            new Regex(@"^\s*using\s+UnityEngine\.InputSystem[A-Za-z0-9_.]*\s*;", RegexOptions.Multiline);

        /// <summary>全限定引用（如 <c>UnityEngine.InputSystem.InputActionAsset</c>）——只扫 <c>using</c> 会让它绕过。</summary>
        private static readonly Regex UnityInputQualified =
            new Regex(@"UnityEngine\.InputSystem\.", RegexOptions.Multiline);

        /// <summary>Unity 输入类型唯一被允许出现的位置（相对 <c>Runtime/</c>，正斜杠）。</summary>
        private const string InputDefaultDir = "Input/Default";

        /// <summary>
        /// Unity 输入类型只允许出现在 <c>Runtime/Input/Default/</c> 内——门面 <c>InputManager</c> 与
        /// <c>IInputProvider</c> 保持插件中立，替换后端（Rewired 等）的项目在公开面不该看到 Unity 的类型。
        /// <para>
        /// <b>为什么需要它</b>：这条边界以前只活在 CHANGELOG 的一句承诺里，而
        /// <see cref="ProductionCode_DoesNotReferenceOtherModulesInternalNamespaces"/> 查的是跨模块 <c>Internal</c>、
        /// <see cref="PublicApi_DoesNotExposeInternalTypes"/> 查的是漏出的 internal 类型——**可选包的 Unity 类型
        /// 外泄不在任何一条的射程内**。于是 2026-10-02 给 <c>GameLauncher</c> 加字段面时，
        /// 一个 <c>[SerializeField] InputActionAsset</c> 字段活过了编译、测试与文档三道门禁。
        /// </para>
        /// </summary>
        [Test]
        public void UnityInputTypes_StayInsideTheDefaultNamespace()
        {
            var packageRoot = Path.Combine(Application.dataPath, "XFramework");
            var runtimeRoot = Path.Combine(packageRoot, "Runtime");
            Assert.IsTrue(Directory.Exists(runtimeRoot), $"找不到 Runtime 目录:{runtimeRoot}(测试壳的 Assets 是否为 junction?)");

            var violations = new List<string>();
            foreach (var file in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Relative(runtimeRoot, file);
                if (relative.StartsWith(InputDefaultDir, StringComparison.Ordinal))
                    continue;

                var code = ReadCodeOnly(file);
                if (UnityInputUsing.IsMatch(code) || UnityInputQualified.IsMatch(code))
                    violations.Add(relative);
            }

            Assert.IsEmpty(violations,
                "Unity 输入类型（UnityEngine.InputSystem.*）只允许出现在 Runtime/Input/Default/ 内——门面与 " +
                "IInputProvider 保持插件中立，整体替换后端（Rewired 等）的项目不该在公开面看到 Unity 的类型。" +
                "要引用输入配置请用该命名空间下的框架类型（InputSystemOptions / InputSystemOptionsAsset）。违规:\n  " +
                string.Join("\n  ", violations));
        }

        #endregion

        #region 公开面扫描

        [Test]
        public void PublicApi_DoesNotExposeInternalTypes()
        {
            var assembly = typeof(XMessage.MessageManager).Assembly;
            var violations = new List<string>();

            foreach (var type in assembly.GetExportedTypes())
            {
                // ① 公开类型不得声明在 Internal 命名空间里。
                // 编译器拦不住这一条:`internal` 是**类型级**修饰符,命名空间里写 public 完全合法,
                // 而它一旦 public 就能被公开签名合法引用(不触发 CS0050),于是从未经编译器检查的缝里漏出去。
                if (IsInternalType(type))
                    violations.Add($"{type.FullName} 是 Internal 命名空间里的 public 类型");

                // ② 公开签名里不得出现 Internal 命名空间的类型(含泛型实参)。
                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance |
                                                       BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    foreach (var exposed in ExposedTypes(member))
                    {
                        if (IsInternalType(exposed))
                            violations.Add($"{type.FullName}.{member.Name} 暴露了 {exposed}");   // 泛型参数的 FullName 为 null,故用 ToString
                    }
                }
            }

            Assert.IsEmpty(violations,
                "公开面不得出现 Internal 命名空间里的类型——实现可以藏在 internal 里,公开签名不能漏出它。违规:\n  " +
                string.Join("\n  ", violations));
        }

        /// <summary>成员签名里出现的类型(字段 / 属性 / 方法参数与返回值 / 事件,含泛型实参)。不检查实现的接口——那是刻意的设计自由度。</summary>
        private static IEnumerable<Type> ExposedTypes(MemberInfo member)
        {
            switch (member)
            {
                case FieldInfo field:
                    foreach (var t in Expand(field.FieldType)) yield return t;
                    break;
                case PropertyInfo property:
                    foreach (var t in Expand(property.PropertyType)) yield return t;
                    break;
                case EventInfo evt:
                    foreach (var t in Expand(evt.EventHandlerType)) yield return t;
                    break;
                case MethodBase method:
                    foreach (var t in Expand(method is MethodInfo mi ? mi.ReturnType : typeof(void))) yield return t;
                    foreach (var parameter in method.GetParameters())
                        foreach (var t in Expand(parameter.ParameterType)) yield return t;
                    break;
            }
        }

        /// <summary>展开泛型实参(如 <c>List&lt;SomeInternal&gt;</c>)。</summary>
        private static IEnumerable<Type> Expand(Type type)
        {
            if (type == null)
                yield break;

            yield return type;

            if (!type.IsGenericType)
                yield break;

            foreach (var argument in type.GetGenericArguments())
                foreach (var nested in Expand(argument))
                    yield return nested;
        }

        private static bool IsInternalType(Type type)
            => type != null && type.Namespace != null && type.Namespace.EndsWith(".Internal", StringComparison.Ordinal);

        #endregion
    }
}
