using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XReactive;

namespace XFramework.XUI.Data
{
    /// <summary>
    /// UI 面板数据绑定组件。挂载在面板预制体根节点上。
    /// <para>提供两种绑定方式：</para>
    /// <para>1. 命名约定（自动）：子节点按前缀命名（txt_xxx、img_xxx、btn_xxx 等），调用 <see cref="BindByConvention{T}"/> 按属性名自动查找。</para>
    /// <para>2. 手动绑定：通过 <see cref="RegisterBinding{T}"/> 自由绑定任意 UI 组件。</para>
    /// </summary>
    [AddComponentMenu("XFramework/UI/UIPanelBinding")]
    public class UIPanelBinding : MonoBehaviour
    {
        #region Fields

        private IViewModel _viewModel;

        /// <summary>
        /// 所有绑定产生的订阅，面板销毁时统一释放。
        /// <para>预分配容量 16，避免扩容 GC。</para>
        /// </summary>
        private readonly List<IDisposable> _bindings = new List<IDisposable>(16);

        /// <summary>
        /// 按名字缓存已查找到的子节点组件，避免重复 GetComponent。
        /// <para>Key 为子节点名称（不区分大小写），Value 为找到的 Component。</para>
        /// </summary>
        private readonly Dictionary<string, Component> _componentCache
            = new Dictionary<string, Component>(16, StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public — Bind / Unbind

        /// <summary>
        /// 绑定 ViewModel 到此面板。面板关闭前需调用 <see cref="Unbind"/>。
        /// <para>调用后会自动调用 <see cref="CacheComponents"/> 缓存子节点组件。</para>
        /// </summary>
        /// <param name="viewModel">面板的 ViewModel 实例。</param>
        public void Bind(IViewModel viewModel)
        {
            Unbind(); // 先解绑旧的
            if (viewModel == null) return;

            _viewModel = viewModel;
            _viewModel.OnBound();
            CacheComponents(transform);
        }

        /// <summary>
        /// 解绑当前 ViewModel，释放所有订阅。
        /// <para>面板关闭时自动调用（通过 <see cref="OnDestroy"/>）。</para>
        /// </summary>
        public void Unbind()
        {
            if (_viewModel != null)
            {
                _viewModel.OnUnbound();
                _viewModel.Dispose();
                _viewModel = null;
            }

            foreach (var binding in _bindings)
                binding?.Dispose();
            _bindings.Clear();

            _componentCache.Clear();
        }

        /// <summary>
        /// 当前是否已绑定 ViewModel。
        /// </summary>
        public bool IsBound => _viewModel != null;

        #endregion

        #region Public — Manual Binding

        /// <summary>
        /// 手动注册绑定：将 ViewModel 的 <see cref="ReactiveProperty{T}"/> 绑定到 UI 组件。
        /// </summary>
        /// <typeparam name="T">值的类型。</typeparam>
        /// <param name="source">ViewModel 中的 ReactiveProperty。</param>
        /// <param name="onValueChanged">值变化时的回调，用于更新 UI 组件。</param>
        public void RegisterBinding<T>(ReactiveProperty<T> source, Action<T> onValueChanged)
        {
            if (source == null || onValueChanged == null) return;

            var disposable = source.Subscribe(value =>
            {
                // 检查自身未被销毁且处于激活状态
                if (this != null && isActiveAndEnabled)
                    onValueChanged(value);
            });
            _bindings.Add(disposable);

            // 立即同步当前值
            onValueChanged(source.Value);
        }

        #endregion

        #region Public — Convention-based Binding

        /// <summary>
        /// 按命名约定将 ViewModel 的 <see cref="ReactiveProperty{T}"/> 绑定到 UI 组件。
        /// <para>约定：子节点名 "txt_{propertyName}" → Text / "img_{propertyName}" → Image / "btn_{propertyName}" → Button。</para>
        /// <para>需先调用 <see cref="Bind"/>，确保组件已缓存。</para>
        /// </summary>
        /// <typeparam name="T">值的类型。</typeparam>
        /// <param name="propertyName">ViewModel 属性的名称（不含前缀）。</param>
        /// <param name="source">ViewModel 中的 ReactiveProperty。</param>
        public void BindByConvention<T>(string propertyName, ReactiveProperty<T> source)
        {
            if (source == null || string.IsNullOrEmpty(propertyName))
                return;

            // 优先 Text：txt_{propertyName}
            var textKey = $"txt_{propertyName}";
            if (_componentCache.TryGetValue(textKey, out var comp) && comp is Text text)
            {
                RegisterBinding(source, val => text.text = val?.ToString());
                return;
            }

            // Image：img_{propertyName}
            var imgKey = $"img_{propertyName}";
            if (_componentCache.TryGetValue(imgKey, out comp) && comp is Image img)
            {
                if (source is ReactiveProperty<Sprite> spriteProp)
                {
                    RegisterBinding(spriteProp, val => img.sprite = val);
                }
                else if (source is ReactiveProperty<Color> colorProp)
                {
                    RegisterBinding(colorProp, val => img.color = val);
                }
                else if (source is ReactiveProperty<float> fillProp)
                {
                    RegisterBinding(fillProp, val => img.fillAmount = val);
                }
                return;
            }

            // 未找到匹配组件（仅在编辑器中输出警告，避免 Release 版 GC）
#if UNITY_EDITOR
            Debug.LogWarning(
                $"[UIPanelBinding] 未找到 '{propertyName}' 对应的 UI 组件（需命名为 txt_{propertyName} 或 img_{propertyName}）: {gameObject.name}");
#endif
        }

        #endregion

        #region Public — Component Cache

        /// <summary>
        /// 缓存根节点下所有符合命名约定的子节点组件。
        /// <para>自动遍历子节点，按前缀归类：txt_ → Text、img_ → Image、btn_ → Button、sld_ → Slider、tgl_ → Toggle。</para>
        /// </summary>
        /// <param name="root">根 Transform（通常为 transform）。</param>
        public void CacheComponents(Transform root)
        {
            _componentCache.Clear();
            if (root == null) return;

            CacheComponentsRecursive(root, 0, 5); // 最多 5 层深度
        }

        private void CacheComponentsRecursive(Transform node, int depth, int maxDepth)
        {
            if (depth > maxDepth || node == null) return;

            var name = node.name;

            if (name.StartsWith("txt_", StringComparison.OrdinalIgnoreCase))
            {
                var text = node.GetComponent<Text>();
                if (text != null) _componentCache[node.name] = text;
            }
            else if (name.StartsWith("img_", StringComparison.OrdinalIgnoreCase))
            {
                var image = node.GetComponent<Image>();
                if (image != null) _componentCache[node.name] = image;
            }
            else if (name.StartsWith("btn_", StringComparison.OrdinalIgnoreCase))
            {
                var button = node.GetComponent<Button>();
                if (button != null) _componentCache[node.name] = button;
            }
            else if (name.StartsWith("sld_", StringComparison.OrdinalIgnoreCase))
            {
                var slider = node.GetComponent<Slider>();
                if (slider != null) _componentCache[node.name] = slider;
            }
            else if (name.StartsWith("tgl_", StringComparison.OrdinalIgnoreCase))
            {
                var toggle = node.GetComponent<Toggle>();
                if (toggle != null) _componentCache[node.name] = toggle;
            }

            // 递归子节点
            for (int i = 0; i < node.childCount; i++)
                CacheComponentsRecursive(node.GetChild(i), depth + 1, maxDepth);
        }

        #endregion

        #region Unity Lifecycle

        private void OnDestroy()
        {
            Unbind();
        }

        #endregion
    }
}