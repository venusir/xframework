using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 公开 API 快照渲染器：把一个程序集的公开面渲染成**扁平行集合**（Ordinal 排序 + 去重）。
    /// <para><b>为什么是这个形状</b>：比较是**行集合差**，所以排序、去重、diff 三件事都不需要解析器——
    /// 更新模式要算差异时只做 <c>HashSet</c> 减法。每行自包含（`全限定成员名  ::  签名`），
    /// 而 `::` 从不出现于 C# 签名里，人扫一眼就知道哪半边变了。</para>
    /// <para><b>收录什么</b>：<c>GetExportedTypes()</c> 的每个类型（类型行：kind + 修饰符 + 基类型 +
    /// **直接声明**的接口 + 泛型参数与约束），以及其上 <c>DeclaredOnly</c> 的 public 成员；
    /// 外加**非 sealed 类上的 <c>protected</c> / <c>protected internal</c>**——本仓的框架底座
    /// （<c>GameLauncher</c> 的 <c>ConfigureStages</c>、<c>UIPanelBase</c> 的 14 个生命周期覆写点）
    /// 就是靠继承给第三方用的，漏掉它们等于放过「底座被改坏」这条最贵的破坏性变更。</para>
    /// <para><b>签名里必须有参数名与返回类型</b>：0.3.0 那批真实变更里，最轻的两类是「仅形参名变更」
    /// （<c>Subscribe(Action&lt;T&gt; onNext)</c> → <c>handler</c>）与「返回类型变更」（<c>GetEnumerator()</c>
    /// 改结构体枚举器）——两者编译器都不吭声，只看「名字 + 参数个数」的指纹抓不到。</para>
    /// <para><b>确定性</b>：排序用 <see cref="StringComparer.Ordinal"/>（culture 排序会随机器区域设置漂移）；
    /// 行序无关的接口列表与泛型约束列表在渲染前排序；不写时间戳、版本号、路径。</para>
    /// <para><b>射程</b>（照 <c>ModuleBoundaryTests</c> 交代射程的口径）：① <c>protected</c> 嵌套类型不在射程
    /// （<c>GetExportedTypes()</c> 不返回它们，本仓当前 0 个）；② <c>[Obsolete]</c> 不单列维度（本仓当前
    /// 0 处，将来引入弃用过渡期再说）；③ 只反映**编辑器编译产物**的公开面（<c>#if UNITY_EDITOR</c> 门控的
    /// 成员会进快照而播放器里没有——本仓当前 0 个，2026-10-13 核过）；④ 行集合去重：渲染完全相同的两行会
    /// 折叠，真出现即意味着渲染器丢了区分维度，是渲染器的 bug 而非数据问题。</para>
    /// </summary>
    internal static class PublicApiSnapshot
    {
        #region Constants

        /// <summary>名字与签名之间的分隔符——`::` 不出现于 C# 签名，前后各留两空格让两半视觉分离。</summary>
        private const string Sep = "  ::  ";

        /// <summary>基线文件头部。<b>必须写死</b>：它是文件的一部分，含任何环境相关量都会每次再生都产生 diff。</summary>
        internal const string Header =
            "# XFramework Runtime 程序集公开 API 基线（自动生成，勿手工编辑）\n" +
            "#\n" +
            "# 生成：Tests/Runtime/Architecture/PublicApiSnapshot.cs\n" +
            "# 更新：pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline\n" +
            "# 比较：按 \\n 拆行、去行尾 \\r，忽略 '#' 开头的行——行尾漂移不算变化；其余任何一行的增删都是公开面变化。\n" +
            "#\n" +
            "# 本文件任何非注释行变化都意味着公开 API 变化，按顺序处置：\n" +
            "#   1) 公开 API 变更 / 破坏性变更：按 CLAUDE.md「提交拆分与授权」单独请用户确认；\n" +
            "#   2) 破坏性变更：在 Assets/XFramework/CHANGELOG.md 的「破坏性变更迁移表」补一行（旧写法 → 新写法 → 说明）；\n" +
            "#   3) 更新基线（-UpdateApiBaseline），随后不带开关重跑一次取绿灯；本文件与其 .meta 必须一起提交。\n" +
            "#\n" +
            "# 射程：Runtime 程序集 Venusy609.Xframework（含非 sealed 类上的 protected 扩展点）。\n" +
            "# Editor 程序集 Venusy609.Xframework.Editor 不在射程内（Runtime 测试程序集看不到它，要查得另写 EditMode fixture）。\n";

        /// <summary>基础类型别名表——只为可读性；其余一律全限定名，不留 using 歧义。</summary>
        private static readonly Dictionary<Type, string> Aliases = new Dictionary<Type, string>
        {
            [typeof(void)] = "void", [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte",
            [typeof(char)] = "char", [typeof(decimal)] = "decimal", [typeof(double)] = "double", [typeof(float)] = "float",
            [typeof(int)] = "int", [typeof(uint)] = "uint", [typeof(long)] = "long", [typeof(ulong)] = "ulong",
            [typeof(short)] = "short", [typeof(ushort)] = "ushort", [typeof(string)] = "string", [typeof(object)] = "object",
        };

        #endregion

        #region Capture / Render

        /// <summary>抓取一个程序集的公开面（排序 + 去重后的行集合）。</summary>
        internal static SortedSet<string> Capture(Assembly assembly)
        {
            var lines = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var type in assembly.GetExportedTypes())
            {
                lines.Add(TypeLine(type));

                if (type.IsEnum)
                {
                    CaptureEnumMembers(lines, type);
                    continue;
                }

                // 委托的签名由类型行整体承载；逐个渲染成员会把 .ctor(object, IntPtr) 这种运行时噪音钉进基线
                if (IsDelegate(type))
                    continue;

                CaptureMembers(lines, type);
            }

            return lines;
        }

        /// <summary>渲染成完整的基线文本（头部 + 行，`\n` 行尾）。</summary>
        internal static string Render(Assembly assembly)
        {
            var sb = new StringBuilder(Header);
            foreach (var line in Capture(assembly))
                sb.Append(line).Append('\n');
            return sb.ToString();
        }

        /// <summary>把基线文本解析回行集合：拆行、去行尾 `\r`、忽略 `#` 开头的注释行。</summary>
        internal static SortedSet<string> Parse(string text)
        {
            var lines = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#')
                    continue;
                lines.Add(line);
            }

            return lines;
        }

        #endregion

        #region Types

        private static void CaptureEnumMembers(SortedSet<string> lines, Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (field.IsSpecialName) // value__
                    continue;

                lines.Add($"{TypeName(type)}.{field.Name}{Sep}= {Literal(field.GetRawConstantValue())}");
            }
        }

        private static string TypeLine(Type type)
        {
            var sb = new StringBuilder(TypeName(type)).Append(Sep).Append("public ");

            if (type.IsAbstract && type.IsSealed)
            {
                sb.Append("static ");
            }
            else
            {
                if (type.IsAbstract && !type.IsInterface)
                    sb.Append("abstract ");
                if (type.IsSealed && !type.IsValueType && !IsDelegate(type))
                    sb.Append("sealed ");
                if (type.IsValueType && !type.IsEnum && !type.IsPrimitive &&
                    type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false))
                    sb.Append("readonly ");
            }

            sb.Append(Kind(type)).Append(Tail(type)).Append(Constraints(type));
            return sb.ToString();
        }

        private static string Kind(Type type)
        {
            if (type.IsEnum) return "enum";
            if (IsDelegate(type)) return "delegate";
            if (type.IsInterface) return "interface";
            return type.IsValueType ? "struct" : "class";
        }

        private static bool IsDelegate(Type type) => typeof(Delegate).IsAssignableFrom(type);

        private static string Tail(Type type)
        {
            if (type.IsEnum)
                return " : " + TypeName(Enum.GetUnderlyingType(type));

            if (IsDelegate(type))
            {
                var invoke = type.GetMethod("Invoke");
                return " -> " + TypeName(invoke.ReturnType) + Parameters(invoke.GetParameters());
            }

            var parts = new List<string>();

            var baseType = type.BaseType;
            if (baseType != null && baseType != typeof(object) && baseType != typeof(ValueType))
                parts.Add(TypeName(baseType));

            var interfaces = new List<string>();
            foreach (var face in DirectInterfaces(type))
                interfaces.Add(TypeName(face));
            interfaces.Sort(StringComparer.Ordinal);
            parts.AddRange(interfaces);

            return parts.Count == 0 ? string.Empty : " : " + string.Join(", ", parts);
        }

        /// <summary>
        /// **直接声明**的接口：<c>GetInterfaces()</c> 给的是含继承而来的全集，这里把「基类型已经提供的」
        /// 与「接口自身继承来的」剔掉——否则基类的一次改动会让所有派生类型的行一起变，噪音压过信号。
        /// </summary>
        private static List<Type> DirectInterfaces(Type type)
        {
            var all = new List<Type>(type.GetInterfaces());

            if (type.BaseType != null)
            {
                foreach (var inherited in type.BaseType.GetInterfaces())
                    all.Remove(inherited);
            }

            foreach (var face in type.GetInterfaces())
            {
                foreach (var parent in face.GetInterfaces())
                    all.Remove(parent);
            }

            return all;
        }

        /// <summary>泛型约束，按固定次序渲染：<c>class</c> → 类型约束（排序）→ <c>new()</c>。</summary>
        private static string Constraints(Type type)
        {
            if (!type.IsGenericTypeDefinition)
                return string.Empty;

            var sb = new StringBuilder();

            foreach (var parameter in type.GetGenericArguments())
            {
                var parts = new List<string>();
                var attributes = parameter.GenericParameterAttributes;

                if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                    parts.Add("class");
                if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                    parts.Add("struct");

                var constraints = new List<string>();
                foreach (var constraint in parameter.GetGenericParameterConstraints())
                {
                    if (constraint == typeof(ValueType)) // struct 约束的产物，已由上面的 struct 表达
                        continue;
                    constraints.Add(TypeName(constraint));
                }
                constraints.Sort(StringComparer.Ordinal);
                parts.AddRange(constraints);

                if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                    (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                    parts.Add("new()");

                if (parts.Count > 0)
                    sb.Append(" where ").Append(parameter.Name).Append(" : ").Append(string.Join(", ", parts));
            }

            return sb.ToString();
        }

        #endregion

        #region Members

        private static void CaptureMembers(SortedSet<string> lines, Type type)
        {
            string owner = TypeName(type);
            bool extensible = CanBeInherited(type);

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!IsVisible(field.IsPublic, field.IsFamily || field.IsFamilyOrAssembly, extensible))
                    continue;
                if (field.IsSpecialName || IsCompilerGenerated(field))
                    continue;

                lines.Add(FieldLine(owner, field));
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var getter = property.GetMethod;
                var setter = property.SetMethod;

                bool visible = IsVisible(IsPublic(getter), IsProtected(getter), extensible) ||
                               IsVisible(IsPublic(setter), IsProtected(setter), extensible);

                if (!visible || IsCompilerGenerated(property))
                    continue;

                lines.Add(PropertyLine(owner, property, getter, setter));
            }

            foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var adder = evt.AddMethod;

                if (!IsVisible(IsPublic(adder), IsProtected(adder), extensible) || IsCompilerGenerated(evt))
                    continue;

                lines.Add(EventLine(owner, evt, adder));
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                // 访问器（get_/set_/add_/remove_）与编译器产物跳过；运算符（op_*）是公开面的一部分，
                // 必须纳入基线（此前与访问器一起被 IsSpecialName 一刀切掉，`TimerHandle` 的 ==/!= 等
                // 运算符因此不在射程内）。
                if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal))
                    continue;
                if (IsCompilerGenerated(method))
                    continue;
                if (!IsVisible(method.IsPublic, method.IsFamily || method.IsFamilyOrAssembly, extensible))
                    continue;

                lines.Add(MethodLine(owner, type, method));
            }

            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic |
                                                      BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!IsVisible(ctor.IsPublic, ctor.IsFamily || ctor.IsFamilyOrAssembly, extensible))
                    continue;

                lines.Add($"{owner}#ctor{Sep}{Accessibility(ctor.IsPublic, ctor.IsFamily, ctor.IsFamilyOrAssembly)}" +
                          Parameters(ctor.GetParameters()));
            }
        }

        private static string FieldLine(string owner, FieldInfo field)
        {
            var sb = new StringBuilder(owner).Append('.').Append(field.Name).Append(Sep);
            sb.Append(Accessibility(field.IsPublic, field.IsFamily, field.IsFamilyOrAssembly)).Append(' ');

            if (field.IsLiteral)
            {
                sb.Append("const ");
            }
            else
            {
                if (field.IsStatic) sb.Append("static ");
                if (field.IsInitOnly) sb.Append("readonly ");
            }

            sb.Append(TypeName(field.FieldType));

            if (field.IsLiteral)
                sb.Append(" = ").Append(Literal(field.GetRawConstantValue()));

            return sb.ToString();
        }

        private static string PropertyLine(string owner, PropertyInfo property, MethodInfo getter, MethodInfo setter)
        {
            var sb = new StringBuilder(owner).Append('.').Append(property.Name).Append(Sep);

            // 属性的可访问性取两个访问器里更宽的那个（C# 的写法就是「宽的那个写在属性上」）
            MethodInfo lead = IsPublic(getter) ? getter
                : IsPublic(setter) ? setter
                : getter ?? setter;
            string leadAccess = Accessibility(IsPublic(lead), IsProtected(lead), IsProtectedInternal(lead));

            sb.Append(leadAccess).Append(' ');

            if (IsStatic(getter) || IsStatic(setter))
                sb.Append("static ");

            sb.Append(TypeName(property.PropertyType)).Append(' ');

            var indexParameters = property.GetIndexParameters();
            if (indexParameters.Length > 0)
                sb.Append("this[").Append(ParameterList(indexParameters)).Append(']'); // 索引器的名字统一是 Item，渲染成 this[…]
            else
                sb.Append(property.Name);

            var accessors = new StringBuilder();
            AppendAccessor(accessors, "get", getter, leadAccess);
            AppendAccessor(accessors, "set", setter, leadAccess);

            return sb.Append(" { ").Append(accessors).Append(" }").ToString();
        }

        /// <summary>
        /// 渲染一个访问器：与属性同级时写裸 <c>get;</c> / <c>set;</c>（C# 的惯用写法，也与公开面基线该有的
        /// 信噪比一致），更窄时写出它的修饰符（<c>protected set;</c>）。
        /// </summary>
        private static void AppendAccessor(StringBuilder sb, string keyword, MethodInfo accessor, string leadAccess)
        {
            if (accessor == null)
                return;

            if (sb.Length > 0)
                sb.Append(' ');

            string access = Accessibility(IsPublic(accessor), IsProtected(accessor), IsProtectedInternal(accessor));
            if (!string.Equals(access, leadAccess, StringComparison.Ordinal))
                sb.Append(access).Append(' ');

            sb.Append(keyword).Append(';');
        }

        private static string EventLine(string owner, EventInfo evt, MethodInfo adder)
        {
            var sb = new StringBuilder(owner).Append('.').Append(evt.Name).Append(Sep);
            sb.Append(Accessibility(IsPublic(adder), IsProtected(adder), IsProtectedInternal(adder))).Append(' ');

            if (IsStatic(adder))
                sb.Append("static ");

            return sb.Append("event ").Append(TypeName(evt.EventHandlerType)).ToString();
        }

        private static string MethodLine(string owner, Type declaringType, MethodInfo method)
        {
            var sb = new StringBuilder(owner).Append('.').Append(method.Name).Append(Sep);
            sb.Append(Accessibility(method.IsPublic, method.IsFamily, method.IsFamilyOrAssembly)).Append(' ');

            if (method.IsStatic)
                sb.Append("static ");

            // 接口成员不写 abstract/virtual（那是接口语义的冗余，不是该成员的区分维度）
            if (!declaringType.IsInterface)
            {
                bool isOverride = method.GetBaseDefinition() != method;

                if (method.IsAbstract)
                    sb.Append("abstract ");
                else if (isOverride)
                    sb.Append(method.IsFinal ? "sealed override " : "override ");
                else if (method.IsVirtual)
                    sb.Append("virtual ");
            }

            sb.Append(TypeName(method.ReturnType)).Append(Parameters(method.GetParameters()));

            foreach (var parameter in method.GetGenericArguments())
            {
                var parts = new List<string>();
                foreach (var constraint in parameter.GetGenericParameterConstraints())
                    parts.Add(TypeName(constraint));
                parts.Sort(StringComparer.Ordinal);

                if (parts.Count > 0)
                    sb.Append(" where ").Append(parameter.Name).Append(" : ").Append(string.Join(", ", parts));
            }

            return sb.ToString();
        }

        private static string Parameters(ParameterInfo[] parameters)
            => "(" + ParameterList(parameters) + ")";

        /// <summary>参数清单本体（不含外层括号）——索引器要把它包在 <c>this[…]</c> 里。</summary>
        private static string ParameterList(ParameterInfo[] parameters)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");

                var parameter = parameters[i];

                if (parameter.IsDefined(typeof(ParamArrayAttribute), false))
                    sb.Append("params ");
                else if (parameter.ParameterType.IsByRef)
                    sb.Append(parameter.IsOut ? "out " : "ref ");

                sb.Append(TypeName(parameter.ParameterType)).Append(' ').Append(parameter.Name);

                if (!parameter.HasDefaultValue)
                    continue;

                if (parameter.DefaultValue == null)
                    sb.Append(parameter.ParameterType.IsValueType ? " = default" : " = null");
                else
                    sb.Append(" = ").Append(DefaultLiteral(parameter));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 默认值字面量。**包了 try/catch**：<c>ParameterInfo.DefaultValue</c> 对 <c>DateTime</c> /
        /// <c>decimal</c> 型可选参数会抛（本仓当前无此类参数，兜底只为不炸渲染器）。
        /// </summary>
        private static string DefaultLiteral(ParameterInfo parameter)
        {
            try
            {
                return Literal(parameter.DefaultValue);
            }
            catch (Exception)
            {
                return "<unreadable>";
            }
        }

        #endregion

        #region Primitives

        /// <summary>类型的渲染名：泛型实参用**形参名**（<c>IEventStream&lt;T&gt;</c>），不用 <c>Type.FullName</c>
        /// ——后者会把实参渲染成程序集限定名（<c>[[System.Object, mscorlib]]</c>），跨环境不稳定。</summary>
        private static string TypeName(Type type)
        {
            if (type == null)
                return "?";

            if (type.IsGenericParameter)
                return type.Name;

            if (type.IsArray)
            {
                string element = TypeName(type.GetElementType());
                int rank = type.GetArrayRank();
                return rank == 1 ? element + "[]" : element + "[" + new string(',', rank - 1) + "]";
            }

            if (type.IsByRef) return TypeName(type.GetElementType());
            if (type.IsPointer) return TypeName(type.GetElementType()) + "*";

            if (Aliases.TryGetValue(type, out var alias))
                return alias;

            if (type.IsGenericType)
            {
                var arguments = type.GetGenericArguments();
                var sb = new StringBuilder(PlainName(type.GetGenericTypeDefinition())).Append('<');
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(TypeName(arguments[i]));
                }
                return sb.Append('>').ToString();
            }

            return PlainName(type);
        }

        private static string PlainName(Type type)
        {
            string name = type.Name;
            int tick = name.IndexOf('`');
            if (tick >= 0)
                name = name.Substring(0, tick);

            if (type.IsNested && type.DeclaringType != null)
                return PlainName(type.DeclaringType) + "." + name;

            return string.IsNullOrEmpty(type.Namespace) ? name : type.Namespace + "." + name;
        }

        private static string Literal(object value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case string text:
                    return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                case char character:
                    return "'" + (character == '\'' ? "\\'" : character.ToString()) + "'";
                case bool flag:
                    return flag ? "true" : "false";
                case float single:
                    return single.ToString("R", CultureInfo.InvariantCulture) + "f";
                case double number:
                    return number.ToString("R", CultureInfo.InvariantCulture);
                case decimal precise:
                    return precise.ToString(CultureInfo.InvariantCulture) + "m";
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static string Accessibility(bool isPublic, bool isFamily, bool isFamilyOrAssembly)
        {
            if (isPublic) return "public";
            if (isFamilyOrAssembly) return "protected internal";
            return isFamily ? "protected" : "internal";
        }

        /// <summary>收录判据：public 一律收；protected 只在**声明类型可被继承**时收（第三方够得到才算 API）。</summary>
        private static bool IsVisible(bool isPublic, bool isProtected, bool extensible)
            => isPublic || (isProtected && extensible);

        /// <summary>可被继承 = 类且非 sealed（static class 是 abstract+sealed，天然排除）。</summary>
        private static bool CanBeInherited(Type type) => type.IsClass && !type.IsSealed;

        private static bool IsCompilerGenerated(MemberInfo member)
            => member.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false);

        private static bool IsPublic(MethodInfo method) => method != null && method.IsPublic;

        private static bool IsProtected(MethodInfo method)
            => method != null && (method.IsFamily || method.IsFamilyOrAssembly);

        private static bool IsProtectedInternal(MethodInfo method) => method != null && method.IsFamilyOrAssembly;

        private static bool IsStatic(MethodInfo method) => method != null && method.IsStatic;

        #endregion
    }
}
