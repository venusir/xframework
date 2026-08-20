using System;
using System.Threading;

namespace XFramework.XReactive.Internal
{
    /// <summary>
    /// 将任意 Action 包装为 IDisposable 的轻量实现。
    /// <para>替代 R3.Disposable.Create 的自研实现(移除 R3 依赖计划 Phase 1),供 R3.Disposable.Create 的迁移使用。</para>
    /// </summary>
    /// <remarks>
    /// 幂等:Dispose 只执行一次,重复调用被忽略(Interlocked.Exchange 置空后判断)。
    /// </remarks>
    internal sealed class AnonymousDisposable : IDisposable
    {
        private Action _dispose;

        private AnonymousDisposable(Action dispose)
        {
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        }

        /// <summary>创建包装指定 Action 的 IDisposable。</summary>
        public static IDisposable Create(Action dispose) => new AnonymousDisposable(dispose);

        /// <summary>执行一次释放动作,重复调用忽略。</summary>
        public void Dispose()
        {
            var action = Interlocked.Exchange(ref _dispose, null);
            action?.Invoke();
        }
    }
}
