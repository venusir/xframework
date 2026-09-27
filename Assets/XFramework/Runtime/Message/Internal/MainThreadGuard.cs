using System;

namespace XFramework.XMessage.Internal
{
    /// <summary>
    /// 发布/订阅入口的主线程契约断言(仅 Editor 编译;Release 构建零开销)。
    /// <para><b>为什么需要它:</b>消息总线的通道表、过滤器管道缓存与缓冲流都按「主线程使用」设计
    /// (见模块 README「线程」段),但入口处此前没有任何机制拦住跨线程调用——文档说一套,而
    /// <c>PublishAsync</c> 的旧文案还暗示「不在主线程调用也行,只要自行切回主线程」。本断言把这条契约
    /// 变成开发期可见的事实。</para>
    /// <para><b>覆盖边界(三处不覆盖,均属有意):</b>① <b>退订路径</b>——令牌可在任意线程被取消
    /// (<c>CancellationToken.Register</c> 的回调跑在取消者线程上),引擎对该场景只有「订阅链表不被写坏」
    /// 的容忍度,不是许可;② 4 个 <c>SubscribeAsync</c> 重载在令牌已取消时的早退路径——它不触碰任何
    /// 共享状态,故不判;③ 处理器内部的线程行为——断言只管入口,不管用户代码自己怎么切线程。</para>
    /// <para><b>只报首错:</b>越线程调用通常每帧重复发生,记一处即可防洪泛。闩锁由调用方按 broker 实例
    /// 持有,故 <c>MessageManager.Clear()</c> 换新实例后重新具备提示能力。</para>
    /// </summary>
    internal static class MainThreadGuard
    {
        /// <summary>
        /// 断言当前位于 Unity 主线程;越线程时记一条 <c>[Message]</c> 前缀的 Error,并置位
        /// <paramref name="logged"/> 以免重复报告。
        /// <para>主线程检测复用 UniTask 的 <see cref="Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread"/>
        /// (mainThreadId 由 UniTask 在域加载/运行时初始化时于主线程固化),全限定调用与
        /// <c>PipelineStageContext</c>、<c>Pipeline</c> 的既有风格一致。</para>
        /// <para>仅 <c>#if UNITY_EDITOR</c> 编译:Release 下本方法是空操作,调用方因此不必再各自包一层
        /// 条件编译(参数在 Release 下未被使用,C# 不对未使用的参数告警)。</para>
        /// </summary>
        /// <param name="logged">首错闩锁;用 <c>ref</c> 传入调用方的实例字段,测试可直接用局部变量驱动。</param>
        /// <param name="api">被调用的入口名,用于拼装报错文案。</param>
        internal static void AssertEntry(ref bool logged, string api)
        {
#if UNITY_EDITOR
            if (logged || Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
                return;

            logged = true;
            UnityEngine.Debug.LogError(
                $"[Message] {api} 必须在 Unity 主线程调用:通道表、过滤器缓存与缓冲流都按主线程使用设计," +
                "跨线程调用会并发改写这些结构。从非主线程发消息请先切回主线程;" +
                "线程契约见模块 README「线程」段。本提示每个 broker 生命周期只报一次。");
#endif
        }
    }
}
