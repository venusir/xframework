using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XAudio.Fmod.Tests
{
    /// <summary>
    /// Phase 1 的 provider 程序集不应有 AutoInit；自动安装属于 Phase 2 条件集成程序集。
    /// </summary>
    [TestFixture]
    public class FmodAutoInitTests
    {
        [Test]
        public void ProviderAssembly_HasNoAutoInit()
        {
            var offenders = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var type in typeof(FmodAudioSettings).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(flags))
                {
                    if (method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null)
                        offenders.Add(type.FullName + "." + method.Name + " [RuntimeInitializeOnLoadMethod]");
#if UNITY_EDITOR
                    if (method.GetCustomAttribute<UnityEditor.InitializeOnLoadMethodAttribute>() != null)
                        offenders.Add(type.FullName + "." + method.Name + " [InitializeOnLoadMethod]");
#endif
                }
            }

            Assert.IsEmpty(offenders,
                "Phase 1 的 FMOD-free provider 程序集不应有 AutoInit；自动安装应在 Phase 2 条件集成程序集：" +
                "\n  " + string.Join("\n  ", offenders));
        }
    }
}
