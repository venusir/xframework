using System;
using UnityEngine;

namespace XFramework.Example
{
    /// <summary>
    /// 展示 XFramework Reactive 模块的响应式属性用法。
    /// <para>通过 <see cref="ReactiveProperty{T}"/> 节点实现属性值变化的自动推送与订阅。</para>
    /// </summary>
    public class SampleExample : MonoBehaviour
    {
        #region Private Fields

        private RootNode _root;
        private ReactiveProperty<int> _healthProp;
        private ReactiveProperty<float> _scoreProp;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // 1. 创建节点树
            _root = RootNode.Create();

            // 2. 创建响应式属性节点
            //    ReactiveProperty<T> 继承 LeafNode，可直接挂入节点树
            //    通过 NodeFactory.GetNode<T>(arg) 创建并初始化初始值
            _healthProp = _root.AddNode<ReactiveProperty<int>>("Health");
            _scoreProp = _root.AddNode<ReactiveProperty<float>>("Score");
        }

        private void Start()
        {
            // 3. 设置初始值（设置 Value 会自动通知订阅者）
            _healthProp.Value = 100;
            _scoreProp.Value = 0f;

            // 4. 订阅值变化
            //    ReactiveProperty<T> 实现了 IReactiveProperty<T>，
            //    节点本身可直接被订阅，无需通过中间属性
            _healthProp.Subscribe(value =>
            {
                Debug.Log($"[Health] 当前血量: {value}");
                UpdateHealthBar(value);
            });

            _scoreProp.Subscribe(value =>
            {
                Debug.Log($"[Score] 当前分数: {value}");
                UpdateScoreUI(value);
            });

            // 5. 模拟值变化
            SimulateGameplay();
        }

        private void OnDestroy()
        {
            // 6. 销毁节点树（自动回池）
            _root?.Destroy();
        }

        #endregion

        #region Private Methods

        private void SimulateGameplay()
        {
            // 设置值会自动通知所有订阅者
            _healthProp.Value = 80;  // 受到伤害
            _scoreProp.Value = 100;  // 获得分数

            _healthProp.Value = 50;  // 再次受伤
            _scoreProp.Value = 250;  // 更多分数

            _healthProp.Value = 0;   // 死亡
        }

        private void UpdateHealthBar(int health)
        {
            // 实际项目中更新 UI 血条
            Debug.Log($"[UI] 血条更新至: {health}");
        }

        private void UpdateScoreUI(float score)
        {
            // 实际项目中更新 UI 分数
            Debug.Log($"[UI] 分数更新至: {score}");
        }

        #endregion
    }
}
