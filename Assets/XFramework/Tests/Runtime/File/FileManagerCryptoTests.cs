using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XSave.Tests;

namespace XFramework.XFileManager.Tests
{
    /// <summary>
    /// <see cref="FileManager"/> 加解密层（<see cref="CryptoFileProvider"/> 装饰器）测试。
    /// <para>覆盖:字节/文本加密往返、磁盘落盘为密文、按域限定作用域、禁用加密后的行为。</para>
    /// </summary>
    [TestFixture]
    public class FileManagerCryptoTests
    {
        private TempFileProvider _fileProvider;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _fileProvider?.Cleanup();
        }

        [SetUp]
        public void SetUp()
        {
            _fileProvider?.Cleanup();
            _fileProvider = new TempFileProvider();

            FileManager.Destroy();
            FileManager.Initialize(_fileProvider);
        }

        [TearDown]
        public void TearDown()
        {
            FileManager.Destroy();

            // 还原成「零配置可用」，而不是把销毁闩锁留给后续 fixture——懒加载豁免只对「未初始化」生效，
            // 已销毁时 EnsureInitialized 直接抛，同进程混跑两个平台会让后面的 EditMode 用例吃到它
            FileManager.Initialize();
        }

        [Test]
        public async Task CryptoRoundTrip_Bytes_ReadBackDecrypted()
        {
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"));
            try
            {
                var payload = Encoding.UTF8.GetBytes("secret data");
                await FileManager.WriteAllBytesAsync(FileDomain.AppData, "crypto.bin", payload);

                var read = await FileManager.ReadAllBytesAsync(FileDomain.AppData, "crypto.bin");
                Assert.AreEqual("secret data", Encoding.UTF8.GetString(read), "读取应还原明文");

                // 磁盘上应是密文（绕过门面直读底层 Provider）
                var raw = await _fileProvider.ReadAllBytesAsync(FileDomain.AppData, "crypto.bin");
                Assert.AreNotEqual("secret data", Encoding.UTF8.GetString(raw), "磁盘内容应为密文而非明文");
            }
            finally
            {
                FileManager.SetCryptoProvider(null);
            }
        }

        [Test]
        public async Task CryptoRoundTrip_Text_ReadBackPlain()
        {
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"));
            try
            {
                await FileManager.WriteAllTextAsync(FileDomain.AppData, "crypto.txt", "你好 XFramework");

                var read = await FileManager.ReadAllTextAsync(FileDomain.AppData, "crypto.txt");
                Assert.AreEqual("你好 XFramework", read, "中文文本加密往返应无损");
            }
            finally
            {
                FileManager.SetCryptoProvider(null);
            }
        }

        [Test]
        public async Task SetCryptoProvider_WithDomain_EncryptsOnlyTargetDomain()
        {
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"), FileDomain.SaveData);
            try
            {
                await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "save.bin", Encoding.UTF8.GetBytes("save-secret"));
                await FileManager.WriteAllBytesAsync(FileDomain.AppData, "app.bin", Encoding.UTF8.GetBytes("app-plain"));

                // 目标域：磁盘应为密文
                var saveRaw = await _fileProvider.ReadAllBytesAsync(FileDomain.SaveData, "save.bin");
                Assert.AreNotEqual("save-secret", Encoding.UTF8.GetString(saveRaw), "限定的目标域落盘应为密文");

                // 非目标域：磁盘应保持明文——这正是限定作用域要解决的问题
                var appRaw = await _fileProvider.ReadAllBytesAsync(FileDomain.AppData, "app.bin");
                Assert.AreEqual("app-plain", Encoding.UTF8.GetString(appRaw), "非目标域不应被连带加密");

                // 两个域经门面读回都应还原原始内容
                var saveRead = await FileManager.ReadAllBytesAsync(FileDomain.SaveData, "save.bin");
                var appRead = await FileManager.ReadAllBytesAsync(FileDomain.AppData, "app.bin");
                Assert.AreEqual("save-secret", Encoding.UTF8.GetString(saveRead), "目标域应正确解密");
                Assert.AreEqual("app-plain", Encoding.UTF8.GetString(appRead), "非目标域应原样读回");
            }
            finally
            {
                FileManager.SetCryptoProvider(null);
            }
        }

        [Test]
        public async Task SetCryptoProvider_WithoutDomain_EncryptsAllDomains()
        {
            // 单参重载的历史语义 = 全域加密，新增可选参数不得改变它
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"));
            try
            {
                await FileManager.WriteAllBytesAsync(FileDomain.SaveData, "save.bin", Encoding.UTF8.GetBytes("save-secret"));
                await FileManager.WriteAllBytesAsync(FileDomain.AppData, "app.bin", Encoding.UTF8.GetBytes("app-secret"));

                var saveRaw = await _fileProvider.ReadAllBytesAsync(FileDomain.SaveData, "save.bin");
                var appRaw = await _fileProvider.ReadAllBytesAsync(FileDomain.AppData, "app.bin");
                Assert.AreNotEqual("save-secret", Encoding.UTF8.GetString(saveRaw), "不限定域时应加密 SaveData");
                Assert.AreNotEqual("app-secret", Encoding.UTF8.GetString(appRaw), "不限定域时应加密 AppData");
            }
            finally
            {
                FileManager.SetCryptoProvider(null);
            }
        }

        [Test]
        public async Task SetCryptoProvider_CalledAgain_ReplacesScope()
        {
            // 重复调用整体替换上一次配置：先全域，再收窄到 SaveData 后 AppData 应不再加密
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"));
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"), FileDomain.SaveData);
            try
            {
                await FileManager.WriteAllBytesAsync(FileDomain.AppData, "app.bin", Encoding.UTF8.GetBytes("app-plain"));

                var appRaw = await _fileProvider.ReadAllBytesAsync(FileDomain.AppData, "app.bin");
                Assert.AreEqual("app-plain", Encoding.UTF8.GetString(appRaw), "重复设置应替换而非叠加作用域");
            }
            finally
            {
                FileManager.SetCryptoProvider(null);
            }
        }

        [Test]
        public async Task SetCryptoProvider_Null_DisablesEncryption()
        {
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"));
            await FileManager.WriteAllBytesAsync(FileDomain.AppData, "crypto.bin", Encoding.UTF8.GetBytes("plain"));
            FileManager.SetCryptoProvider(null);

            var read = await FileManager.ReadAllBytesAsync(FileDomain.AppData, "crypto.bin");
            // 写入时磁盘已落密文；禁用加密后读取直接返回磁盘原文（不再解密），
            // 读到无法还原的垃圾密文正自证「加密读取已关闭」
            Assert.AreNotEqual("plain", Encoding.UTF8.GetString(read), "禁用加密后应读到磁盘密文原文（不再解密）");
        }

        [Test]
        public async Task SetCryptoProvider_AfterWrite_ReadsRequireSameKey()
        {
            FileManager.SetCryptoProvider(new XorCryptoProvider("key-a"));
            await FileManager.WriteAllBytesAsync(FileDomain.AppData, "crypto.bin", Encoding.UTF8.GetBytes("with-a"));
            FileManager.SetCryptoProvider(new XorCryptoProvider("key-b"));

            var read = await FileManager.ReadAllBytesAsync(FileDomain.AppData, "crypto.bin");
            Assert.AreNotEqual("with-a", Encoding.UTF8.GetString(read), "密钥不匹配时读出的应是被错误解密的垃圾数据（快照语义自证）");
        }
    }
}
