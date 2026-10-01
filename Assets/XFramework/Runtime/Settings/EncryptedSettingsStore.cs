using System;
using System.Text;
using UnityEngine;
using XFramework.XFileManager;
using XFramework.XLog;

namespace XFramework.XSettings
{
    /// <summary>
    /// 加解密装饰器：把设置载荷加密后再交给内层存储后端。
    /// <para><b>定位：</b>与内层后端正交——它不关心数据落在文件、PlayerPrefs 还是云端，
    /// 只负责「交给内层之前先加密、从内层取出之后解密」。因此可以叠在任意
    /// <see cref="ISettingsStore"/> 之上，且内层原有的能力（原子写、一代备份、失败降级）
    /// 全部保留。</para>
    /// <para><b>用途是防篡改而非保密：</b>加密强度的上限取决于注入的 <see cref="ICryptoProvider"/>
    /// （内置的 <see cref="XorCryptoProvider"/> 只防普通用户手改，要强加密请自行实现 AES）。
    /// 密钥随游戏分发，请勿用它保护真正敏感的数据。</para>
    /// <para><b>落盘形状：</b>载荷先被 <c>JsonUtility</c> 序列化、加密，再以 Base64 包进
    /// <see cref="SettingsCipherBlob"/>。多一层壳是 <see cref="ISettingsStore"/> 的形状决定的：
    /// 内层后端只有泛型的 <c>Load/Save</c>、没有字节通道，而 Base64 是任意字节序列能无损放进
    /// JSON 字符串的唯一编码。</para>
    /// <para><b>换密钥等于换加密方案：</b>旧文件将无法还原，读取时按「解密失败」回退默认值并告警。
    /// 需要迁移请自行在换密钥前把数据读出来重新保存。</para>
    /// <para><b>线程：</b>本类自身无可变状态，线程安全与否取决于内层后端。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// var store = new EncryptedSettingsStore(
    ///     new JsonFileStore(Application.persistentDataPath + "/settings.json"),
    ///     new XorCryptoProvider("my-secret-key"));
    ///
    /// SettingsManager.Initialize&lt;GameSettings&gt;(store);
    /// </code>
    /// </example>
    public sealed class EncryptedSettingsStore : ISettingsStore
    {
        #region Private Fields

        private readonly ISettingsStore _inner;
        private readonly ICryptoProvider _crypto;

        #endregion

        #region Constructors

        /// <summary>
        /// 创建加解密装饰器。
        /// </summary>
        /// <param name="inner">被装饰的存储后端。</param>
        /// <param name="cryptoProvider">加解密实现。</param>
        /// <exception cref="ArgumentNullException">任一参数为 <c>null</c> 时抛出。</exception>
        public EncryptedSettingsStore(ISettingsStore inner, ICryptoProvider cryptoProvider)
        {
            // 早失败:缺任一项都要到第一次读写才炸,错误点离病因很远
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _crypto = cryptoProvider ?? throw new ArgumentNullException(nameof(cryptoProvider));
        }

        #endregion

        #region ISettingsStore

        /// <inheritdoc />
        public bool Exists()
        {
            return _inner.Exists();
        }

        /// <inheritdoc />
        public void Delete()
        {
            // 密文与明文是同一份文件,删除逻辑内层说了算
            _inner.Delete();
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> 为 <c>null</c> 时抛出。</exception>
        public void Save<T>(T settings) where T : class, new()
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            // 缩进对密文没有意义,内层看到的只是 Base64,故不生成可读格式
            var json = JsonUtility.ToJson(settings, false);
            var cipher = _crypto.Encrypt(Encoding.UTF8.GetBytes(json));

            _inner.Save(new SettingsCipherBlob { Data = Convert.ToBase64String(cipher) });
        }

        /// <inheritdoc />
        public T Load<T>() where T : class, new()
        {
            var blob = _inner.Load<SettingsCipherBlob>();

            // 内层无数据时按其契约返回 new SettingsCipherBlob(),此时 Data 为 null
            if (blob == null || string.IsNullOrEmpty(blob.Data))
                return new T();

            byte[] plain;
            try
            {
                plain = _crypto.Decrypt(Convert.FromBase64String(blob.Data));
            }
            catch (FormatException ex)
            {
                LogManager.Exception(LogLevel.Warning, LogCategories.SettingsManager, ex,
                    "设置密文不是合法的 Base64，已回退默认值");
                return new T();
            }

            if (plain == null)
            {
                // ICryptoProvider 的契约:解密失败返回 null。最常见的原因是密钥换了
                LogManager.Warning(LogCategories.SettingsManager,
                    "设置密文解密失败（ICryptoProvider.Decrypt 返回 null），已回退默认值。" +
                    "最常见的原因是密钥已更换——换密钥等于换加密方案，旧文件无法还原。");
                return new T();
            }

            var loaded = FromJsonOrDefault<T>(Encoding.UTF8.GetString(plain));
            return loaded ?? new T();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 解密后的明文解析为 <typeparamref name="T"/>，解析失败告警并返回 <c>null</c>。
        /// <para>按本模块的既有取舍：损坏的配置不该让游戏启动失败，一律告警回退。</para>
        /// </summary>
        private static T FromJsonOrDefault<T>(string json) where T : class, new()
        {
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                LogManager.Exception(LogLevel.Warning, LogCategories.SettingsManager, ex,
                    string.Format("解密后的设置内容无法解析为 {0}，已回退默认值", typeof(T).Name));
                return null;
            }
        }

        #endregion
    }

    /// <summary>
    /// <see cref="EncryptedSettingsStore"/> 的落盘外壳：只有一个 Base64 密文字段。
    /// <para>独立成类型而非嵌套私有类，因为它同时是给用户看的落盘契约——排查「设置读不出来」
    /// 时第一眼要看的就是这个形状（与 <see cref="SettingsEnvelope{T}"/> 同一考虑）。</para>
    /// </summary>
    [Serializable]
    internal sealed class SettingsCipherBlob
    {
        /// <summary>Base64 编码的密文。明文是载荷的 JSON。</summary>
        public string Data;
    }
}
