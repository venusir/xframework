using System.Threading;
using Cysharp.Threading.Tasks;

namespace XFramework.XSettings
{
    /// <summary>
    /// 可选能力接口：为设置存储后端提供真正的异步读写能力。
    /// <para>实现本接口的存储后端（云存档、平台 SDK 等）可让读写完全离开调用线程。未实现的
    /// 存储后端<b>不必改动</b>——管理器会做能力探测，把同步调用整体挪到线程池，因此对调用方而言
    /// <c>SaveAsync</c>/<c>LoadAsync</c> 同样是非阻塞的。</para>
    /// <para><b>与同步成员的关系：</b>继承自 <see cref="ISettingsStore"/> 的同步成员仍需正确实现，
    /// 它们由 <see cref="SettingsManager"/> 的同步 API 使用。</para>
    /// </summary>
    public interface IAsyncSettingsStore : ISettingsStore
    {
        /// <summary>
        /// 异步检查持久层是否存在已保存的数据。
        /// <para>必须提供：管理器靠它区分「有持久化数据」与「根本没有数据」，从而在无数据时
        /// 使用 <c>defaultFactory</c> 而非 <c>new T()</c>。缺了它，异步加载会与同步加载
        /// 产生不同的默认值——那正是 <see cref="ISettingsManager{T}.Load"/> 上修过的缺陷。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask<bool> ExistsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步从持久层加载设置对象。
        /// <para>如果持久层不存在数据，应返回 <c>new T()</c>；返回 <c>null</c> 会被管理器
        /// 回退为默认值并打 LogWarning，但不应依赖这一兜底。</para>
        /// </summary>
        /// <typeparam name="T">设置对象类型。必须满足 <c>class, new()</c> 约束。</typeparam>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask<T> LoadAsync<T>(CancellationToken cancellationToken = default) where T : class, new();

        /// <summary>
        /// 异步将设置对象保存到持久层。
        /// </summary>
        /// <typeparam name="T">设置对象类型。</typeparam>
        /// <param name="settings">要保存的设置对象。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask SaveAsync<T>(T settings, CancellationToken cancellationToken = default) where T : class, new();
    }
}
