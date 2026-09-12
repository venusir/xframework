using XFramework.XFileManager;

namespace XFramework.XSave
{
    /// <summary>
    /// 存档模块初始化选项。
    /// <para>经 <see cref="SaveManager.Initialize(SaveManagerFactory, SaveOptions)"/> 传入，
    /// 或由节点树在挂载 <c>SaveBootstrapNode</c> 时经 <c>AddNode&lt;SaveBootstrapNode&gt;(options)</c> 传入。</para>
    /// </summary>
    public sealed class SaveOptions
    {
        /// <summary>
        /// 当前客户端支持的存档格式版本上限，默认 <c>1</c>。
        /// <para>保存时写入快照的 <see cref="XData.DataSnapshot.version"/>；
        /// 加载时<b>高于</b>该值的存档会被整份拒绝（见 <see cref="SaveLoadStatus.VersionTooNew"/>），
        /// <b>低于</b>该值的存档正常加载并按逐块迁移链升级（见 <see cref="SaveLoadStatus.Migrated"/>）。</para>
        /// <para>游戏每次发布新存档格式时递增此值，即可防止旧客户端把新格式存档「半加载」
        /// （数据块各自跳过，结果一半是新数据一半是空的）。</para>
        /// </summary>
        public int CurrentVersion = 1;

        /// <summary>
        /// 存档加密实现；为 <c>null</c> 时存档为明文。
        /// <para>非空时由 <see cref="SaveManager.Initialize"/> 经
        /// <see cref="FileManager.SetCryptoProvider"/> 接线，且<b>只作用于
        /// <see cref="FileDomain.SaveData"/> 域</b>——不会连带加密 AppData / Cache。</para>
        /// <para><b>陷阱一：切换加密状态会让已有存档立即不可读。</b><see cref="XorCryptoProvider"/> 这类
        /// 对称实现面对明文同样会「解密成功」而不抛异常，只是产出垃圾字节，最终表现为
        /// 「存档已损坏」。开启或更换密钥前必须先迁移存量存档，不能直接切换。</para>
        /// <para><b>陷阱二：XOR 不是可靠加密。</b>框架自带的 <see cref="XorCryptoProvider"/> 只做混淆，
        /// 能挡住随手改存档，挡不住有心人。防篡改应换成实现 <see cref="ICryptoProvider"/> 的
        /// 真正算法（AES 等）。</para>
        /// <para>加密不会削弱其他保障：<see cref="XFileManager.CryptoFileProvider"/> 本身实现了
        /// <c>IAtomicFileProvider</c> 与 <c>IDirectoryProvider</c>，原子写与目录枚举照常可用。</para>
        /// </summary>
        public ICryptoProvider CryptoProvider;
    }
}
