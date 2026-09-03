namespace XFramework.XPipeline
{
    /// <summary>
    /// 相位阶段:在 <see cref="IPipelineStage"/> 之上声明相位号,参与按相位分组的装配编排。
    /// <para>同相位的阶段并行执行,不同相位的阶段按相位号升序串行执行——相位号数值含义由各使用方约定
    /// (框架内置引导相位约定见 Pipeline 模块 README「内置 Phase 约定」)。</para>
    /// <para>经 <see cref="Pipeline.BuildPhaseGroups"/> 装配为每相位一个 <see cref="ParallelStage"/> 后加入管线:
    /// 组内并行、组间由管线串行。本接口不引入第二套执行契约——阶段写面(进度/描述/状态)、契约兜底与
    /// 取消语义完全继承 <see cref="IPipelineStage"/>。</para>
    /// </summary>
    public interface IPhaseStage : IPipelineStage
    {
        /// <summary>相位号。同值阶段并行执行,不同值阶段按值升序串行执行。</summary>
        int Phase { get; }
    }
}
