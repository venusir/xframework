using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// 门面注册查询（<c>TrySettings</c> / <c>IsRegistered</c>）与默认路径 <c>Initialize</c> 重载的测试。
    /// </summary>
    [TestFixture]
    public class SettingsRegistryTests
    {
        #region Test Doubles

        [Serializable]
        private sealed class DefaultPathSettings
        {
            public int Volume = 1;
        }

        /// <summary>与 <see cref="GroupB"/> 里的同名类型构成「默认路径撞车」，两者短名均为 Same。</summary>
        private static class GroupA
        {
            [Serializable]
            public sealed class Same
            {
                public int V;
            }
        }

        private static class GroupB
        {
            [Serializable]
            public sealed class Same
            {
                public int V;
            }
        }

        private sealed class MemoryStore : ISettingsStore
        {
            public object Data;

            public bool Exists() => Data != null;

            public T Load<T>() where T : class, new() => Data as T ?? new T();

            public void Save<T>(T settings) where T : class, new() => Data = settings;

            public void Delete() => Data = null;
        }

        #endregion

        #region Fixture

        /// <summary>默认路径会写到真实的 persistentDataPath，测试产生的文件必须清掉。</summary>
        private readonly List<string> _filesToDelete = new();

        [SetUp]
        public void SetUp() => SettingsManager.Destroy();

        [TearDown]
        public void TearDown()
        {
            SettingsManager.Destroy();

            foreach (var path in _filesToDelete)
            {
                if (File.Exists(path))
                    File.Delete(path);
            }

            _filesToDelete.Clear();
        }

        #endregion

        #region 注册查询

        [Test]
        public void TrySettings_NotRegistered_ReturnsFalseWithoutThrowing()
        {
            var ok = SettingsManager.TrySettings<DefaultPathSettings>(out var settings);

            Assert.IsFalse(ok);
            Assert.IsNull(settings);
        }

        [Test]
        public void TrySettings_Registered_ReturnsTrueAndInstance()
        {
            SettingsManager.Initialize<DefaultPathSettings>(
                new MemoryStore(), () => new DefaultPathSettings { Volume = 7 });

            var ok = SettingsManager.TrySettings<DefaultPathSettings>(out var settings);

            Assert.IsTrue(ok);
            Assert.AreSame(SettingsManager.Settings<DefaultPathSettings>(), settings, "取到的应是同一个对象");
            Assert.AreEqual(7, settings.Volume);
        }

        [Test]
        public void TrySettings_AfterDestroy_ReturnsFalseWithoutThrowing()
        {
            SettingsManager.Initialize<DefaultPathSettings>(new MemoryStore());
            SettingsManager.Destroy();

            var ok = SettingsManager.TrySettings<DefaultPathSettings>(out var settings);

            Assert.IsFalse(ok);
            Assert.IsNull(settings, "已销毁等同于未注册——探测路径不该抛异常");
        }

        [Test]
        public void IsRegistered_ReflectsRegistrationAndDestroy()
        {
            Assert.IsFalse(SettingsManager.IsRegistered<DefaultPathSettings>());

            SettingsManager.Initialize<DefaultPathSettings>(new MemoryStore());
            Assert.IsTrue(SettingsManager.IsRegistered<DefaultPathSettings>());

            SettingsManager.Destroy();
            Assert.IsFalse(SettingsManager.IsRegistered<DefaultPathSettings>());
        }

        [Test]
        public void Settings_NotRegistered_StillThrowsWithRepairHint()
        {
            // 探测接口是新增，但原来的「未初始化即抛」语义必须保留——
            // 否则真正的配置遗漏会被 TrySettings 之外的调用方静默吞掉
            var ex = Assert.Throws<InvalidOperationException>(
                () => SettingsManager.Settings<DefaultPathSettings>());

            StringAssert.Contains("Initialize", ex.Message);
        }

        #endregion

        #region 默认路径

        [Test]
        public void Initialize_DefaultPath_NoArgs_WritesUnderPersistentDataPath()
        {
            var path = Application.persistentDataPath + "/" + nameof(DefaultPathSettings) + ".json";
            _filesToDelete.Add(path);

            SettingsManager.Initialize<DefaultPathSettings>(); // 零参数：默认路径 + 无工厂 + 无选项
            SettingsManager.Save<DefaultPathSettings>();

            Assert.IsTrue(File.Exists(path), $"默认路径应为 {path}");
        }

        [Test]
        public void Initialize_DefaultPath_SameShortNameDifferentTypes_Throws()
        {
            // 默认路径取类型短名，两个不同命名空间下的 Same 会算出同一个文件。
            // 若放任不管，两份设置会互相覆盖且毫无提示
            SettingsManager.Initialize<GroupA.Same>();

            var ex = Assert.Throws<InvalidOperationException>(() => SettingsManager.Initialize<GroupB.Same>());

            StringAssert.Contains("Same", ex.Message);
            StringAssert.Contains("显式路径", ex.Message, "报错要给出可行的修复方向");
        }

        [Test]
        public void Initialize_DefaultPath_SameTypeTwice_IsIdempotent()
        {
            SettingsManager.Initialize<DefaultPathSettings>();

            // 同一类型重复初始化走既有幂等路径（打 LogWarning 后返回原实例），
            // 不应被「路径占用检查」误判成撞车
            LogAssert.Expect(LogType.Warning,
                "[SettingsManager] Initialize<DefaultPathSettings> was called more than once. Ignoring duplicate.");
            var second = SettingsManager.Initialize<DefaultPathSettings>();

            Assert.IsNotNull(second);
        }

        #endregion
    }
}
