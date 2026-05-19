using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using XFramework.XLoader;

namespace XFramework.XCore
{
    /// <summary>
    /// 引导节点。在启动阶段统一管理所有非节点树的模块（如 AssetManager、LockManager、
    /// MessageManager 等）的初始化，向启动管线报告统一的加载进度。
    /// <para>继承 <see cref="EntityNode"/>，实现 <see cref="ILoadable"/>，
    /// 会被 <see cref="StartupExtensions"/> 自动收集并在加载阶段执行。</para>
    /// <para>启动完成后，BootstrapNode 可被安全销毁，模块保持运行状态。</para>
    /// <para>可通过子类化并重写 <see cref="OnRegisterModules"/> 来自定义启动模块列表和顺序。</para>
    /// </summary>
    public class BootstrapNode : EntityNode, ILoadable
    {
        #region Private Fields

        /// <summary>注册的模块列表，按注册顺序依次初始化。</summary>
        private List<ModuleEntry> _modules;

        /// <summary>所有模块的权重总和。</summary>
        private float _totalWeight;

        #endregion

        #region ILoadable

        /// <summary>
        /// 加载阶段号。设为 0（最低优先级），确保 BootstrapNode 最先被加载。
        /// </summary>
        public int Phase => 0;

        /// <summary>
        /// 按注册顺序依次初始化所有已注册的模块，并向 context 报告统一的加载进度。
        /// <para>已初始化的模块会被自动跳过。</para>
        /// </summary>
        public async UniTask LoadAsync(LoadContext context, CancellationToken cancellationToken)
        {
            if (_modules == null || _modules.Count == 0)
            {
                context.SetProgress(1f);
                context.SetState(LoadState.Completed);
                return;
            }

            float accumulatedWeight = 0f;

            for (int i = 0; i < _modules.Count; i++)
            {
                var entry = _modules[i];
                var service = entry.Service;

                // 跳过已初始化的模块
                if (service.IsInitialized)
                {
                    accumulatedWeight += entry.Weight;
                    continue;
                }

                // 计算当前模块在总进度中的区间
                float rangeStart = accumulatedWeight / _totalWeight;
                float rangeEnd = (accumulatedWeight + entry.Weight) / _totalWeight;

                context.SetDescription($"Initializing {entry.Name}...");
                context.SetProgress(rangeStart);

                // 创建范围进度报告器，将模块内部的 0~1 进度映射到 [rangeStart, rangeEnd]
                var scopedProgress = new ScopedProgress(context, rangeStart, rangeEnd);
                await service.InitializeAsync(scopedProgress, cancellationToken);

                accumulatedWeight += entry.Weight;
            }

            context.SetProgress(1f);
            context.SetDescription("Bootstrap completed.");
            context.SetState(LoadState.Completed);
        }

        #endregion

        #region Protected API

        /// <summary>
        /// 注册一个模块到启动管线。
        /// <para>通常在 <see cref="OnRegisterModules"/> 中调用，注册顺序决定初始化顺序。</para>
        /// </summary>
        /// <param name="service">模块服务实例。</param>
        /// <param name="name">模块名称，用于进度报告中的描述文字。</param>
        /// <param name="weight">模块在总进度中的权重。权重越大，占据的进度条越长。</param>
        protected void RegisterModule(IModuleService service, string name, float weight = 1f)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            if (weight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be > 0.");

            if (_modules == null)
                _modules = new List<ModuleEntry>();

            _modules.Add(new ModuleEntry(service, name, weight));
            _totalWeight += weight;
        }

        /// <summary>
        /// 注册模块的回调。子类可重写此方法来注册自定义的模块列表。
        /// <para>此方法在 <see cref="OnAwake"/> 中调用，早于 <see cref="ILoadable.LoadAsync"/>。</para>
        /// </summary>
        protected virtual void OnRegisterModules()
        {
            // 子类可重写此方法来注册模块。
            // 示例：
            // RegisterModule(new AssetBootstrapService(), "Asset Manager", 30f);
            // RegisterModule(new LockBootstrapService(), "Lock Manager", 10f);
            // RegisterModule(new MessageBootstrapService(), "Message System", 10f);
        }

        #endregion

        #region Lifecycle

        protected override void OnAwake()
        {
            base.OnAwake();
            OnRegisterModules();
        }

        protected override void OnDestroy()
        {
            // 反向销毁：后初始化的先销毁
            if (_modules != null)
            {
                for (int i = _modules.Count - 1; i >= 0; i--)
                {
                    _modules[i].Service.Dispose();
                }
                _modules.Clear();
                _modules = null;
            }

            _totalWeight = 0f;
            base.OnDestroy();
        }

        #endregion

        #region Private Types

        /// <summary>
        /// 模块注册条目。
        /// </summary>
        private readonly struct ModuleEntry
        {
            public readonly IModuleService Service;
            public readonly string Name;
            public readonly float Weight;

            public ModuleEntry(IModuleService service, string name, float weight)
            {
                Service = service;
                Name = name;
                Weight = weight;
            }
        }

        /// <summary>
        /// 范围进度报告器。将模块内部 0~1 的进度映射到外部 [rangeStart, rangeEnd] 区间。
        /// </summary>
        private sealed class ScopedProgress : IProgress<LoadContext>
        {
            private readonly LoadContext _context;
            private readonly float _rangeStart;
            private readonly float _rangeEnd;

            public ScopedProgress(LoadContext context, float rangeStart, float rangeEnd)
            {
                _context = context;
                _rangeStart = rangeStart;
                _rangeEnd = rangeEnd;
            }

            public void Report(LoadContext value)
            {
                if (value == null) return;

                // 将模块内的 overallProgress (0~1) 映射到父级区间
                float mappedProgress = _rangeStart + value.OverallProgress * (_rangeEnd - _rangeStart);
                _context.SetProgress(mappedProgress);

                // 透传描述信息
                if (!string.IsNullOrEmpty(value.Description))
                {
                    _context.SetDescription(value.Description);
                }
            }
        }

        #endregion
    }
}