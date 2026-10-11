using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 遮罩的<b>射线管道</b>端到端测试：点击真的送得到遮罩上吗。
    /// <para><b>为什么与 <see cref="UIMaskTests"/> 分开</b>：那边验的是<b>接线</b>——直接调
    /// <c>Button.onClick.Invoke()</c>，绕开整条 UGUI 射线管道（前提说明见 <c>UIPopupTests</c> 头部）。
    /// 接线通了不等于点得到：<c>GraphicRaycaster</c> 只查<b>自己 GameObject 上那个 Canvas</b> 下注册的
    /// 图形，而遮罩自带 Canvas，所以组件缺了也没人发现。本 fixture 补的就是这一段。</para>
    /// <para><b>为什么不需要输入模块</b>：<c>EventSystem.RaycastAll</c> 遍历的是全局 raycaster 列表，
    /// 只要 raycaster 处于激活态即可，不需要 <c>StandaloneInputModule</c> 之类。本工程
    /// <c>activeInputHandler = 1</c>（仅 Input System），挂标准输入模块反而会抛——
    /// 故这里<b>刻意只建 EventSystem、不挂输入模块</b>。</para>
    /// <para>本 fixture 验的是「射线送得到 + 送到的确实是遮罩」，不是「指针事件在真机上如何合成」。</para>
    /// </summary>
    [TestFixture]
    public class UIMaskPointerTests
    {
        #region Fields

        private GameObject _root;
        private GameObject _eventSystemGo;
        private EventSystem _eventSystem;
        private FakePanelFactory _factory;

        /// <summary>正向对照物：射线链路本身通不通，靠它证明。</summary>
        private GameObject _control;

        private readonly List<RaycastResult> _results = new List<RaycastResult>();

        #endregion

        #region Lifecycle

        [SetUp]
        public void SetUp()
        {
            // 归零：不依赖上一个 fixture 的 TearDown（SetUp 中途失败时它不会执行）
            UIManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            // UIRoot 必须自带 Canvas：Overlay 画布会把根 RectTransform 撑成屏幕尺寸，锚点才有意义。
            // 没有它时根矩形只是一个 100x100 的默认值，按锚点算出的坐标会落在屏幕外
            // （实测：对照物被算到 -25,-25）——那是夹具没搭对，不是被测对象的问题。
            _root = new GameObject("UIRoot_MaskPointerTest", typeof(RectTransform), typeof(Canvas));

            // 显式把根矩形摆成屏幕区域 [0,w]x[0,h]：生产里这件事由 UIRootNode 的 Canvas 负责，
            // 但本夹具实测 Overlay 根画布在 batchmode 下不会把根矩形撑开——它停在默认的
            // 100x100 且以原点为中心，于是按锚点算出的坐标落进负半轴，GraphicRaycaster 判越界后
            // 直接返回、一条都不命中（实测对照物被算到 -25,-25）。显式摆正后锚点才有意义。
            var rootRt = _root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(Screen.width, Screen.height);
            rootRt.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<FakePanelB>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);

            _eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
            _eventSystem = _eventSystemGo.GetComponent<EventSystem>();

            _control = CreatePositiveControl();
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_control != null)
                UnityEngine.Object.DestroyImmediate(_control);

            if (_eventSystemGo != null)
                UnityEngine.Object.DestroyImmediate(_eventSystemGo);

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        #endregion

        #region Positive control

        /// <summary>
        /// 正向对照：一个<b>确定能被射线命中</b>的对象，用来把「遮罩没被命中」与
        /// 「本 fixture 的射线链路根本不工作」分开。它绿而遮罩红，结论才落在遮罩身上。
        /// <para><b>排序必须高于遮罩</b>：遮罩是全屏的，若对照物排在它下面，
        /// 遮罩修好之后对照物会被盖住、这条控制反而变红——控制组必须与缺陷修复无关。</para>
        /// <para>摆在左下方四分之一处，避开屏幕中心（中心那一点留给遮罩的用例）。</para>
        /// </summary>
        private GameObject CreatePositiveControl()
        {
            var go = new GameObject("MaskPointer_PositiveControl", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_root.transform, false);
            rt.anchorMin = new Vector2(0.15f, 0.15f);
            rt.anchorMax = new Vector2(0.35f, 0.35f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = UISorting.SystemOrder;

            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<Image>();

            return go;
        }

        [Test]
        public async Task PositiveControl_IsHitByRaycast()
        {
            RequireMeasurableScreen();
            await SettleCanvas();

            var point = ScreenPointOf(_control.GetComponent<RectTransform>());
            RaycastAll(point);

            Assert.IsTrue(Contains(_control),
                $"正向对照物应被射线命中——它不中说明本 fixture 的射线链路没搭对，而不是遮罩有问题。{Describe(point)}");
        }

        #endregion

        #region 遮罩的射线可命中性

        [Test]
        public async Task Mask_IsHitByRaycast()
        {
            RequireMeasurableScreen();

            UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, Color.black, clickToClose: true));
            await SettleCanvas();

            var mask = FindMask();
            Assert.IsNotNull(mask, "前置条件：遮罩已创建");

            var point = ScreenPointOf(mask.GetComponent<RectTransform>());
            RaycastAll(point);

            Assert.IsTrue(Contains(mask.gameObject),
                $"点击必须送得到遮罩上——命中不到它，clickToClose 就永不触发，遮罩也挡不住下方输入。{Describe(point)}");
        }

        /// <summary>
        /// 端到端：一次真实的射线命中 → 解析出 <c>IPointerClickHandler</c> → 执行指针点击 →
        /// 栈顶被弹掉。这是「点击遮罩关闭面板」这条 README 承诺的完整链路。
        /// <para>与 <c>UIMaskTests.ClickToClose_PopsTopPanel</c> 的差别：那条从
        /// <c>Button.onClick.Invoke()</c> 起步（接线），这条从射线起步（管道）。</para>
        /// </summary>
        [Test]
        public async Task ClickToClose_TakesEffectUnderRaycast()
        {
            RequireMeasurableScreen();

            await UIManager.OpenAsync<FakePanel>("ui/first");
            await UIManager.PushAsync<FakePanelB>("ui/second");

            UIManager.ShowMask(new UIMaskStyle(UILayers.Mask, Color.black, clickToClose: true));
            await SettleCanvas();

            var mask = FindMask();
            Assert.IsNotNull(mask, "前置条件：遮罩已创建");

            var point = ScreenPointOf(mask.GetComponent<RectTransform>());
            RaycastAll(point);

            Assert.IsTrue(_results.Count > 0, $"屏幕中心应有命中物。{Describe(point)}");

            // 最高排序的命中项应当就是遮罩：遮罩取所在层末位序（MaskOrder(500) = 16031），
            // 高于其上任何面板（默认层 100 → 3201 起）
            var hit = _results[0].gameObject;
            var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);

            Assert.AreSame(mask.gameObject, handler,
                $"最高排序的命中项应解析出遮罩的点击处理器。{Describe(point)}");

            var data = new PointerEventData(_eventSystem)
            {
                position = point,
                button = PointerEventData.InputButton.Left,
            };

            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerClickHandler);
            await UniTask.Yield();

            Assert.IsFalse(UIManager.IsOpen<FakePanelB>(), "点击遮罩应弹掉栈顶");
            Assert.IsTrue(UIManager.IsOpen<FakePanel>(), "栈底面板应保持打开");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 让 Canvas 完成一次布局再取坐标。
        /// <para>Screen Space - Overlay 画布的 RectTransform 由 Canvas 在帧更新里设成屏幕尺寸，
        /// 同一帧内新建的对象还没被摆好——此时 <c>rt.position</c> 仍是 (0,0)，据此算出的屏幕坐标
        /// 会落在屏幕角落，命中为空。<b>这不是缺陷，是取坐标的时机不对</b>：实测未 settle 时
        /// 四个用例的 <c>rayPoint</c> 全是 <c>0.0,0.0</c>（对照物为 <c>-25.0,-25.0</c>）。</para>
        /// </summary>
        private static async UniTask SettleCanvas()
        {
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// 射线在 Overlay 画布下的兜底依赖屏幕尺寸：<c>Screen.width/height</c> 为 0 时，
        /// <c>GraphicRaycaster</c> 会判定坐标越界并直接返回，一条都不命中。
        /// <para>这时要<b>明确说出「环境不支持」</b>，而不是让遮罩替环境背锅。</para>
        /// </summary>
        private static void RequireMeasurableScreen()
        {
            Assert.Greater(Screen.width, 0,
                "batchmode 下屏幕尺寸为 0 ⇒ 射线层不可测（不是遮罩的缺陷）：" +
                "GraphicRaycaster 会把所有坐标判为越界。此时应退到结构断言（UIMaskTests.Mask_HasGraphicRaycaster）。");
            Assert.Greater(Screen.height, 0, "同上：Screen.height 为 0。");
        }

        /// <summary>Overlay 画布下世界坐标即屏幕像素，故 <c>WorldToScreenPoint(null, …)</c> 可直接用。</summary>
        private static Vector2 ScreenPointOf(RectTransform rt)
        {
            return RectTransformUtility.WorldToScreenPoint(null, rt.position);
        }

        private void RaycastAll(Vector2 point)
        {
            _results.Clear();

            var data = new PointerEventData(_eventSystem)
            {
                position = point,
                button = PointerEventData.InputButton.Left,
            };

            _eventSystem.RaycastAll(data, _results);
        }

        private bool Contains(GameObject go)
        {
            for (int i = 0; i < _results.Count; i++)
            {
                if (ReferenceEquals(_results[i].gameObject, go))
                    return true;
            }

            return false;
        }

        private Transform FindMask()
        {
            return _root.transform.Find("UIManager_Mask");
        }

        /// <summary>失败消息里带上足以判断「下一步做什么」的现场：屏幕尺寸、命中数与命中物名字。</summary>
        private string Describe(Vector2 point)
        {
            var sb = new StringBuilder();
            sb.Append(" screen=").Append(Screen.width).Append('x').Append(Screen.height);
            sb.Append(" rayPoint=").Append(point.x.ToString("F1")).Append(',').Append(point.y.ToString("F1"));
            sb.Append(" 命中=").Append(_results.Count);

            if (_results.Count > 0)
            {
                sb.Append(" [");
                for (int i = 0; i < _results.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    sb.Append(_results[i].gameObject.name);
                }
                sb.Append(']');
            }

            return sb.ToString();
        }

        #endregion
    }
}
