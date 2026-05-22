using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework.XReactive;

namespace XFramework.Example
{

    /// <summary>
    /// 展示 XFramework Reactive 模块的响应式属性用法。
    /// <para><see cref="ReactiveProperty{T}"/> 是轻量可观察属性，不依赖节点树，直接 new 即可使用。</para>
    /// <para>订阅产生的 disposable 由 <see cref="ViewModelBase"/> 或手动管理。</para>
    /// </summary>
    public class SampleExample : MonoBehaviour
    {
        #region Private Fields

        private ReactiveProperty<int> _healthProp = new ReactiveProperty<int>(100);
        private ReactiveProperty<float> _scoreProp = new ReactiveProperty<float>(0f);

        /// <summary>
        /// 储存所有订阅，销毁时统一释放。
        /// </summary>
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(4);

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            // 1. 订阅值变化
            _subscriptions.Add(_healthProp.Subscribe(value =>
            {
                Debug.Log($"[Health] 当前血量: {value}");
                UpdateHealthBar(value);
            }));

            _subscriptions.Add(_scoreProp.Subscribe(value =>
            {
                Debug.Log($"[Score] 当前分数: {value}");
                UpdateScoreUI(value);
            }));

            // 2. 模拟值变化
            SimulateGameplay();
        }

        private void OnDestroy()
        {
            // 3. 释放所有订阅
            foreach (var sub in _subscriptions)
                sub?.Dispose();
            _subscriptions.Clear();

            // 4. 释放属性（如果不再需要）
            _healthProp?.Dispose();
            _scoreProp?.Dispose();
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
