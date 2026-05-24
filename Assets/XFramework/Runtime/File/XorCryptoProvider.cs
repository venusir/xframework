using System;
using System.Text;

namespace XFramework.XFileManager
{
    /// <summary>
    /// 基于 XOR 的轻量加解密实现。零外部依赖，适合存档防篡改场景。
    /// <para>注意：XOR 加密安全性较低，仅用于防普通用户篡改。如需强加密请实现 <see cref="ICryptoProvider"/> 并使用 AES 等算法。</para>
    /// <para>使用固定密钥进行逐字节异或。可通过 <see cref="XorCryptoProvider(string)"/> 传入自定义密钥。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 使用默认密钥
    /// FileManager.SetCryptoProvider(new XorCryptoProvider());
    ///
    /// // 使用自定义密钥
    /// FileManager.SetCryptoProvider(new XorCryptoProvider("my-secret-key-2024"));
    /// </code>
    /// </example>
    public class XorCryptoProvider : ICryptoProvider
    {
        #region Private Fields

        private readonly byte[] _keyBytes;

        #endregion

        #region Constructors

        /// <summary>
        /// 使用默认密钥创建 XOR 加解密提供者。
        /// </summary>
        public XorCryptoProvider() : this("XFramework.DefaultXorKey.v1")
        {
        }

        /// <summary>
        /// 使用自定义密钥字符串创建 XOR 加解密提供者。
        /// </summary>
        /// <param name="key">加密密钥字符串。密钥越长越安全。</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException"><paramref name="key"/> 为空字符串。</exception>
        public XorCryptoProvider(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Length == 0)
                throw new ArgumentException("密钥不能为空字符串。", nameof(key));

            _keyBytes = Encoding.UTF8.GetBytes(key);
        }

        #endregion

        #region ICryptoProvider

        /// <inheritdoc />
        public byte[] Encrypt(byte[] plainData)
        {
            if (plainData == null || plainData.Length == 0)
                return plainData ?? Array.Empty<byte>();

            var result = new byte[plainData.Length];
            XorTransform(plainData, result);
            return result;
        }

        /// <inheritdoc />
        public byte[] Decrypt(byte[] cipherData)
        {
            // XOR 加解密是对称的，逻辑相同
            return Encrypt(cipherData);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 对输入数据执行 XOR 变换，写入输出数组。
        /// </summary>
        private void XorTransform(byte[] input, byte[] output)
        {
            int keyLen = _keyBytes.Length;
            for (int i = 0; i < input.Length; i++)
            {
                output[i] = (byte)(input[i] ^ _keyBytes[i % keyLen]);
            }
        }

        #endregion
    }
}