using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using XFramework.XAsset;
using YooAsset;

namespace XFramework.XAsset.Tests
{
    /// <summary>
    /// 初始化参数映射与解密服务适配：<c>CreatePlayModeParameters</c> 的产物，以及框架
    /// <see cref="IAssetDecryptionServices"/> → YooAsset <c>IDecryptionServices</c> 的纯转发。
    /// <para><b>为什么值得单测</b>：这条链路此前零覆盖——Asset 的既有用例全走假实现（<c>FakeAssetManager</c>），
    /// 真实 YooAsset 初始化路径从未被执行过，于是「离线模式的 <c>BuildinFileSystemParameters</c> 没接线」
    /// （会让初始化直接失败）与「解密服务没有出口」两件事都无从被测试发现。</para>
    /// </summary>
    class AssetDecryptionServicesTests
    {
        #region Test Doubles

        private sealed class FakeDecryption : IAssetDecryptionServices
        {
            public readonly List<string> Calls = new List<string>();
            public string LastBundleName;
            public string LastFilePath;
            public uint LastCrc;

            private void Record(string call, string bundleName, string fileLoadPath, uint fileLoadCRC)
            {
                Calls.Add(call);
                LastBundleName = bundleName;
                LastFilePath = fileLoadPath;
                LastCrc = fileLoadCRC;
            }

            public AssetDecryptResult LoadAssetBundle(string bundleName, string fileLoadPath, uint fileLoadCRC)
            {
                Record(nameof(LoadAssetBundle), bundleName, fileLoadPath, fileLoadCRC);
                return default;
            }

            public AssetDecryptResult LoadAssetBundleAsync(string bundleName, string fileLoadPath, uint fileLoadCRC)
            {
                Record(nameof(LoadAssetBundleAsync), bundleName, fileLoadPath, fileLoadCRC);
                return default;
            }

            public AssetDecryptResult LoadAssetBundleFallback(string bundleName, string fileLoadPath, uint fileLoadCRC)
            {
                Record(nameof(LoadAssetBundleFallback), bundleName, fileLoadPath, fileLoadCRC);
                return default;
            }

            public byte[] ReadFileData(string bundleName, string fileLoadPath, uint fileLoadCRC)
            {
                Record(nameof(ReadFileData), bundleName, fileLoadPath, fileLoadCRC);
                return new byte[] { 1, 2, 3 };
            }

            public string ReadFileText(string bundleName, string fileLoadPath, uint fileLoadCRC)
            {
                Record(nameof(ReadFileText), bundleName, fileLoadPath, fileLoadCRC);
                return "decrypted-text";
            }
        }

        private sealed class FakeRemote : IAssetRemoteServices
        {
            public string GetRemoteMainURL(string fileName) => "https://main/" + fileName;

            public string GetRemoteFallbackURL(string fileName) => "https://fallback/" + fileName;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 从 YooAsset 的 <see cref="FileSystemParameters"/> 里读回解密服务——<c>CreateParameters</c> 是
        /// YooAsset 程序集 internal 的字段，反射是唯一读法（找不到即它改名了，请同步本 fixture）。
        /// </summary>
        private static IDecryptionServices GetDecryptionService(FileSystemParameters parameters)
        {
            var field = typeof(FileSystemParameters).GetField("CreateParameters",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "找不到 CreateParameters 字段——YooAsset 改名了？请同步本 fixture");

            var dictionary = (Dictionary<string, object>)field.GetValue(parameters);
            return dictionary != null && dictionary.TryGetValue("DECRYPTION_SERVICES", out var value)
                ? value as IDecryptionServices
                : null;
        }

        #endregion

        #region 初始化参数映射

