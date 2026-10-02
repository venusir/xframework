using UnityEngine.InputSystem;

namespace XFramework.XInput.Default
{
    /// <summary>
    /// 用 Unity Input System 的默认提供者初始化时，把**由你加载好的** <see cref="InputActionAsset"/> 交给它。
    /// <para><b>为什么需要它</b>：无参 <c>InputManager.Initialize()</c> 走
    /// <c>Resources.Load("InputSystem_Actions")</c>——那是「零配置起步」的默认路径，却也是**唯一**路径。
    /// 资源放在 <c>Resources/</c> 意味着它总被打进包里、且不能交给 YooAsset 之类的资源系统管理。
    /// 本类型把那一步交还给使用方：YooAsset / Addressables / 自己的加载器都行，加载完把资产交过来即可。</para>
    /// <para><b>它不影响「换输入插件」那条路</b>：本类型属于 <c>XInput.Default</c>（Unity Input System 的实现），
    /// 用 Rewired 之类插件的项目走 <c>InputManager.Initialize(IInputProvider)</c>，公开面不必看这里。</para>
    /// </summary>
    public sealed class InputSystemOptions
    {
        /// <summary>初始 ActionMap 的默认名。<c>"Player"</c> 是零配置路径沿用的约定。</summary>
        public const string DefaultInitialActionMap = "Player";

        /// <summary>
        /// 已加载的输入资产。**加载与生命周期都由使用方负责**（框架不接管它的释放）——
        /// 经 <c>AssetManager</c> 加载的句柄请自行持有到输入管理器销毁为止。
        /// </summary>
        public InputActionAsset Asset;

        /// <summary>
        /// 初始化后自动切换到的 ActionMap 名，默认 <see cref="DefaultInitialActionMap"/>。
        /// <para><c>null</c> 或空串 = <b>不自动切换</b>：保持资产 <c>Enable()</c> 后的「全部 map 常开」状态——
        /// 想自己管理 map 的项目用这个形态。名字不存在时只打警告（与无参路径共用同一实现），不抛。</para>
        /// </summary>
        public string InitialActionMap = DefaultInitialActionMap;
    }
}
