using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XLocalization;
using XFramework.XMessage;

namespace XFramework.XLocalization.Tests
{
    /// <summary>
    /// <see cref="LocalizationManager.Subscribe"/> 的 <b>MonoBehaviour 销毁令牌分支</b>的 PlayMode 覆盖。
    /// <para><b>为什么只能在 PlayMode</b>：EditMode 下 <c>Object.DestroyImmediate</c> <b>不会</b>触发
    /// <c>MonoBehaviour.destroyCancellationToken</c>——Editor 侧的
    /// <c>LocalizationSubscribeTests</c> 自己记着这句话（「该分支只能在 PlayMode 覆盖，本轮未新增
    /// PlayMode fixture」）。本 fixture 就是那一条。</para>
    /// <para><b>它验的是本模块自己的归口入口</b>：<c>LocalizationManager.Subscribe</c> →
    /// <c>MessageManager.TryBindToDestroy</c>。UI 与 Lock 侧此前已各有用例，本模块的门面此前没有。</para>
    /// </summary>
    [TestFixture]
    public class LocalizationDestroyBindingTests
    {
        #region Test Doubles

        /// <summary>只为拿一个真实的 <c>destroyCancellationToken</c>。</summary>
        private sealed class Host : MonoBehaviour
        {
        }

        #endregion

        #region Fixture

        private GameObject _hostObject;

        [SetUp]
        public void SetUp()
        {
            LocalizationManager.Destroy();
            MessageManager.Clear();
            LocalizationManager.Initialize("en", new Dictionary<string, string> { { "seed", "seed" } });
            _hostObject = new GameObject("LocalizationDestroyBinding_Host");
        }

        [TearDown]
        public void TearDown()
        {
            if (_hostObject != null)
                UnityEngine.Object.DestroyImmediate(_hostObject);

            LocalizationManager.Destroy();
            MessageManager.Clear();
        }

        /// <summary>把 <paramref name="lang"/> 塞进缓存，好让 <c>SetLanguage</c> 能同步切过去。</summary>
        private static void CacheLanguage(string lang)
            => LocalizationManager.SetLanguageData(lang, new Dictionary<string, string> { { "seed", "seed" } });

        #endregion

        #region 销毁令牌

        [Test]
        public async Task MonoBehaviourContext_ReceivesUntilDestroyed_ThenAutoUnsubscribes()
        {
            CacheLanguage("ja");
            var host = _hostObject.AddComponent<Host>();

            int calls = 0;
            LocalizationManager.Subscribe(_ => calls++, host);

            LocalizationManager.SetLanguage("ja");
            Assert.AreEqual(1, calls, "前提校验：销毁前订阅应当是活的");

            UnityEngine.Object.Destroy(host);

            // Destroy 延后到帧末落地，令牌也在那时取消
            await UniTask.Yield();
            await UniTask.Yield();

            CacheLanguage("en");
            LocalizationManager.SetLanguage("en");

            Assert.AreEqual(1, calls, "对象销毁后订阅应当自动退订——这正是归口入口的承诺");
        }

        /// <summary>
        /// 边界：<c>Destroy</c> 已调用但帧还没走完时，令牌**尚未**取消，订阅仍然活着。
        /// 使用方会踩到这条（「我明明 Destroy 了，回调怎么还来了一次」），值得钉住。
        /// </summary>
        [Test]
        public async Task MonoBehaviourContext_DestroyedSameFrame_StillReceivesUntilFrameEnd()
        {
            CacheLanguage("ja");
            var host = _hostObject.AddComponent<Host>();

            int calls = 0;
            LocalizationManager.Subscribe(_ => calls++, host);

            UnityEngine.Object.Destroy(host);

            LocalizationManager.SetLanguage("ja");
            Assert.AreEqual(1, calls, "Destroy 在帧末才落地——同帧内订阅仍在，回调照常送达");

            await UniTask.Yield();
            await UniTask.Yield();

            CacheLanguage("en");
            LocalizationManager.SetLanguage("en");

            Assert.AreEqual(1, calls, "跨帧之后应已自动退订");
        }

        #endregion
    }
}
