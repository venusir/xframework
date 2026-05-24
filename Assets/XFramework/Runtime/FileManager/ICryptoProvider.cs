namespace XFramework.XFileManager
{
    /// <summary>
    /// 加解密提供者接口。为文件数据提供可插拔的加解密能力。
    /// <para>设置给 <see cref="FileManager"/> 后，所有读写操作将自动经由此接口加解密。</para>
    /// <para>默认不设置（<c>null</c>），即无加解密开销。内置轻量实现：<see cref="XorCryptoProvider"/>。</para>
    /// </summary>
    public interface ICryptoProvider
    {
        /// <summary>
        /// 加密明文数据。
        /// </summary>
        /// <param name="plainData">原始明文字节数组。</param>
        /// <returns>加密后的密文字节数组。</returns>
        byte[] Encrypt(byte[] plainData);

        /// <summary>
        /// 解密密文数据。
        /// </summary>
        /// <param name="cipherData">密文字节数组。</param>
        /// <returns>解密后的明文字节数组。如果解密失败应返回 <c>null</c>。</returns>
        byte[] Decrypt(byte[] cipherData);
    }
}