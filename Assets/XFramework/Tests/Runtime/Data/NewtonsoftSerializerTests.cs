using System;
using NUnit.Framework;
using XFramework.XSerialize;

namespace XFramework.XData.Tests
{
    /// <summary>
    /// NewtonsoftSerializer 测试：默认切换、数据往返、旧 JsonUtility 存档兼容。
    /// </summary>
    [TestFixture]
    public class NewtonsoftSerializerTests
    {
        [Serializable]
        private sealed class PlayerData
        {
            public string name;
            public int level;
        }

        [SetUp]
        public void SetUp()
        {
            Serializer.Initialize();
        }

        [Test]
        public void Default_IsNewtonsoft()
        {
            Assert.IsInstanceOf<NewtonsoftSerializer>(Serializer.Default, "默认序列化器应为 Newtonsoft");
            Assert.IsInstanceOf<JsonSerializer>(Serializer.Get("json-utility"), "遗留 JsonUtility 序列化器保留为 json-utility");
        }

        [Test]
        public void RoundTrip_PlainData()
        {
            var data = new PlayerData { name = "勇者", level = 10 };

            var bytes = Serializer.Get("json").Serialize(data, typeof(PlayerData));
            var restored = (PlayerData)Serializer.Get("json").Deserialize(bytes, typeof(PlayerData));

            Assert.AreEqual("勇者", restored.name);
            Assert.AreEqual(10, restored.level);
        }

        [Test]
        public void LegacyJsonUtilityBytes_DeserializedByNewtonsoft()
        {
            // 旧档核心兼容：JsonUtility 产出的标准 JSON 可被 Newtonsoft 无损读取
            var legacy = new PlayerData { name = "旧存档", level = 5 };

            var legacyBytes = new JsonSerializer().Serialize(legacy, typeof(PlayerData));
            var restored = (PlayerData)new NewtonsoftSerializer().Deserialize(legacyBytes, typeof(PlayerData));

            Assert.AreEqual("旧存档", restored.name);
            Assert.AreEqual(5, restored.level);
        }

        [Test]
        public void LegacyFullSnapshot_LoadedViaDefault()
        {
            // 模拟旧存档文件：JsonUtility 序列化整个 DataSnapshot 容器。
            // 注意：JsonUtility 无法表达 null 字符串，null 字段序列化为 ""，
            // 故旧档 saveType 实际为空串而非缺失；恢复逻辑按 IsNullOrEmpty 判定走回退语义。
            var snapshot = new DataSnapshot { version = 1, defaultFormat = "json" };
            snapshot.blocks.Add(new DataBlockSnapshot
            {
                blockName = "Bag",
                data = Convert.ToBase64String(new JsonSerializer().Serialize(new PlayerData { name = "旧数据", level = 3 }, typeof(PlayerData))),
            });

            var legacyFile = new JsonSerializer().Serialize(snapshot, snapshot.GetType());
            var loaded = (DataSnapshot)Serializer.Default.Deserialize(legacyFile, typeof(DataSnapshot));

            Assert.AreEqual(1, loaded.version);
            Assert.AreEqual("json", loaded.defaultFormat);
            Assert.AreEqual(1, loaded.blocks.Count);
            Assert.AreEqual("Bag", loaded.blocks[0].blockName);
            Assert.IsTrue(string.IsNullOrEmpty(loaded.blocks[0].saveType), "旧档 saveType 为空串/缺失，恢复时应走回退逻辑");
            Assert.IsNotNull(loaded.blocks[0].data);
        }

        [Test]
        public void Deserialize_EmptyBytes_ReturnsNull()
        {
            Assert.IsNull(new NewtonsoftSerializer().Deserialize(Array.Empty<byte>(), typeof(PlayerData)));
        }
    }
}
