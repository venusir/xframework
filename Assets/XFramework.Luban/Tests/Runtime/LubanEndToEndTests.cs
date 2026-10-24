using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XConfig;
using XFramework.XConfig.Luban.Tests.Generated;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 真实 Luban 5.1.0 生成物 + 生成式行接口实现/适配器 + ConfigManager 全链路用例。
    /// 数据经 fake provider 从磁盘读入，绕开 YooAsset。
    /// </summary>
    [TestFixture]
    public sealed class LubanEndToEndTests
    {
        [SetUp]
        public void SetUp()
        {
            LubanConfig.Destroy();
            ConfigManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            LubanConfig.Destroy();
            ConfigManager.Destroy();
        }

        [Test]
        public void Adapter_HasExpectedShape()
        {
            var adapter = new GeneratedLubanConfigAdapter();

            Assert.That(adapter.DataFileExtension, Is.EqualTo(".bytes"));
            Assert.That(adapter.TableNames.Count, Is.EqualTo(5));
        }

        [Test]
        public async Task Initialize_RegistersKeyedTablesAndGlobals()
        {
            var adapter = new GeneratedLubanConfigAdapter();

            await LubanConfig.InitializeForTestsAsync(adapter, CreateProvider(adapter));

            Assert.That(ConfigManager.Get<cfg.demo.Item, int>(1001).Name, Is.EqualTo("Sword"));
            Assert.That(ConfigManager.GetTable<cfg.demo.Item>().Get(1002).Price, Is.EqualTo(150));
            Assert.That(ConfigManager.GetGlobal<cfg.demo.Global>().Version, Is.EqualTo("1.0.0"));
            Assert.That(ConfigManager.Get<cfg.demo.Monster, int>(2).Hp, Is.EqualTo(200));
            Assert.That(ConfigManager.Get<cfg.demo.SkillLevel, (int, int)>((1, 2)).Desc,
                Is.EqualTo("b"));

            Assert.That(ConfigManager.IsLoaded<cfg.demo.SpawnPoint>(), Is.False);

            var tables = LubanConfig.GetTables<cfg.Tables>();
            Assert.That(tables.TbSpawnPoint.DataList.Count, Is.EqualTo(2));
            Assert.That(LubanConfig.Tables, Is.SameAs(tables));
        }

        [Test]
        public async Task Destroy_UnregistersAndAllowsReload()
        {
            var adapter = new GeneratedLubanConfigAdapter();
            var provider = CreateProvider(adapter);

            await LubanConfig.InitializeForTestsAsync(adapter, provider);
            Assert.That(ConfigManager.IsLoaded<cfg.demo.Item>(), Is.True);

            LubanConfig.Destroy();
            Assert.That(ConfigManager.IsLoaded<cfg.demo.Item>(), Is.False);

            await LubanConfig.InitializeForTestsAsync(adapter, provider);
            Assert.That(ConfigManager.IsLoaded<cfg.demo.Item>(), Is.True);
            Assert.That(ConfigManager.Get<cfg.demo.Item, int>(1001).Name, Is.EqualTo("Sword"));
        }

        private static FakeLubanProvider CreateProvider(GeneratedLubanConfigAdapter adapter)
        {
            var provider = new FakeLubanProvider();
            foreach (var name in adapter.TableNames)
            {
                string path = Path.Combine(
                    LubanRuntimeTestPaths.DataRoot, name + adapter.DataFileExtension);
                provider.Data[name] = File.ReadAllBytes(path);
            }

            return provider;
        }
    }
}
