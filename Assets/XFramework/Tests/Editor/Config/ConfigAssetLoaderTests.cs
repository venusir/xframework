using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 配置资源装载器的句柄释放与错误归因（R4-CONF-002）。
    /// <para>真实的 <see cref="AssetHandle{T}"/> 只能由 YooAsset 句柄构造，测试进程造不出
    /// 「有效句柄 + <c>Asset == null</c>」。这里经 internal 的
    /// <see cref="IConfigAssetLease{T}"/> 缝注入该状态，锁住「类型不符也释放、且恰好一次」。</para>
    /// </summary>
    [TestFixture]
    public class ConfigAssetLoaderTests
    {
        private sealed class FakeLease<T> : IConfigAssetLease<T> where T : UnityEngine.Object
        {
            public T Asset { get; set; }

            public string Location { get; set; }

            public AssetOperationStatus Status { get; set; }

            public string LastError { get; set; }

            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        [Serializable]
        private struct SettingsRow : IConfigRow<int>
        {
            public int Id;

            int IConfigRow<int>.Id => Id;
        }

        private static ConfigException ExpectFault(UniTask task)
        {
            Assert.IsTrue(task.Status.IsFaulted(), "预期返回的 UniTask 处于 faulted 状态");
            return Assert.Throws<ConfigException>(() => task.GetAwaiter().GetResult());
        }

        private static ConfigException ExpectFault<T>(UniTask<T> task)
        {
            Assert.IsTrue(task.Status.IsFaulted(), "预期返回的 UniTask 处于 faulted 状态");
            return Assert.Throws<ConfigException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void LoadTextAsync_TypeMismatch_ReleasesLeaseOnceAndExplains()
        {
            var lease = new FakeLease<TextAsset>
            {
                Asset = null,
                Status = AssetOperationStatus.Succeed,
                Location = "config/rows.json",
                LastError = string.Empty,
            };

            var task = ConfigTextLoader.LoadTextAsync(
                "config/rows.json",
                _ => UniTask.FromResult<IConfigAssetLease<TextAsset>>(lease));
            var ex = ExpectFault(task);

            StringAssert.Contains("type mismatch", ex.Message);
            StringAssert.Contains("TextAsset", ex.Message);
            StringAssert.Contains("config/rows.json", ex.Message);
            Assert.AreEqual(1, lease.DisposeCount, "类型不符也必须释放句柄，且恰好一次");
        }

        [Test]
        public void LoadTextAsync_LoadFailed_ReportsStatusAndError()
        {
            var lease = new FakeLease<TextAsset>
            {
                Asset = null,
                Status = AssetOperationStatus.Failed,
                Location = "config/rows.json",
                LastError = "404 not found",
            };

            var task = ConfigTextLoader.LoadTextAsync(
                "config/rows.json",
                _ => UniTask.FromResult<IConfigAssetLease<TextAsset>>(lease));
            var ex = ExpectFault(task);

            StringAssert.Contains("Failed to load", ex.Message);
            StringAssert.Contains("404 not found", ex.Message);
            Assert.AreEqual(1, lease.DisposeCount);
        }

        [Test]
        public void LoadTextAsync_NotStarted_MentionsInitialization()
        {
            var lease = new FakeLease<TextAsset>
            {
                Asset = null,
                Status = AssetOperationStatus.None,
                Location = string.Empty,
                LastError = string.Empty,
            };

            var task = ConfigTextLoader.LoadTextAsync(
                "config/rows.json",
                _ => UniTask.FromResult<IConfigAssetLease<TextAsset>>(lease));
            var ex = ExpectFault(task);

            StringAssert.Contains("not be initialized", ex.Message);
            StringAssert.Contains("config/rows.json", ex.Message);
            Assert.AreEqual(1, lease.DisposeCount);
        }

        [Test]
        public void LoadTextAsync_Success_ReturnsTextAndReleases()
        {
            var asset = new TextAsset("{\"a\":1}");
            var lease = new FakeLease<TextAsset>
            {
                Asset = asset,
                Status = AssetOperationStatus.Succeed,
                Location = "config/rows.json",
            };

            try
            {
                var text = ConfigTextLoader.LoadTextAsync(
                        "config/rows.json",
                        _ => UniTask.FromResult<IConfigAssetLease<TextAsset>>(lease))
                    .GetAwaiter().GetResult();

                Assert.AreEqual("{\"a\":1}", text);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }

            Assert.AreEqual(1, lease.DisposeCount);
        }

        [Test]
        public void LoadTableAsync_TypeMismatch_ReleasesLeaseOnce()
        {
            var lease = new FakeLease<ScriptableObject>
            {
                Asset = null,
                Status = AssetOperationStatus.Succeed,
                Location = "config/settings.asset",
                LastError = string.Empty,
            };
            var loader = new ScriptableObjectLoader(
                _ => UniTask.FromResult<IConfigAssetLease<ScriptableObject>>(lease));

            var task = ((IConfigLoader)loader).LoadTableAsync<SettingsRow, int>("config/settings.asset");
            var ex = ExpectFault(task);

            StringAssert.Contains("type mismatch", ex.Message);
            StringAssert.Contains("config/settings.asset", ex.Message);
            Assert.AreEqual(1, lease.DisposeCount);
        }
    }
}
