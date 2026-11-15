using System.Collections.Generic;
using UnityEngine;
using XFramework.XAsset;
using XFramework.XData;
using XFramework.XInput;
using XFramework.XInput.Default;
using XFramework.XLocalization;
using XFramework.XSave;
using XFramework.XUI;
using XFramework.XUI.Controller;

namespace XFramework.XBootstrap
{

    /// <summary>
    /// 框架内置引导阶段的构造目录：把「new 一个阶段并装好 options」收成一个可读的登记表达式。
    /// <para><b>只构造，不登记</b>——登记仍经 <see cref="Bootstrap.Register(IBootstrapStage)"/>；本目录不改变
    /// 相位语义、不做依赖排序，也不会替使用方决定初始化什么。它解决的是「登记代码里满是 `new XxxBootstrapStage(...)`」
    /// 的可读性问题，不是配置中心。</para>
    /// <para>留空 / null 的语义与直接构造对应阶段完全一致（例如 <c>Save(null)</c> 得到默认 <c>SaveOptions</c>）。</para>
    /// </summary>
    public static class BootstrapStages
    {
        /// <summary>Asset 阶段（相位 0）。</summary>
        /// <param name="options">资源初始化参数；null 用阶段自身的默认值。</param>
        public static IBootstrapStage Asset(AssetInitOptions options = null)
        {
            return new AssetBootstrapStage(options);
        }

        /// <summary>Data 阶段（相位 3）。</summary>
        public static IBootstrapStage Data()
        {
            return new DataBootstrapStage();
        }

        /// <summary>Save 阶段（相位 4；硬性晚于 Data）。</summary>
        /// <param name="options">存档参数；null 用阶段自身的默认值。</param>
        public static IBootstrapStage Save(SaveOptions options = null)
        {
            return new SaveBootstrapStage(options);
        }

        /// <summary>UI 阶段（相位 2）。</summary>
        /// <param name="uiRoot">场景里的 UI 根（Canvas 根）。不能为 null。</param>
        /// <param name="controller">UI 拦截/路由控制器，可为 null。</param>
        public static IBootstrapStage UI(Transform uiRoot, IUIController controller = null)
        {
            return new UIBootstrapStage(uiRoot, controller);
        }

        /// <summary>Input 阶段（相位 1），使用 Unity Input System 后端。</summary>
        /// <param name="options">输入参数（资产 + 初始 ActionMap 名）。不能为 null。</param>
        public static IBootstrapStage Input(InputSystemOptions options)
        {
            return new InputBootstrapStage(options);
        }

        /// <summary>Input 阶段（相位 1），使用第三方输入后端。</summary>
        /// <param name="provider">输入后端实现。不能为 null。</param>
        public static IBootstrapStage Input(IInputProvider provider)
        {
            return new InputBootstrapStage(provider);
        }

        /// <summary>Localization 阶段（相位 90）。</summary>
        /// <param name="defaultLanguage">默认语言标识。</param>
        /// <param name="data">扁平语言表数据，可为 null。</param>
        public static IBootstrapStage Localization(string defaultLanguage = "zh_Hans",
                                                   Dictionary<string, string> data = null)
        {
            return new LocalizationBootstrapStage(defaultLanguage, data);
        }

        /// <summary>Localization 阶段（相位 90），从 <see cref="TextAsset"/>（扁平 JSON）读语言表。</summary>
        /// <param name="defaultLanguage">默认语言标识。</param>
        /// <param name="table">语言表资产；可为 null（阶段会打一条警告后跳过初始化）。</param>
        public static IBootstrapStage Localization(string defaultLanguage, TextAsset table)
        {
            return new LocalizationBootstrapStage(defaultLanguage, table);
        }
    }
}
