using System.IO;
using UnityEngine;

namespace XFramework.XAsset
{
    /// <summary>
    /// 解密服务的返回产物。与 YooAsset 的同名结构一一对应——字段全是 Unity / BCL 类型，故可以原样镜像。
    /// <para>三个字段表达三种加载形态（同一时刻只有一种有效）：<see cref="Bundle"/>（同步就绪的包）、
    /// <see cref="CreateRequest"/>（异步进行中的包）、<see cref="ManagedStream"/>（托管流）。</para>
    /// </summary>
    public readonly struct AssetDecryptResult
    {
        /// <summary>已解密并就绪的资源包。</summary>
        public readonly AssetBundle Bundle;

        /// <summary>异步解密中的请求句柄（YooAsset 会等待它完成）。</summary>
        public readonly AssetBundleCreateRequest CreateRequest;

        /// <summary>托管流。<b>由 YooAsset 在资源包释放时一并释放</b>——实现方不必也不该自己关它。</summary>
        public readonly Stream ManagedStream;

        /// <summary>构造解密结果。</summary>
        public AssetDecryptResult(AssetBundle bundle = null,
                                  AssetBundleCreateRequest createRequest = null,
                                  Stream managedStream = null)
        {
            Bundle = bundle;
            CreateRequest = createRequest;
            ManagedStream = managedStream;
        }
    }

    /// <summary>
    /// 加密资源包 / 文件的解密服务，经 <see cref="AssetInitOptions.DecryptionServices"/> 交给资源模块。
    /// <para><b>它镜像 YooAsset 的 <c>IDecryptionServices</c></b>（其 <c>DecryptFileInfo</c> 展开成
    /// <c>(bundleName, fileLoadPath, fileLoadCRC)</c> 三个参数）。镜像而不直通 YooAsset 类型，理由与
    /// <see cref="IAssetRemoteServices"/> 相同：公开面不出现第三方类型，第三方接口变更由内部适配器吸收。</para>
    /// <para><b>实现必须与打包侧的加密方式严格对应</b>（密文格式、偏移、校验、清单是否加密）。只加密资源包、
    /// 不加密清单与原生文件的项目，未用到的成员可以 <c>throw new NotSupportedException()</c>——
    /// YooAsset 只在对应路径上调用它们。</para>
    /// <para><b>调用线程与时机由 YooAsset 决定</b>（同步加载在主线程；异步加载的步骤在它的加载管线里），
    /// 实现须遵循 YooAsset 对同名方法的既有约定。</para>
    /// </summary>
    public interface IAssetDecryptionServices
    {
        /// <summary>同步方式获取解密的资源包。</summary>
        AssetDecryptResult LoadAssetBundle(string bundleName, string fileLoadPath, uint fileLoadCRC);

        /// <summary>异步方式获取解密的资源包。</summary>
        AssetDecryptResult LoadAssetBundleAsync(string bundleName, string fileLoadPath, uint fileLoadCRC);

        /// <summary>
        /// 后备方式获取解密的资源包——**正常解密方法失败后**触发（YooAsset 的兜底机制），
        /// 建议用内存加载（<c>AssetBundle.LoadFromMemory</c>）作为保底。
        /// </summary>
        AssetDecryptResult LoadAssetBundleFallback(string bundleName, string fileLoadPath, uint fileLoadCRC);

        /// <summary>获取解密的字节数据（清单 / 原生文件）。</summary>
        byte[] ReadFileData(string bundleName, string fileLoadPath, uint fileLoadCRC);

        /// <summary>获取解密的文本数据（清单 / 原生文件）。</summary>
        string ReadFileText(string bundleName, string fileLoadPath, uint fileLoadCRC);
    }
}
