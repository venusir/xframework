using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XSave.Tests;

namespace XFramework.XFileManager.Tests
{
    /// <summary>
    /// <see cref="FileManager"/> 加解密层（<see cref="CryptoFileProvider"/> 装饰器）测试。
    /// <para>覆盖:字节/文本加密往返、磁盘落盘为密文、禁用加密后的行为。</para>
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
        public async Task SetCryptoProvider_Null_DisablesEncryption()
        {
            FileManager.SetCryptoProvider(new XorCryptoProvider("test-key"));
            await FileManager.WriteAllBytesAsync(FileDomain.AppData, "crypto.bin", Encoding.UTF8.GetBytes("plain"));
            FileManager.SetCryptoProvider(null);

            var read = await FileManager.ReadAllBytesAsync(FileDomain.AppData, "crypto.bin");
            Assert.AreEqual("plain", Encoding.UTF8.GetString(read), "禁用加密后应读到磁盘原文（密文无法还原时行为自证）");
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
