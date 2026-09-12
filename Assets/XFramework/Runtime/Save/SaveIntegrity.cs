namespace XFramework.XSave
{
    /// <summary>
    /// 存档完整性校验（FNV-1a 64 位）。
    /// <para><b>定位要诚实：</b>校验和只能发现<b>损坏</b>（截断、位翻转、传输错误），
    /// <b>不提供防篡改</b>——FNV-1a 不是密码学哈希，刻意改动内容并同步改校验和是轻而易举的。
    /// 防篡改需要加密（见 <see cref="XFileManager.FileManager.SetCryptoProvider"/>），
    /// 或替换为真正的摘要算法。这里选它是因为单遍、零分配、无加密库依赖，
    /// 足以覆盖「JSON 仍然合法但字节已被悄悄改坏」这一 JSON 解析本身发现不了的窄场景。</para>
    /// </summary>
    internal static class SaveIntegrity
    {
        #region Constants

        /// <summary>FNV-1a 64 位的偏移基数。</summary>
        private const ulong FnvOffsetBasis = 14695981039346656037UL;

        /// <summary>FNV-1a 64 位的质数。</summary>
        private const ulong FnvPrime = 1099511628211UL;

        #endregion

        #region Public Methods

        /// <summary>
        /// 计算字节内容的 FNV-1a 64 位校验和。
        /// </summary>
        /// <param name="data">待校验的字节内容。</param>
        /// <returns>校验和；<paramref name="data"/> 为 <c>null</c> 时返回 0（0 在侧车中表示「未记录校验和」）。</returns>
        internal static ulong Compute(byte[] data)
        {
            if (data == null)
                return 0;

            var hash = FnvOffsetBasis;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= FnvPrime;
            }

            return hash;
        }

        #endregion
    }
}