        [Test]
        public void CreatePlayModeParameters_Offline_WiresBuildinFileSystem()
        {
            var parameters = (OfflinePlayModeParameters)YooAssetManagerImpl.CreatePlayModeParameters(
                new AssetInitOptions { PlayMode = AssetPlayMode.Offline });

            // 红基线：修复前这里是裸的 new OfflinePlayModeParameters()，BuildinFileSystemParameters 为 null，
            // 而 YooAsset 的 InitializationOperation 遇到空参数列表直接判失败
            // （"The file system parameters is empty !"）——因为默认 PlayMode 就是 Offline，这条一直没被发现。
            Assert.IsNotNull(parameters.BuildinFileSystemParameters,
                "离线模式必须显式给出内置文件系统参数——留空不是「用默认」，是 YooAsset 初始化直接失败");
        }

        [Test]
        public void CreatePlayModeParameters_Host_WiresBothFileSystems()
        {
            var parameters = (HostPlayModeParameters)YooAssetManagerImpl.CreatePlayModeParameters(
                new AssetInitOptions { PlayMode = AssetPlayMode.Host, RemoteServices = new FakeRemote() });

            Assert.IsNotNull(parameters.BuildinFileSystemParameters, "Host 的内置文件系统不能为空");
            Assert.IsNotNull(parameters.CacheFileSystemParameters, "Host 的缓存文件系统不能为空");
        }

        [Test]
        public void CreatePlayModeParameters_WithDecryption_WiresAdapterIntoBothFileSystems()
        {
            var fake = new FakeDecryption();
            var options = new AssetInitOptions
            {
                PlayMode = AssetPlayMode.Host,
                RemoteServices = new FakeRemote(),
                DecryptionServices = fake,
            };

            var parameters = (HostPlayModeParameters)YooAssetManagerImpl.CreatePlayModeParameters(options);

            var buildin = GetDecryptionService(parameters.BuildinFileSystemParameters);
            var cache = GetDecryptionService(parameters.CacheFileSystemParameters);

            Assert.IsNotNull(buildin, "解密服务必须真的接进 YooAsset 的参数——这是「能给 bundle 加密」的全部意义");
            Assert.AreSame(buildin, cache, "两个文件系统应共用同一个适配器");
        }

        [Test]
        public void CreatePlayModeParameters_WithoutDecryption_LeavesItUnset()
        {
            var parameters = (OfflinePlayModeParameters)YooAssetManagerImpl.CreatePlayModeParameters(
                new AssetInitOptions { PlayMode = AssetPlayMode.Offline });

            Assert.IsNull(GetDecryptionService(parameters.BuildinFileSystemParameters),
                "不传解密服务时不该凭空造一个——默认行为必须是不解密");
        }

        #endregion

        #region 适配器转发

        [Test]
        public void Adapter_ForwardsEveryMember()
        {
            var fake = new FakeDecryption();
            var adapter = new YooAssetManagerImpl.YooAssetDecryptionServicesAdapter(fake);
            var fileInfo = new DecryptFileInfo
            {
                BundleName = "prefabs.bundle",
                FileLoadPath = "/data/prefabs.bundle",
                FileLoadCRC = 42u,
            };

            adapter.LoadAssetBundle(fileInfo);
            adapter.LoadAssetBundleAsync(fileInfo);
            adapter.LoadAssetBundleFallback(fileInfo);
            var data = adapter.ReadFileData(fileInfo);
            var text = adapter.ReadFileText(fileInfo);

            CollectionAssert.AreEqual(
                new[] { "LoadAssetBundle", "LoadAssetBundleAsync", "LoadAssetBundleFallback", "ReadFileData", "ReadFileText" },
                fake.Calls, "五个成员都必须转发到框架接口——漏一个就是某条路径静默不解密");
            Assert.AreEqual("prefabs.bundle", fake.LastBundleName, "DecryptFileInfo 的三个字段要原样展开");
            Assert.AreEqual("/data/prefabs.bundle", fake.LastFilePath);
            Assert.AreEqual(42u, fake.LastCrc);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, data, "返回值要原样带回");
            Assert.AreEqual("decrypted-text", text);
        }

        #endregion
    }
}
