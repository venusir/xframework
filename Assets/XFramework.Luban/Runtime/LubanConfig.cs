using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework.XConfig.Luban
{
    /// <summary>
    /// Luban 配置静态门面：异步加载数据、构造整包 <c>Tables</c>，并把表注册进
    /// <see cref="ConfigManager"/>。
    /// <para>与核心门面一致：未初始化访问抛 <see cref="InvalidOperationException"/>，
    /// 重复初始化告警并忽略，会话复位在 <c>SubsystemRegistration</c> 档。</para>
    /// <para>所有成员必须在主线程调用。</para>
    /// </summary>
    public static class LubanConfig
    {
        #region Private Fields

        private static LubanConfigImpl _impl;

        #endregion

        #region Properties

        /// <summary>
        /// 是否已成功初始化。
        /// </summary>
        public static bool IsInitialized => _impl != null && _impl.IsInitialized;

        /// <summary>
        /// 已构造的整包 <c>Tables</c> 实例（保持为 <see cref="object"/>，包不引用 Luban 程序集）。
        /// <para>未初始化时抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        public static object Tables => EnsureInitialized().Tables;

        #endregion

        #region Lifecycle

        /// <summary>
        /// 加载 Luban 数据、构造 <c>Tables</c> 并注册进 <see cref="ConfigManager"/>。
        /// <para>ConfigManager 未初始化时会自动调用一次 <see cref="ConfigManager.Initialize"/>，
        /// 但<b>永不</b>销毁它。</para>
        /// <para>并发调用共享同一次初始化：首个调用者的 <paramref name="options"/> /
        /// <paramref name="progress"/> / <paramref name="cancellationToken"/> 生效，其余调用等待同一结果。
        /// <see cref="Destroy"/> 或会话复位会以 <see cref="OperationCanceledException"/> 作废在途初始化，
        /// 且不会注册任何表。</para>
        /// </summary>
        /// <param name="adapter">生成的强类型适配器。</param>
        /// <param name="options">加载选项；null 时使用默认值。</param>
        /// <param name="progress">进度回调，可为 null。</param>
        /// <param name="cancellationToken">取消令牌；取消只中断本次等待，已完成的加载/注册不回滚。</param>
        /// <exception cref="ArgumentNullException"><paramref name="adapter"/> 为 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="options"/> 的 DataRoot 为空且未提供 LocationResolver。</exception>
        /// <exception cref="OperationCanceledException">调用方取消，或初始化被 Destroy/会话复位作废。</exception>
        /// <exception cref="LubanConfigException">加载、构造或注册失败。</exception>
        public static UniTask InitializeAsync(
            ILubanConfigAdapter adapter,
            LubanOptions options = null,
            IProgress<LubanInitReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));

            _impl ??= new LubanConfigImpl(new AssetLubanDataProvider());
            return _impl.InitializeAsync(adapter, options, progress, cancellationToken);
        }

        /// <summary>
        /// 丢弃门面状态并卸载本扩展注册过的表。
        /// <para>若 <see cref="ConfigManager"/> 已被销毁，则跳过卸载并记 Warning。
        /// <b>不会</b>销毁 ConfigManager（所有权始终归使用方）。</para>
        /// </summary>
        public static void Destroy()
        {
            _impl?.Destroy();
            _impl = null;
        }

        #endregion

        #region Query

        /// <summary>
        /// 取强类型 <c>Tables</c> 实例（例如 <c>cfg.Tables</c>）。
        /// </summary>
        /// <typeparam name="T">生成的 <c>Tables</c> 类型。</typeparam>
        /// <exception cref="InvalidOperationException">未初始化，或实际类型与 <typeparamref name="T"/> 不符。</exception>
        public static T GetTables<T>() where T : class
        {
            return EnsureInitialized().GetTables<T>();
        }

        /// <summary>
        /// 安全取强类型 <c>Tables</c> 实例；未初始化或类型不符时返回 <c>false</c>。
        /// </summary>
        /// <typeparam name="T">生成的 <c>Tables</c> 类型。</typeparam>
        /// <param name="tables">取到的实例；失败时为 null。</param>
        public static bool TryGetTables<T>(out T tables) where T : class
        {
            if (_impl != null)
            {
                return _impl.TryGetTables(out tables);
            }

            tables = null;
            return false;
        }

        #endregion

        #region Internal

        /// <summary>
        /// 测试专用：注入 fake 数据后端。不改变公开门面的生产路径。
        /// </summary>
        internal static UniTask InitializeForTestsAsync(
            ILubanConfigAdapter adapter,
            ILubanDataProvider provider,
            LubanOptions options = null,
            IProgress<LubanInitReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            if (_impl == null || (!_impl.IsInitialized && !_impl.IsInitializing))
            {
                _impl = new LubanConfigImpl(provider);
            }

            return _impl.InitializeAsync(adapter, options, progress, cancellationToken);
        }

        /// <summary>
        /// 关闭域重载会话复位：只丢引用，不卸载（ConfigManager 自己也会复位）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForNewSession()
        {
            _impl?.ResetForNewSession();
            _impl = null;
        }

        private static LubanConfigImpl EnsureInitialized()
        {
            if (_impl == null || !_impl.IsInitialized)
            {
                throw new InvalidOperationException(
                    "[LubanConfig] LubanConfig 尚未初始化。请先调用 " +
                    "LubanConfig.InitializeAsync(adapter) 或登记 LubanBootstrapStage。");
            }

            return _impl;
        }

        #endregion
    }
}
