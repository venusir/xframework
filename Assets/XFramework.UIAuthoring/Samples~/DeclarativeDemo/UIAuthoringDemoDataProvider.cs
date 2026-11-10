using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XReactive;
using XFramework.XUI.Authoring;

namespace XFramework.Example
{
    /// <summary>
    /// 声明式 UI 示例的数据源：内部是一组 <see cref="ReactiveProperty{T}"/>，按数据键暴露给
    /// 预制体上的 <c>UIDeclarativePanel</c> / <c>UIDeclarativeHudItem</c>。
    /// <para>项目实际接入时把这里换成自己的数值来源（角色属性、背包、设置项）即可；
    /// 声明式视图只认 <see cref="IUIDataProvider"/>，不认识任何领域类型。</para>
    /// </summary>
    public sealed class UIAuthoringDemoDataProvider : IUIDataProvider, IDisposable
    {
        /// <summary>面板标题。</summary>
        public const string TitleKey = "demo.title";

        /// <summary>面板图片颜色。</summary>
        public const string TintKey = "demo.tint";

        /// <summary>面板滑条进度（双向）。</summary>
        public const string ProgressKey = "demo.progress";

        /// <summary>面板开关静音（双向）。</summary>
        public const string MuteKey = "demo.mute";

        /// <summary>HUD 名字。</summary>
        public const string HudNameKey = "demo.hudName";

        /// <summary>HUD 血条填充量。</summary>
        public const string HpKey = "demo.hp";

        private readonly Dictionary<string, object> _values =
            new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>创建示例数据源并填入初值。</summary>
        public UIAuthoringDemoDataProvider()
        {
            _values[TitleKey] = new ReactiveProperty<string>("声明式数据绑定示例");
            _values[TintKey] = new ReactiveProperty<Color>(new Color(0.4f, 0.8f, 1f, 1f));
            _values[ProgressKey] = new ReactiveProperty<float>(0.5f);
            _values[MuteKey] = new ReactiveProperty<bool>(false);
            _values[HudNameKey] = new ReactiveProperty<string>("NPC");
            _values[HpKey] = new ReactiveProperty<float>(0.8f);
        }

        /// <summary>取指定键的响应式属性；类型不匹配返回 false。</summary>
        public ReactiveProperty<T> GetProperty<T>(string key)
        {
            return _values.TryGetValue(key, out object value) && value is ReactiveProperty<T> typed
                ? typed
                : null;
        }

        /// <inheritdoc />
        public bool TryGet<T>(string key, out IReactiveProperty<T> source)
        {
            source = null;
            if (!_values.TryGetValue(key, out object value))
                return false;

            if (value is IReactiveProperty<T> typed)
            {
                source = typed;
                return true;
            }

            return false;
        }

        /// <summary>释放全部响应式属性；调用方必须先注销 provider。</summary>
        public void Dispose()
        {
            foreach (object value in _values.Values)
                (value as IDisposable)?.Dispose();

            _values.Clear();
        }
    }
}
