using System;
using NUnit.Framework;
using UnityEngine;

namespace XFramework.XFile.Tests
{
    /// <summary>
    /// 护栏：<c>DesktopFileProvider.PrimeRootsOnLoad</c> 所在的
    /// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/> 时机，<c>Application.*Path</c> 必须已可用。
    /// <para><b>为什么值得长期留着</b>：这三条路径的可用时机是 **Unity 的平台行为**，而失败是**静默**的
    /// ——<c>PrimeRoots</c> 遇空串直接 return、不缓存不告警，随后由 <c>GetDomainRoot</c> 的懒加载兜底；
    /// 也就是说域根预热会悄悄失效，没有任何症状。本用例是唯一会在别的平台上把这件事喊出来的机制。</para>
    /// <para>实测记录（2026-09-28，Windows 编辑器）：三条路径在该时机均非空
    /// （<c>persistentDataPath</c> / <c>streamingAssetsPath</c> / <c>temporaryCachePath</c>）；
    /// 其它平台未逐一实测——正是这条护栏的用途。</para>
    /// </summary>
    [TestFixture]
    public class FileRootsTimingTests
    {
        internal static bool HookRan;
        internal static bool AppDataOk;
        internal static bool StreamingOk;
        internal static bool CacheOk;
        internal static string AppDataValue;
        internal static string StreamingValue;
        internal static string CacheValue;

        /// <summary>钩子：与 <c>PrimeRootsOnLoad</c> 同一档位，记录该时机三条路径的可用性。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CapturePaths()
        {
            HookRan = true;

            AppDataValue = SafeGet(() => Application.persistentDataPath);
            StreamingValue = SafeGet(() => Application.streamingAssetsPath);
            CacheValue = SafeGet(() => Application.temporaryCachePath);

            AppDataOk = !string.IsNullOrEmpty(AppDataValue);
            StreamingOk = !string.IsNullOrEmpty(StreamingValue);
            CacheOk = !string.IsNullOrEmpty(CacheValue);
        }

        /// <summary>路径属性本身也可能抛（那同样是「该档位不可用」的证据）。</summary>
        private static string SafeGet(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception e)
            {
                return $"<抛异常: {e.GetType().Name}>";
            }
        }

        [Test]
        public void BeforeSceneLoad_ApplicationPathsAreAvailable()
        {
            TestContext.WriteLine(
                $"hookRan={HookRan} | persistentDataPath=\"{AppDataValue}\" | " +
                $"streamingAssetsPath=\"{StreamingValue}\" | temporaryCachePath=\"{CacheValue}\"");

            Assert.IsTrue(HookRan, "BeforeSceneLoad 钩子应已执行（否则本探针无效）");
            Assert.IsTrue(AppDataOk, "BeforeSceneLoad 时机 persistentDataPath 应可用");
            Assert.IsTrue(StreamingOk, "BeforeSceneLoad 时机 streamingAssetsPath 应可用");
            Assert.IsTrue(CacheOk, "BeforeSceneLoad 时机 temporaryCachePath 应可用");
        }
    }
}
