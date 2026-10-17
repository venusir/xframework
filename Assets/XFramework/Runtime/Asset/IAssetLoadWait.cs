using YooAsset;

namespace XFramework.XAsset
{
    /// <summary>
    /// 一次资产加载等待的最小面（internal 测试缝）：轮询完成态 + 释放底层句柄。
    /// <para><b>为什么需要缝</b>：取消善后（AST-001）必须有一条「改前必红」的用例，而 YooAsset 的
    /// <see cref="HandleBase"/> 构造函数是 internal、替身造不出来——与 <c>ISceneLoadSource</c> 抽出时
    /// 是同一条理由。缝之上是「取消 → 释放 → 重抛」，缝之下（<see cref="HandleBaseWait"/>）只是机械转发。</para>
    /// </summary>
    internal interface IAssetLoadWait
    {
        /// <summary>加载操作是否已完成。</summary>
        bool IsDone { get; }

        /// <summary>释放底层句柄。对已释放/已销毁的句柄安全（YooAsset 的 <c>Release</c> 会提前返回）。</summary>
        void Release();
    }

    /// <summary><see cref="IAssetLoadWait"/> 的生产实现：包住一个 YooAsset 句柄。</summary>
    internal readonly struct HandleBaseWait : IAssetLoadWait
    {
        private readonly HandleBase _handle;

        /// <summary>包装一个 YooAsset 句柄。</summary>
        /// <param name="handle">加载操作返回的句柄；不可为 null。</param>
        internal HandleBaseWait(HandleBase handle) => _handle = handle;

        /// <inheritdoc />
        public bool IsDone => _handle.IsDone;

        /// <inheritdoc />
        public void Release() => _handle.Release();
    }
}
