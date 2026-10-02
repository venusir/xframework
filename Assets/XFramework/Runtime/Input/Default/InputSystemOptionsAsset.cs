using UnityEngine;
using UnityEngine.InputSystem;

namespace XFramework.XInput.Default
{
    /// <summary>
    /// <see cref="InputSystemOptions"/> 的<b>资产形式</b>：把「用哪个输入资产 + 初始 ActionMap 名」做成
    /// 可以在 Inspector 里填、可跨场景与多套配置复用的 <see cref="ScriptableObject"/>。
    /// <para><b>它为什么必须住在这里</b>：本模块的边界约定是「Unity 输入类型只出现在 <c>XInput.Default</c>」
    /// （门面与 <c>IInputProvider</c> 保持插件中立，整体替换后端——Rewired 那类——的项目在公开面不该看到
    /// Unity 的类型）。别的模块（如 <c>XBootstrap</c> 的 <c>DefaultGameLauncher</c>）要提供「Inspector 里配输入」
    /// 的入口，就只能引用本命名空间下的框架类型，于是可序列化的载体也只能住在这里。守卫见
    /// <c>Tests/Editor/Architecture/ModuleBoundaryTests.UnityInputTypes_StayInsideTheDefaultNamespace</c>。</para>
    /// <para>用法：建一个本资产（<c>Create → XFramework → Input System Options</c>），把 <c>.inputactions</c>
    /// 拖进资产槽；交给 <c>DefaultGameLauncher</c> 的字段，或自行
    /// <c>Bootstrap.Register(new InputBootstrapStage(config.ToOptions()))</c>。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "InputSystemOptions", menuName = "XFramework/Input System Options")]
    public sealed class InputSystemOptionsAsset : ScriptableObject
    {
        #region Serialized Fields

        [Tooltip("输入资产（.inputactions）。加载与生命周期由使用方负责——本资产只持有引用。")]
        [SerializeField] private InputActionAsset _asset;

        [Tooltip("初始化后自动切换到的 ActionMap 名；空 = 不自动切换（保持全部 map 常开）。")]
        [SerializeField] private string _initialActionMap = InputSystemOptions.DefaultInitialActionMap;

        #endregion

        #region Public API

        /// <summary>
        /// 装成 <see cref="InputSystemOptions"/>，可交给 <c>InputManager.Initialize</c> 或
        /// <c>InputBootstrapStage</c>。资产槽留空时得到的是一份 <c>Asset == null</c> 的选项——
        /// 消费方（<c>InputManager.Initialize</c>）会以它自己的参数校验拒绝，消息只有那一处。
        /// </summary>
        public InputSystemOptions ToOptions()
        {
            return new InputSystemOptions
            {
                Asset = _asset,
                InitialActionMap = _initialActionMap,
            };
        }

        #endregion
    }
}
