using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="ConfigManager"/> 静态门面测试。
    /// <para>覆盖：未初始化保护、重复初始化忽略、SetInstance 注入转发、ConfigChanged 事件派发、
    /// Destroy 后不可用与重新初始化恢复、已加载后省略路径。</para>
    /// <para>通过 <see cref="ConfigManager.SetInstance"/> 注入真实 <see cref="ConfigManagerImpl"/> +
    /// <see cref="FakeConfigLoader"/>，不依赖真实资源。</para>
    /// </summary>
    class ConfigManagerFacadeTests
    {
        [Serializable]
        private struct TestItemRow : IConfigRow<int>
        {
            public int Id { get; set; }
            public string Name;
        }

        private static ConfigTable<TestItemRow> MakeTable()
        {
            return new ConfigTable<TestItemRow>(
                new Dictionary<int, TestItemRow> { [1] = new TestItemRow { Id = 1 } });
        }

        /// <summary>最近一次注入门面的 fake loader（测试内复用，保证加载任务已注入而非空任务）。</summary>
        private FakeConfigLoader _installedFake;

        /// <summary>注入已配置好测试数据的门面实现（每次调用前先 Destroy 确保干净状态）。</summary>
        private ConfigManagerImpl InstallImpl()
        {
            ConfigManager.Destroy();
            var impl = new ConfigManagerImpl();
            var fake = new FakeConfigLoader();
            fake.SetTableTask(UniTask.FromResult(MakeTable()));
            _installedFake = fake;
            ConfigManager.SetInstance(impl);
            return impl;
        }

        [TearDown]
        public void TearDown()
        {
            ConfigManager.Destroy();
        }

        [Test]
        public void Uninitialized_Access_ThrowsInvalidOperationException()
        {
            ConfigManager.Destroy();
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigManager.GetTable<TestItemRow>());
            StringAssert.Contains("[ConfigManager]", ex.Message, "未初始化异常消息应带 [ConfigManager] 前缀");
        }

        [Test]
        public void Initialize_Twice_LogsWarningAndIgnores()
        {
            LogAssert.Expect(LogType.Warning,
                "[ConfigManager] Initialize was called more than once. Ignoring duplicate.");
            ConfigManager.Initialize();
            ConfigManager.Initialize();
        }

        [Test]
        public void SetInstance_PreloadAndQuery_Works()
        {
            InstallImpl();
            var table = ConfigManager.PreloadTableAsync<TestItemRow>("config/items", _installedFake)
                .GetAwaiter().GetResult();

            Assert.IsTrue(ConfigManager.IsLoaded<TestItemRow>());
            Assert.AreSame(table, ConfigManager.GetTable<TestItemRow>(), "门面查询应转发到同一包装器");
        }

        [Test]
        public void ConfigChanged_FiresOnPreload()
        {
            InstallImpl();
            var received = (Type)null;
            ConfigManager.ConfigChanged += OnChanged;
            try
            {
                ConfigManager.PreloadTableAsync<TestItemRow>("config/items", _installedFake)
                    .GetAwaiter().GetResult();
            }
            finally
            {
                ConfigManager.ConfigChanged -= OnChanged;
            }
            Assert.AreEqual(typeof(TestItemRow), received, "预加载完成后应派发 ConfigChanged 事件");

            void OnChanged(Type type) => received = type;
        }

        [Test]
        public void Destroy_ThenReinitialize_Recovers()
        {
            InstallImpl();
            ConfigManager.Destroy();

            // 销毁后：未初始化异常
            Assert.Throws<InvalidOperationException>(() => ConfigManager.GetTable<TestItemRow>());

            // 重新初始化后：恢复可用，查询报「未加载」而非「未初始化」
            ConfigManager.Initialize();
            Assert.Throws<ConfigException>(() => ConfigManager.GetTable<TestItemRow>());
        }

        [Test]
        public void Preload_LoadedThenEmptyPath_NoThrow()
        {
            InstallImpl();
            ConfigManager.PreloadTableAsync<TestItemRow>("config/items", _installedFake)
                .GetAwaiter().GetResult();

            Assert.DoesNotThrow(() =>
                ConfigManager.PreloadTableAsync<TestItemRow>("").GetAwaiter().GetResult());
        }

        /// <summary>Global 配置类型——用于触发 <see cref="ConfigManager.ConfigChanged"/>。</summary>
        private sealed class TestGlobalConfig
        {
            public int Value;
        }

        [Test]
        public void SetInstance_SameImplTwice_ConfigChangedFiresOnce()
        {
            // **这条钉住的是既有正确行为，不是修好的缺陷。**
            // 审计时曾怀疑「同一个实例连调两次 SetInstance 会留下两份订阅、事件派发两次」——
            // 复现把它**证伪**了：SetInstance 里 `if (_instance is ConfigManagerImpl oldImpl)
            // UnsubscribeImplEvents(oldImpl)` 对**同一个实例**同样成立，于是先退订再订阅，净一份。
            // 保留本用例是因为这条不变量此前没人钉过，而它依赖的是「退订旧实例」那两行的存在
            var impl = InstallImpl();
            ConfigManager.SetInstance(impl);

            var count = 0;
            Action<Type> handler = _ => count++;
            ConfigManager.ConfigChanged += handler;
            try
            {
                ConfigManager.RegisterGlobal(new TestGlobalConfig());

                Assert.AreEqual(1, count, "同一个实例重复 SetInstance 不得让事件派发两次");
            }
            finally
            {
                ConfigManager.ConfigChanged -= handler;
            }
        }

        [Test]
        public void Preload_SubscriberThrows_DataStillRegisteredAndNoExceptionEscapes()
        {
            // 订阅者异常此前落在加载路径的 try **之内** → 被包成 ConfigException 抛给调用方，
            // 而 `_tables` / `_tableWrappers` / `_assetPaths` 三个字典已经写完。
            // 调用方看到「加载失败」，数据其实已注册（重试还会命中「已加载」直接返回缓存）——报错与服务实态相反
            InstallImpl();

            Action<Type> thrower = _ => throw new InvalidOperationException("subscriber boom");
            ConfigManager.ConfigChanged += thrower;
            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[Config\] ConfigChanged 的订阅者抛出异常"));

                var table = ConfigManager.PreloadTableAsync<TestItemRow>("config/items", _installedFake)
                    .GetAwaiter().GetResult();

                Assert.IsNotNull(table, "订阅者抛异常不得让加载失败");
                Assert.IsTrue(ConfigManager.IsLoaded<TestItemRow>(), "数据应已注册");
                Assert.IsTrue(ConfigManager.TryGetTable<TestItemRow>(out _), "查询面应能看到它");
            }
            finally
            {
                ConfigManager.ConfigChanged -= thrower;
            }
        }

        #region 零覆盖补齐（2026-09-30 审计）

        // 本区全部是**新增守卫，不是回归**：下列成员此前零覆盖，而当前行为已经正确——
        // 它们买的是「以后改坏即红」。如实记在这里，免得下一轮审计把它们当成修好的缺陷。

        [Test]
        public void RegisterAndQuery_AllPublicMembers_WorkThroughFacade()
        {
            // 一次性覆盖八个此前零调用的成员：RegisterTable<T> / RegisterGlobal<T> / IsLoaded<T> /
            // TryGetTable / Get<T,TKey> / TryGet<T,TKey> / GetGlobal<T> / TryGetGlobal<T> / Unload<T>
            InstallImpl();

            ConfigManager.RegisterTable(MakeTable());
            ConfigManager.RegisterGlobal(new TestGlobalConfig { Value = 9 });

            Assert.IsTrue(ConfigManager.IsLoaded<TestItemRow>());
            Assert.IsTrue(ConfigManager.TryGetTable<TestItemRow>(out var table));
            Assert.AreEqual(1, table.Count);
            Assert.AreEqual(1, ConfigManager.Get<TestItemRow, int>(1).Id);
            Assert.IsTrue(ConfigManager.TryGet<TestItemRow, int>(1, out var row));
            Assert.AreEqual(1, row.Id);
            Assert.AreEqual(9, ConfigManager.GetGlobal<TestGlobalConfig>().Value);
            Assert.IsTrue(ConfigManager.TryGetGlobal<TestGlobalConfig>(out var global));
            Assert.AreEqual(9, global.Value);

            ConfigManager.Unload<TestItemRow>();
            Assert.IsFalse(ConfigManager.IsLoaded<TestItemRow>(), "Unload 后应不再报告已加载");
        }

        [Test]
        public void RegisterTable_NonGeneric_RegistersByRowType()
        {
            InstallImpl();
            IConfigTable table = MakeTable();

            ConfigManager.RegisterTable(typeof(TestItemRow), table);

            Assert.IsTrue(ConfigManager.IsLoaded<TestItemRow>());
            Assert.IsTrue(ConfigManager.TryGetTable<TestItemRow>(out var wrapped));
            Assert.AreEqual(1, wrapped.Count);
        }

        [Test]
        public void SetInstance_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ConfigManager.SetInstance(null));
        }

        [Test]
        public void PreloadTable_UnknownFormat_WrappedAsConfigException()
        {
            // format→内置 Loader 的分派此前从未执行过。用未注册的枚举值走这条分派：
            // 装载器表里没有它 → KeyNotFoundException → 被包装成 ConfigException。
            // 这条路径**不需要 AssetManager**（查表发生在调用装载器之前）
            InstallImpl();

            var ex = Assert.Throws<ConfigException>(() =>
                ConfigManager.PreloadTableAsync<TestItemRow>("config/items", (ConfigFormat)99)
                    .GetAwaiter().GetResult());

            StringAssert.Contains("Failed to preload Table", ex.Message);
        }

        [Test]
        public void ConfigManifest_RecordsAddedEntries()
        {
            // ConfigManifest 此前**整个类型零覆盖**
            var manifest = new ConfigManifest();
            manifest.AddTable<TestItemRow>("config/items", group: "battle", format: ConfigFormat.Csv);
            manifest.AddGlobal<TestGlobalConfig>("config/global", group: "battle");

            Assert.AreEqual(2, manifest.Entries.Count);
            Assert.AreEqual(typeof(TestItemRow), manifest.Entries[0].RowType);
            Assert.AreEqual("config/items", manifest.Entries[0].AssetPath);
            Assert.AreEqual("battle", manifest.Entries[0].Group);
            Assert.IsTrue(manifest.Entries[0].IsTable);
            Assert.AreEqual(ConfigFormat.Csv, manifest.Entries[0].Format);
            Assert.IsFalse(manifest.Entries[1].IsTable);
            // AddGlobal **没有** format 参数、内部固定写 Json —— 钉住现状（审计 F10 已归档）
            Assert.AreEqual(ConfigFormat.Json, manifest.Entries[1].Format);
        }

        [Test]
        public void PreloadGroupAndAll_DegenerateInputs_BehaveAsDocumented()
        {
            InstallImpl();
            var manifest = new ConfigManifest();
            manifest.AddTable<TestItemRow>("config/items", group: "battle");

            // 分组名不匹配 → 没有条目命中 → 不碰装载器（这条路径同样不需要 AssetManager）
            Assert.DoesNotThrow(() =>
                ConfigManager.PreloadGroupAsync("nonexistent", manifest).GetAwaiter().GetResult());

            // 空清单 → 循环不执行
            Assert.DoesNotThrow(() =>
                ConfigManager.PreloadAllAsync(new ConfigManifest()).GetAwaiter().GetResult());

            // 参数防御
            Assert.Throws<ArgumentNullException>(() =>
                ConfigManager.PreloadGroupAsync("battle", null).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() =>
                ConfigManager.PreloadGroupAsync(null, manifest).GetAwaiter().GetResult());
        }

        #endregion
    }
}
