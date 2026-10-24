using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 自动初始化族级守卫（本包版）：靠 <see cref="RuntimeInitializeOnLoadMethodAttribute"/>
    /// 触发的成员必须在清单里登记且档位相符，未登记即红。
    /// </summary>
    [TestFixture]
    public sealed class LubanAutoInitTests
    {
        private static readonly (Type Type, string Method, RuntimeInitializeLoadType LoadType)[] Registry =
        {
            (typeof(LubanConfig), "ResetForNewSession", RuntimeInitializeLoadType.SubsystemRegistration),
        };

        [Test]
        public void EveryAutoInitMethod_IsRegisteredWithItsLoadType()
        {
            var violations = new List<string>();

            foreach (var entry in Scan())
            {
                if (Find(entry.Type, entry.Method) == null)
                {
                    violations.Add("未登记：" + entry.Type.Name + "." + entry.Method +
                                   "（loadType=" + entry.LoadType +
                                   "）——新增自动初始化请到 LubanAutoInitTests 的 Registry 登记");
                }
            }

            foreach (var expected in Registry)
            {
                var method = expected.Type.GetMethod(
                    expected.Method,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                {
                    violations.Add("清单登记了 " + expected.Type.Name + "." + expected.Method +
                                   "，但该方法不存在（改名了？）");
                    continue;
                }

                var attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                if (attribute == null)
                {
                    violations.Add(expected.Type.Name + "." + expected.Method +
                                   " 没有 RuntimeInitializeOnLoadMethod");
                    continue;
                }

                if (attribute.loadType != expected.LoadType)
                {
                    violations.Add(expected.Type.Name + "." + expected.Method + " 的 loadType 是 " +
                                   attribute.loadType + "，清单登记的是 " + expected.LoadType);
                }
            }

            Assert.That(violations, Is.Empty,
                "自动初始化族级不变量被破坏：\n  " + string.Join("\n  ", violations));
        }

        private static List<(Type Type, string Method, RuntimeInitializeLoadType LoadType)> Scan()
        {
            var found = new List<(Type, string, RuntimeInitializeLoadType)>();
            foreach (var type in typeof(LubanConfig).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(
                             BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                    if (attribute != null)
                    {
                        found.Add((type, method.Name, attribute.loadType));
                    }
                }
            }

            return found;
        }

        private static (Type Type, string Method, RuntimeInitializeLoadType LoadType)? Find(
            Type type,
            string method)
        {
            foreach (var entry in Registry)
            {
                if (entry.Type == type && entry.Method == method)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
