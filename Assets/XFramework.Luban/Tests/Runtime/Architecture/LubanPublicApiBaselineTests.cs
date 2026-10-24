using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 扩展包 Runtime 程序集的公开 API 基线。扩展包不在核心
    /// <c>PublicApiBaselineTests</c> 射程内，这里用一份紧凑渲染器补上。
    /// <para>更新：<c>pwsh -File Tools/run-tests.ps1 -UpdateLubanApiBaseline</c>
    /// （写盘后有意失败一次，不带开关重跑才绿）。</para>
    /// </summary>
    [TestFixture]
    public sealed class LubanPublicApiBaselineTests
    {
        private const string ExpectedAssemblyName = "Venusy609.Xframework.Luban";
        private const string UpdateEnvVar = "XFW_UPDATE_LUBAN_API_BASELINE";
        private const string Sep = "  ::  ";

        private const string Header =
            "# XFramework Luban Runtime 程序集公开 API 基线（自动生成，勿手工编辑）\n" +
            "#\n" +
            "# 生成：Tests/Runtime/Architecture/LubanPublicApiBaselineTests.cs\n" +
            "# 更新：pwsh -File Tools/run-tests.ps1 -UpdateLubanApiBaseline\n" +
            "# 比较：按 \\n 拆行、去行尾 \\r，忽略 '#' 开头的行；其余任何一行的增删都是公开面变化。\n" +
            "#\n" +
            "# 射程：Runtime 程序集 Venusy609.Xframework.Luban（公开类型与 public 成员）。\n" +
            "# Editor 程序集 Venusy609.Xframework.Luban.Editor 不在射程内。\n";

        [Test]
        public void AssemblyName_IsStable()
        {
            Assert.That(typeof(LubanConfig).Assembly.GetName().Name,
                Is.EqualTo(ExpectedAssemblyName));
        }

        [Test]
        public void PublicApi_MatchesBaseline()
        {
            Assembly assembly = typeof(LubanConfig).Assembly;
            string actual = Render(assembly);
            string path = Path.Combine(
                LubanRuntimeTestPaths.PackageRoot,
                "Tests", "Runtime", "Architecture", "LubanPublicApiBaseline.txt");

            if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, actual, new UTF8Encoding(false));
                Assert.Fail("已重写 LubanPublicApiBaseline.txt，请审阅后不带开关重跑。");
            }

            Assert.That(File.Exists(path), Is.True, "缺少基线文件：" + path);
            string expected = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.That(Normalize(actual), Is.EqualTo(Normalize(expected)));
        }

        private static string Normalize(string text)
        {
            return text.Replace("\r\n", "\n");
        }

        private static string Render(Assembly assembly)
        {
            var lines = new SortedSet<string>(StringComparer.Ordinal);
            Type[] types = assembly.GetExportedTypes();
            Array.Sort(types, (a, b) => string.CompareOrdinal(a.FullName, b.FullName));

            const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.Instance | BindingFlags.Static |
                                         BindingFlags.DeclaredOnly;

            for (int t = 0; t < types.Length; t++)
            {
                Type type = types[t];
                lines.Add(TypeLine(type));

                if (type.IsEnum)
                {
                    foreach (var field in type.GetFields(
                                 BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (!field.IsSpecialName)
                        {
                            lines.Add(TypeName(type) + "." + field.Name + Sep +
                                      "= " + field.GetRawConstantValue());
                        }
                    }

                    continue;
                }

                if (typeof(Delegate).IsAssignableFrom(type))
                {
                    continue;
                }

                foreach (var field in type.GetFields(declared))
                {
                    if (IsVisible(field))
                    {
                        lines.Add(FieldLine(type, field));
                    }
                }

                foreach (var property in type.GetProperties(declared))
                {
                    if (IsVisible(property))
                    {
                        lines.Add(PropertyLine(type, property));
                    }
                }

                foreach (var evt in type.GetEvents(declared))
                {
                    if (IsVisible(evt))
                    {
                        lines.Add(EventLine(type, evt));
                    }
                }

                foreach (var constructor in type.GetConstructors(declared))
                {
                    if (IsVisible(constructor))
                    {
                        lines.Add(MethodLine(type, constructor));
                    }
                }

                foreach (var method in type.GetMethods(declared))
                {
                    if (method.IsSpecialName || !IsVisible(method))
                    {
                        continue;
                    }

                    lines.Add(MethodLine(type, method));
                }
            }

            var builder = new StringBuilder(Header);
            foreach (var line in lines)
            {
                builder.Append(line).Append('\n');
            }

            return builder.ToString();
        }

        private static bool IsVisible(FieldInfo field)
        {
            return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
        }

        private static bool IsVisible(PropertyInfo property)
        {
            MethodInfo getter = property.GetGetMethod(true);
            MethodInfo setter = property.GetSetMethod(true);
            return (getter != null && (getter.IsPublic || getter.IsFamily || getter.IsFamilyOrAssembly)) ||
                   (setter != null && (setter.IsPublic || setter.IsFamily || setter.IsFamilyOrAssembly));
        }

        private static bool IsVisible(EventInfo evt)
        {
            MethodInfo add = evt.GetAddMethod(true);
            return add != null && (add.IsPublic || add.IsFamily || add.IsFamilyOrAssembly);
        }

        private static bool IsVisible(MethodBase method)
        {
            return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
        }

        private static string TypeLine(Type type)
        {
            var builder = new StringBuilder(TypeName(type)).Append(Sep).Append("public ");
            if (type.IsAbstract && type.IsSealed)
            {
                builder.Append("static ");
            }
            else
            {
                if (type.IsAbstract && !type.IsInterface)
                {
                    builder.Append("abstract ");
                }

                if (type.IsSealed && !type.IsValueType && !typeof(Delegate).IsAssignableFrom(type))
                {
                    builder.Append("sealed ");
                }

                if (type.IsValueType && !type.IsEnum &&
                    type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false))
                {
                    builder.Append("readonly ");
                }
            }

            builder.Append(Kind(type));

            if (type.IsEnum)
            {
                builder.Append(" : ").Append(TypeName(Enum.GetUnderlyingType(type)));
                return builder.ToString();
            }

            var parts = new List<string>();
            if (type.BaseType != null &&
                type.BaseType != typeof(object) &&
                type.BaseType != typeof(ValueType))
            {
                parts.Add(TypeName(type.BaseType));
            }

            var interfaces = DirectInterfaces(type);
            interfaces.Sort((a, b) => string.CompareOrdinal(TypeName(a), TypeName(b)));
            for (int i = 0; i < interfaces.Count; i++)
            {
                parts.Add(TypeName(interfaces[i]));
            }

            if (parts.Count > 0)
            {
                builder.Append(" : ").Append(string.Join(", ", parts));
            }

            return builder.ToString();
        }

        private static string Kind(Type type)
        {
            if (type.IsEnum)
            {
                return "enum";
            }

            if (type.IsInterface)
            {
                return "interface";
            }

            return type.IsValueType ? "struct" : "class";
        }

        /// <summary>直接声明的接口：剔除基类型已提供的与接口继承链下层的那部分。</summary>
        private static List<Type> DirectInterfaces(Type type)
        {
            var all = new List<Type>(type.GetInterfaces());

            if (type.BaseType != null)
            {
                Type[] inherited = type.BaseType.GetInterfaces();
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    if (Array.IndexOf(inherited, all[i]) >= 0)
                    {
                        all.RemoveAt(i);
                    }
                }
            }

            for (int i = all.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < all.Count; j++)
                {
                    if (i != j && all[i] != all[j] && all[i].IsAssignableFrom(all[j]))
                    {
                        all.RemoveAt(i);
                        break;
                    }
                }
            }

            return all;
        }

        private static string FieldLine(Type type, FieldInfo field)
        {
            var builder = new StringBuilder(TypeName(type)).Append('.').Append(field.Name).Append(Sep);
            if (field.IsStatic)
            {
                builder.Append("static ");
            }

            if (field.IsInitOnly)
            {
                builder.Append("readonly ");
            }

            builder.Append(TypeName(field.FieldType));
            if (field.IsLiteral)
            {
                builder.Append(" = ").Append(field.GetRawConstantValue());
            }

            return builder.ToString();
        }

        private static string PropertyLine(Type type, PropertyInfo property)
        {
            var builder = new StringBuilder(TypeName(type)).Append('.').Append(property.Name).Append(Sep);
            MethodInfo getter = property.GetGetMethod(true);
            if (getter != null && getter.IsStatic)
            {
                builder.Append("static ");
            }

            builder.Append(TypeName(property.PropertyType)).Append(" { ");
            if (getter != null && (getter.IsPublic || getter.IsFamily || getter.IsFamilyOrAssembly))
            {
                builder.Append("get; ");
            }

            MethodInfo setter = property.GetSetMethod(true);
            if (setter != null && (setter.IsPublic || setter.IsFamily || setter.IsFamilyOrAssembly))
            {
                builder.Append("set; ");
            }

            return builder.Append('}').ToString();
        }

        private static string EventLine(Type type, EventInfo evt)
        {
            var builder = new StringBuilder(TypeName(type)).Append('.').Append(evt.Name).Append(Sep);
            MethodInfo add = evt.GetAddMethod(true);
            if (add != null && add.IsStatic)
            {
                builder.Append("static ");
            }

            return builder.Append("event ").Append(TypeName(evt.EventHandlerType)).ToString();
        }

        private static string MethodLine(Type type, MethodBase method)
        {
            var builder = new StringBuilder(TypeName(type)).Append(Sep);
            if (method.IsStatic)
            {
                builder.Append("static ");
            }

            if (method is MethodInfo info)
            {
                builder.Append(TypeName(info.ReturnType)).Append(' ');
            }

            string name = method.IsConstructor ? ".ctor" : method.Name;
            if (method.IsGenericMethodDefinition)
            {
                Type[] genericArguments = method.GetGenericArguments();
                var names = new string[genericArguments.Length];
                for (int i = 0; i < genericArguments.Length; i++)
                {
                    names[i] = genericArguments[i].Name;
                }

                name += "<" + string.Join(", ", names) + ">";
            }

            builder.Append(name).Append('(');

            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                Type parameterType = parameters[i].ParameterType;
                string modifier = string.Empty;
                if (parameterType.IsByRef)
                {
                    modifier = parameters[i].IsOut ? "out " : "ref ";
                    parameterType = parameterType.GetElementType();
                }

                builder.Append(modifier).Append(TypeName(parameterType));
                builder.Append(' ').Append(parameters[i].Name);
                if (parameters[i].HasDefaultValue)
                {
                    builder.Append(" = ").Append(
                        DefaultText(parameters[i].DefaultValue, parameterType));
                }
            }

            builder.Append(')').Append(Constraints(method));
            return builder.ToString();
        }

        private static string Constraints(MethodBase method)
        {
            if (!method.IsGenericMethodDefinition)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            Type[] arguments = method.GetGenericArguments();
            for (int i = 0; i < arguments.Length; i++)
            {
                Type argument = arguments[i];
                var constraints = new List<string>();
                GenericParameterAttributes attributes = argument.GenericParameterAttributes;
                if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                {
                    constraints.Add("class");
                }

                if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                {
                    constraints.Add("struct");
                }

                Type[] parameterConstraints = argument.GetGenericParameterConstraints();
                for (int c = 0; c < parameterConstraints.Length; c++)
                {
                    if (parameterConstraints[c] != typeof(ValueType))
                    {
                        constraints.Add(TypeName(parameterConstraints[c]));
                    }
                }

                if (constraints.Count > 0)
                {
                    parts.Add("where " + argument.Name + " : " + string.Join(", ", constraints));
                }
            }

            return parts.Count == 0 ? string.Empty : " " + string.Join(" ", parts);
        }

        private static string DefaultText(object value, Type parameterType)
        {
            if (value == null || value == DBNull.Value || value == Missing.Value)
            {
                bool isValueType = parameterType.IsValueType &&
                                   Nullable.GetUnderlyingType(parameterType) == null;
                return isValueType ? "default" : "null";
            }

            if (value is string text)
            {
                return "\"" + text + "\"";
            }

            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string TypeName(Type type)
        {
            if (Aliases.TryGetValue(type, out string alias))
            {
                return alias;
            }

            if (type.IsArray)
            {
                return TypeName(type.GetElementType()) + "[]";
            }

            if (type.IsGenericType)
            {
                string name = type.GetGenericTypeDefinition().FullName;
                int tick = name.IndexOf('`');
                if (tick >= 0)
                {
                    name = name.Substring(0, tick);
                }

                Type[] arguments = type.GetGenericArguments();
                var names = new string[arguments.Length];
                for (int i = 0; i < arguments.Length; i++)
                {
                    names[i] = TypeName(arguments[i]);
                }

                return name + "<" + string.Join(", ", names) + ">";
            }

            return type.FullName ?? type.Name;
        }

        private static readonly Dictionary<Type, string> Aliases = new Dictionary<Type, string>
        {
            [typeof(void)] = "void",
            [typeof(bool)] = "bool",
            [typeof(byte)] = "byte",
            [typeof(sbyte)] = "sbyte",
            [typeof(char)] = "char",
            [typeof(decimal)] = "decimal",
            [typeof(double)] = "double",
            [typeof(float)] = "float",
            [typeof(int)] = "int",
            [typeof(uint)] = "uint",
            [typeof(long)] = "long",
            [typeof(ulong)] = "ulong",
            [typeof(short)] = "short",
            [typeof(ushort)] = "ushort",
            [typeof(string)] = "string",
            [typeof(object)] = "object",
        };
    }
}
