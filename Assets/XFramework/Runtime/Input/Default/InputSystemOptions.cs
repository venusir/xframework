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
        /// <summary>
        /// 已加载的输入资产。**加载与生命周期都由使用方负责**（框架不接管它的释放）——
        /// 经 <c>AssetManager</c> 加载的句柄请自行持有到输入管理器销毁为止。
        /// </summary>
        public InputActionAsset Asset;
    }
}
