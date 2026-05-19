using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XLoader;

namespace XFramework.XCore
{
    /// <summary>
    /// 非节点树模块的统一初始化契约。
    /// <para>所有不挂载到节点树上、但需要在启动阶段统一初始化的非节点模块（如 AssetManager、
    /// LockManager、MessageManager 等），应实现此接口并通过 <see cref="BootstrapNode"/> 注册。</para>
    /// <para>接口本身继承 <see cref="IDisposable"/>，实现类应在 Dispose() 中完成资源释放和状态重置。</para>
    /// </summary>
    public interface IModuleService : IDisposable
    {
        /// <summary>
        /// 模块是否已完成初始化。
        /// <para><see cref="BootstrapNode"/> 会检查此属性，已初始化的模块将被跳过。</para>
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 异步初始化此模块。
        /// <para>初始化过程中应通过 <paramref name="progress"/> 报告进度和描述信息。</para>
        /// <para>支持通过 <paramref name="cancellationToken"/> 取消正在进行的初始化流程。</para>
        /// </summary>
        /// <param name="progress">进度报告回调，用于向 <see cref="BootstrapNode"/> 统一上报启动进度。</param>
        /// <param name="cancellationToken">用于取消初始化流程的 Token。</param>
        UniTask InitializeAsync(IProgress<LoadContext> progress, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }
    }
}