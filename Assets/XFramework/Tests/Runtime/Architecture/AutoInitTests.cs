using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using XFramework.XAudio;
using XFramework.XFileManager;
using XFramework.XLock;
using XFramework.XLog;
using XFramework.XMessage;
using XFramework.XSerialize;
using XFramework.XTimer;
using XFramework.XUpdate;

namespace XFramework.Architecture.Tests
{
    /// <summary>
    /// 自动初始化**族级守卫**：框架里所有靠特性自初始化的静态服务，必须挂
    /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/>，且它的 load type 是一次**有意识的决定**
    /// ——登记在下面的清单里，**未登记即红**。
    /// <para><b>为什么锁这个</b>：缺陷不在方法体，而在它**什么时候被调用**。只挂编辑器特性时，关闭
    /// Reload Domain（Project Settings → Editor → Enter Play Mode Options）后进入播放不会重新加载
    /// 程序集，该回调不再执行；而测试无法复现「进入播放两次」，只能直接锁特性（模块级先例：
    /// <c>UpdateManagerTests.AutoInit_HasRuntimeInitializeOnLoadMethod</c>）。族级清单比逐个写死断言宽：
    /// 将来新增自动初始化的人**必须来登记一次**，否则本用例红——这条正是本项目存在的理由（旧形状之所以
    /// 扩散，是因为没有一处地方要求新成员做决定）。</para>
    /// <para><b>放 PlayMode 而不是 EditMode</b>：<c>Documentation/Workflow.md</c> §二 明写带 <c>-Filter</c>
    /// 且未指定平台时只跑 PlayMode，EditMode 侧的守卫会被最常见的开发回路静默跳过。</para>
    /// <para><b>本用例不覆盖什么</b>：编辑器侧的 <c>[InitializeOnLoadMethod]</c> 没被断言——运行时程序集
    /// 看不到 <c>UnityEditor</c>。少了它不会让模块在播放模式下失效（运行时特性兜着），只影响「域加载即
    /// 就绪」；需要的话得另写一条 EditMode 用例，但那要把清单复制一份（两个测试程序集互不可见），
    /// 两份真相不如不写。</para>
    /// </summary>
    [TestFixture]
    public class AutoInitTests
    {
        /// <summary>
        /// 族清单：<c>(类型, 方法名, 期望的 load type)</c>。**新增自动初始化时来这里登记**。
        /// <para>load type 按需要选，不为统一而统一：需要早于首个场景 Awake 的用
        /// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/>（如 File 的域根预热——它的价值就在
        /// 「早于首次使用」，默认档位晚于首场景 Awake 等于没预热）；需要注入 PlayerLoop 的用
        /// <see cref="RuntimeInitializeLoadType.AfterAssembliesLoaded"/>；只做订阅/注册的用默认档即可。</para>
        /// </summary>
        private static readonly (Type Type, string Method, RuntimeInitializeLoadType LoadType)[] Registry =
        {
            (typeof(UpdateManager), "AutoInit", RuntimeInitializeLoadType.SubsystemRegistration),
            (typeof(UpdateManager), "AutoInjectDriver", RuntimeInitializeLoadType.AfterAssembliesLoaded),
            (typeof(LockManager), "AutoInit", RuntimeInitializeLoadType.AfterSceneLoad),
            (typeof(MessageManager), "AutoInit", RuntimeInitializeLoadType.AfterSceneLoad),
            (typeof(Serializer), "AutoInit", RuntimeInitializeLoadType.AfterSceneLoad),
            (typeof(AudioManager), "AutoInit", RuntimeInitializeLoadType.AfterSceneLoad),
            (typeof(TimerManager), "AutoInit", RuntimeInitializeLoadType.SubsystemRegistration),
            (typeof(DesktopFileProvider), "PrimeRootsOnLoad", RuntimeInitializeLoadType.BeforeSceneLoad),
            (typeof(LogManager), "AutoInit", RuntimeInitializeLoadType.SubsystemRegistration),
        };

        [Test]
        public void EveryAutoInitMethod_IsRegisteredWithItsLoadType()
        {
            var found = ScanAssemblyForAutoInit();
            var violations = new List<string>();

            // ① 扫到的每一处都必须在清单里（新增成员会在这里红）
            foreach (var entry in found)
            {
                if (Find(entry.Type, entry.Method) == null)
                    violations.Add($"未登记：{entry.Type.Name}.{entry.Method}（loadType={entry.LoadType}）" +
                                   "——新增自动初始化请到 AutoInitTests 的 Registry 登记并写明档位理由");
            }

            // ② 清单里的每一处都必须存在且档位相符
            foreach (var expected in Registry)
            {
                var method = expected.Type.GetMethod(expected.Method,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (method == null)
                {
                    violations.Add($"清单登记了 {expected.Type.Name}.{expected.Method}，但该方法不存在（改名了？）");
                    continue;
                }

                var attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                if (attribute == null)
                {
                    violations.Add($"{expected.Type.Name}.{expected.Method} 没有 RuntimeInitializeOnLoadMethod" +
                                   "——只挂编辑器特性时，关闭 Reload Domain 后进入播放不会执行它");
                    continue;
                }

                if (attribute.loadType != expected.LoadType)
                    violations.Add($"{expected.Type.Name}.{expected.Method} 的 loadType 是 {attribute.loadType}，" +
                                   $"清单登记的是 {expected.LoadType}");
            }

            Assert.IsEmpty(violations, "自动初始化族级不变量被破坏：\n  " + string.Join("\n  ", violations));
        }

        /// <summary>扫运行程序集里所有挂 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 的静态方法。</summary>
        private static List<(Type Type, string Method, RuntimeInitializeLoadType LoadType)> ScanAssemblyForAutoInit()
        {
            var found = new List<(Type, string, RuntimeInitializeLoadType)>();

            foreach (var type in typeof(LockManager).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                    if (attribute != null)
                        found.Add((type, method.Name, attribute.loadType));
                }
            }

            return found;
        }

        private static (Type Type, string Method, RuntimeInitializeLoadType LoadType)? Find(Type type, string method)
        {
            foreach (var entry in Registry)
            {
                if (entry.Type == type && entry.Method == method)
                    return entry;
            }

            return null;
        }

        /// <summary>
        /// 自检：清单里的类型都取自被测程序集（防止把测试程序集自己的类型写进清单，那会让扫描与清单对不上）。
        /// </summary>
        [Test]
        public void Registry_TypesBelongToTheFrameworkAssembly()
        {
            var assembly = typeof(LockManager).Assembly;

            foreach (var entry in Registry)
                Assert.AreSame(assembly, entry.Type.Assembly,
                    $"{entry.Type.Name} 不属于框架程序集——清单只登记框架自己的自动初始化");
        }
    }
}
