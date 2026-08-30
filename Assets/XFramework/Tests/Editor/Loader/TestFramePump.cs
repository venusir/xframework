using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 手动帧泵：以确定性方式驱动 Loader 的每帧轮询（EditMode 测试环境无 PlayerLoop 泵，阻塞式断言依赖此泵推进）。
    /// <para>注入方式：装配 Loader 时设置 <c>_framePump = pump.Next</c>；每调用一次 <see cref="Step"/> 推进一帧。</para>
    /// <para>本包版本 <see cref="UniTaskCompletionSource"/> 续体默认内联执行——TrySetResult 时同步续体，保证单次 Step 内轮询迭代完成。</para>
    /// </summary>
    internal sealed class TestFramePump
    {
        private readonly Queue<UniTaskCompletionSource> _queue = new Queue<UniTaskCompletionSource>();

        /// <summary>帧泵函数：Loader 每轮询一帧后 await 一次。</summary>
        public UniTask Next()
        {
            var tcs = new UniTaskCompletionSource();
            _queue.Enqueue(tcs);
            return tcs.Task;
        }

        /// <summary>推进一帧：放行最早挂起的轮询。</summary>
        public void Step()
        {
            if (_queue.Count == 0) return;
            _queue.Dequeue().TrySetResult();
        }

        /// <summary>是否仍有挂起的帧（轮询仍在等待，即加载尚未收敛）。</summary>
        public bool HasPending => _queue.Count > 0;
    }
}
