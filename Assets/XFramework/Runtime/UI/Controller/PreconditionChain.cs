using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace XFramework.XUI.Controller
{
    /// <summary>
    /// 前提条件链。链式组合多个异步校验条件，按顺序依次执行。
    /// <para>任何一个条件返回 false 则中断链并返回 false，常用于 <see cref="IUIController.OnBeforeOpenAsync"/> 中。</para>
    /// <para>使用示例：</para>
    /// <code>
    /// var chain = new PreconditionChain(panelType, assetPath, layer, userData)
    ///     .Add(CheckLoginAsync)
    ///     .Add(CheckVipAsync);
    /// var canOpen = await chain.ExecuteAsync();
    /// </code>
    /// </summary>
    public sealed class PreconditionChain
    {
        #region Fields

        /// <summary>
        /// 条件列表。预分配容量 3 避免扩容 GC。
        /// </summary>
        private readonly List<Func<Type, string, int, object, UniTask<bool>>> _conditions
            = new List<Func<Type, string, int, object, UniTask<bool>>>(3);

        /// <summary>
        /// 传递给每个条件的参数。
        /// </summary>
        private readonly Type _panelType;
        private readonly string _assetPath;
        private readonly int _layer;
        private readonly object _userData;

        #endregion

        #region Constructor

        /// <summary>
        /// 创建前提条件链。
        /// </summary>
        /// <param name="panelType">面板类型。</param>
        /// <param name="assetPath">面板资源的 YooAsset 地址。</param>
        /// <param name="layer">面板层级。</param>
        /// <param name="userData">自定义数据。</param>
        public PreconditionChain(Type panelType, string assetPath, int layer, object userData)
        {
            _panelType = panelType;
            _assetPath = assetPath;
            _layer = layer;
            _userData = userData;
        }

        #endregion

        #region Public — Add / Execute

        /// <summary>
        /// 添加一个前提条件到链的末尾。
        /// </summary>
        /// <param name="condition">
        /// 一个异步校验函数。参数依次为面板类型、资源路径、层级、自定义数据。返回 true 允许继续，false 则中断链。
        /// </param>
        /// <returns>自身，支持链式调用。</returns>
        public PreconditionChain Add(Func<Type, string, int, object, UniTask<bool>> condition)
        {
            if (condition != null)
                _conditions.Add(condition);
            return this;
        }

        /// <summary>
        /// 依次执行所有前提条件。
        /// <para>任何一个条件返回 false 则立即返回 false 并停止执行后续条件。</para>
        /// </summary>
        /// <returns>true 表示所有条件通过，false 表示被某个条件拦截。</returns>
        public async UniTask<bool> ExecuteAsync()
        {
            for (int i = 0; i < _conditions.Count; i++)
            {
                var canProceed = await _conditions[i](_panelType, _assetPath, _layer, _userData);
                if (!canProceed)
                    return false;
            }
            return true;
        }

        #endregion
    }
}