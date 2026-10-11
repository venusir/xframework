using UnityEngine;

namespace XFramework.XUI
{
    /// <summary>
    /// 弹窗相对锚点的展开方向。
    /// </summary>
    public enum UIPopupDirection
    {
        /// <summary>
        /// 自动选向：按 <see cref="Down"/> → <see cref="Up"/> → <see cref="Right"/> → <see cref="Left"/>
        /// 取第一个放得下的方向；四个都放不下时取可用空间最大的那个，再由收敛兜底。
        /// </summary>
        Auto = 0,

        /// <summary>向下展开（弹窗顶边贴锚点底边减去 offset）。下拉框、组合框的常用方向。</summary>
        Down = 1,

        /// <summary>向上展开（弹窗底边贴锚点顶边加上 offset）。</summary>
        Up = 2,

        /// <summary>向左展开（弹窗右边贴锚点左边减去 offset）。左侧子菜单。</summary>
        Left = 3,

        /// <summary>向右展开（弹窗左边贴锚点右边加上 offset）。右侧子菜单。</summary>
        Right = 4,
    }

    /// <summary>
    /// 弹窗在<b>垂直于展开方向的那根轴</b>上的对齐方式。
    /// <para>垂直展开（<see cref="UIPopupDirection.Down"/> / <see cref="UIPopupDirection.Up"/>）时它管<b>水平</b>
    /// 对齐；水平展开（<see cref="UIPopupDirection.Left"/> / <see cref="UIPopupDirection.Right"/>）时它管
    /// <b>垂直</b>对齐。</para>
    /// </summary>
    public enum UIPopupAlign
    {
        /// <summary>起点对齐：垂直展开时左对齐，水平展开时顶对齐。</summary>
        Start = 0,

        /// <summary>在锚点上居中。</summary>
        Center = 1,

        /// <summary>终点对齐：垂直展开时右对齐，水平展开时底对齐。</summary>
        End = 2,
    }

    /// <summary>
    /// 弹窗定位：算出弹窗该摆在锚点元素的哪里。用于下拉菜单、右键菜单、悬浮说明、组合框展开、嵌套子菜单。
    /// <para><b>纯几何，不含策略</b>：无状态、无 Unity 对象依赖、值进值出。它不管弹窗怎么开、什么时候关、
    /// 有没有箭头——那些是调用方的事（见 <c>remarks</c> 的边界说明）。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>为什么抽成纯函数：</b>翻转、避让、贴边收敛全是几何，抽出来才能脱离场景直接验证——
    /// 否则这些边界（贴屏幕下沿、锚点在角落、弹窗比屏幕还大）只能在真机上碰运气。与
    /// <see cref="XFramework.XUI.View.UISafeArea.CalculateAnchors"/> 同一理由。</para>
    /// <para><b>坐标系：</b>输入与输出都是<b>屏幕像素坐标</b>（左下为原点，y 向上）。本框架的 UI 根被
    /// 强制为 <c>RenderMode.ScreenSpaceOverlay</c>（见 <see cref="XFramework.XUI.View.UIRootNode"/>），
    /// 该模式下画布世界空间与屏幕像素一一对应，因此把结果直接赋给 <c>rectTransform.position</c> 即可。
    /// <b>换到 ScreenSpaceCamera 或 WorldSpace 画布时本函数的结果不再成立</b>——需要时请自行换算。</para>
    /// <para><b>为什么两个轴都收敛：</b>只收敛一侧会在「锚点贴屏幕左缘 + 弹窗比锚点宽」时把弹窗推出屏幕。
    /// 收敛用显式分支而非 <c>Mathf.Clamp</c>，因为弹窗大于可用区域时上界会小于下界，而
    /// <c>Mathf.Clamp</c> 在 <c>max &lt; min</c> 时的返回值取决于输入值，语义不可靠。</para>
    /// <para><b>不做自动初始化，且这是设计结论：</b>本类是纯静态工具，没有需要复位的状态、没有需要登记
    /// 的启动档位，故刻意不带 <c>[RuntimeInitializeOnLoadMethod]</c>——带去反而要进
    /// <c>AutoInitTests</c> 的族清单。与 <see cref="UISorting"/> / <see cref="UILayers"/> 同类。</para>
    /// <para><b>边界：</b>本类只算位置。点击外部关闭、开合生命周期、动画、箭头绘制都不在这里——
    /// 它们是调用方（或面板子类）的职责，不要往这里加。</para>
    /// </remarks>
    public static class UIPopupPosition
    {
        #region Derivation

        /// <summary>
        /// 算出弹窗的 <b>pivot 应该落在的屏幕点</b>。
        /// <para>返回的是 pivot 的位置，不是角——<c>rectTransform.position</c> 赋值设置的是物体的 pivot。
        /// 因此调用方必须把自己预制体的 pivot 一并传进来（<paramref name="popupPivot"/>），
        /// 否则中心 pivot 的预制体会静默错位半个尺寸。</para>
        /// </summary>
        /// <param name="anchorScreenRect">锚点元素的屏幕矩形，可由 <see cref="GetScreenRect"/> 取得。</param>
        /// <param name="popupSize">弹窗尺寸（像素）。非正值按 0 处理，不抛异常。</param>
        /// <param name="direction">展开方向，见 <see cref="UIPopupDirection"/>。</param>
        /// <param name="align">垂直于展开方向那根轴上的对齐方式，见 <see cref="UIPopupAlign"/>。</param>
        /// <param name="screenRect">可用区域（屏幕像素）。传全屏即
        /// <c>new Rect(0, 0, Screen.width, Screen.height)</c>；要避让刘海/圆角则传安全区，
        /// 取值见 <see cref="Screen.safeArea"/>。</param>
        /// <param name="popupPivot">弹窗预制体的 pivot（归一化，<c>(0,0)</c> 为左下、<c>(0.5,0.5)</c> 为中心）。</param>
        /// <param name="resolved">实际采用的方向。<paramref name="direction"/> 非
        /// <see cref="UIPopupDirection.Auto"/> 时等于它本身；为 <c>Auto</c> 时是自动选中的那个——
        /// 要按方向翻转箭头或播不同动画时读它。</param>
        /// <param name="offset">主轴方向上的间隙（像素），默认 0。参与「放不放得下」的判定，
        /// 即一个较大的间隙本身就可能触发 <see cref="UIPopupDirection.Auto"/> 翻转。</param>
        /// <returns>弹窗 pivot 应落在的屏幕像素坐标。</returns>
        public static Vector2 CalculatePosition(
            in Rect anchorScreenRect,
            in Vector2 popupSize,
            UIPopupDirection direction,
            UIPopupAlign align,
            in Rect screenRect,
            in Vector2 popupPivot,
            out UIPopupDirection resolved,
            float offset = 0f)
        {
            // 退化尺寸按 0 处理而非抛异常（对照 UISafeArea.CalculateAnchors 的除法守卫）：
            // 弹窗在创建中途被量到 0 尺寸是常见情形，不该让定位把它升级成异常
            float width = popupSize.x > 0f ? popupSize.x : 0f;
            float height = popupSize.y > 0f ? popupSize.y : 0f;

            resolved = direction == UIPopupDirection.Auto
                ? ResolveAuto(anchorScreenRect, width, height, screenRect, offset)
                : direction;

            ComputeTarget(anchorScreenRect, width, height, resolved, align, offset,
                out float x, out float y);

            // 两个轴都收敛。只用一侧收敛时，「锚点贴屏幕左缘 + 弹窗比锚点宽」会把弹窗推出屏幕
            x = ClampAxis(x, width, screenRect.xMin, screenRect.xMax);
            y = ClampAxis(y, height, screenRect.yMin, screenRect.yMax);

            return new Vector2(x + popupPivot.x * width, y + popupPivot.y * height);
        }

        /// <summary>
        /// 取 <see cref="RectTransform"/> 在屏幕像素坐标下的矩形。
        /// <para>四个角各做一次 <c>TransformPoint</c> 后取下界/上界，<b>无缓冲区、零分配</b>；
        /// 锚点被旋转时得到的是外接矩形——对定位而言这正是想要的。</para>
        /// <para><b>前置条件</b>：画布为 <c>ScreenSpaceOverlay</c>（本框架强制如此），该模式下画布世界空间
        /// 即屏幕像素。其它渲染模式下返回值不是屏幕坐标。</para>
        /// </summary>
        /// <param name="anchor">锚点元素的矩形组件。</param>
        /// <returns>屏幕像素矩形；<paramref name="anchor"/> 为 <c>null</c> 时返回零矩形（不抛异常）。</returns>
        public static Rect GetScreenRect(RectTransform anchor)
        {
            // 空引用返回零矩形而非抛异常：本类是几何工具，调用点常在销毁边缘
            if (anchor == null)
                return default;

            Rect local = anchor.rect;

            Vector3 bottomLeft = anchor.TransformPoint(new Vector3(local.xMin, local.yMin, 0f));
            Vector3 topRight = anchor.TransformPoint(new Vector3(local.xMax, local.yMax, 0f));
            Vector3 topLeft = anchor.TransformPoint(new Vector3(local.xMin, local.yMax, 0f));
            Vector3 bottomRight = anchor.TransformPoint(new Vector3(local.xMax, local.yMin, 0f));

            float xMin = Mathf.Min(Mathf.Min(bottomLeft.x, topRight.x), Mathf.Min(topLeft.x, bottomRight.x));
            float xMax = Mathf.Max(Mathf.Max(bottomLeft.x, topRight.x), Mathf.Max(topLeft.x, bottomRight.x));
            float yMin = Mathf.Min(Mathf.Min(bottomLeft.y, topRight.y), Mathf.Min(topLeft.y, bottomRight.y));
            float yMax = Mathf.Max(Mathf.Max(bottomLeft.y, topRight.y), Mathf.Max(topLeft.y, bottomRight.y));

            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        #endregion

        #region Private — Resolution

        /// <summary>
        /// 按 <paramref name="align"/> 与方向算出弹窗左下角的初始位置（尚未收敛进屏幕）。
        /// </summary>
        private static void ComputeTarget(in Rect anchor, float width, float height,
            UIPopupDirection direction, UIPopupAlign align, float offset,
            out float x, out float y)
        {
            switch (direction)
            {
                case UIPopupDirection.Up:
                    y = anchor.yMax + offset;
                    x = AlignOnAxis(anchor.xMin, anchor.xMax, width, align);
                    break;

                case UIPopupDirection.Left:
                    x = anchor.xMin - offset - width;
                    y = AlignOnAxis(anchor.yMin, anchor.yMax, height, align);
                    break;

                case UIPopupDirection.Right:
                    x = anchor.xMax + offset;
                    y = AlignOnAxis(anchor.yMin, anchor.yMax, height, align);
                    break;

                default:
                    // Down，以及 Auto 在极端情形下落到 Down 的分支
                    y = anchor.yMin - offset - height;
                    x = AlignOnAxis(anchor.xMin, anchor.xMax, width, align);
                    break;
            }
        }

        /// <summary>
        /// 副轴对齐：把长度 <paramref name="size"/> 的弹窗摆在 <paramref name="min"/>~<paramref name="max"/>
        /// 之间的起点 / 中点 / 终点。
        /// </summary>
        private static float AlignOnAxis(float min, float max, float size, UIPopupAlign align)
        {
            switch (align)
            {
                case UIPopupAlign.Center:
                    return (min + max) * 0.5f - size * 0.5f;

                case UIPopupAlign.End:
                    return max - size;

                default:
                    return min;
            }
        }

        /// <summary>
        /// <see cref="UIPopupDirection.Auto"/> 的选向：按 Down / Up / Right / Left 取第一个放得下的；
        /// 四个都放不下时取可用空间最大的那个。
        /// </summary>
        private static UIPopupDirection ResolveAuto(in Rect anchor, float width, float height,
            in Rect screen, float offset)
        {
            if (anchor.yMin - offset - height >= screen.yMin)
                return UIPopupDirection.Down;

            if (anchor.yMax + offset + height <= screen.yMax)
                return UIPopupDirection.Up;

            if (anchor.xMax + offset + width <= screen.xMax)
                return UIPopupDirection.Right;

            if (anchor.xMin - offset - width >= screen.xMin)
                return UIPopupDirection.Left;

            // 四向都放不下（弹窗比可用区域还大）：取可用空间最大的方向，由收敛把它贴到边上。
            // 定序是确定的——同分时先出现的胜出，故结果可复现
            float best = anchor.yMin - screen.yMin - offset;
            UIPopupDirection pick = UIPopupDirection.Down;

            float up = screen.yMax - anchor.yMax - offset;
            if (up > best)
            {
                best = up;
                pick = UIPopupDirection.Up;
            }

            float right = screen.xMax - anchor.xMax - offset;
            if (right > best)
            {
                best = right;
                pick = UIPopupDirection.Right;
            }

            float left = anchor.xMin - screen.xMin - offset;
            if (left > best)
                pick = UIPopupDirection.Left;

            return pick;
        }

        /// <summary>
        /// 把长度为 <paramref name="size"/> 的弹窗沿一根轴收敛进 <paramref name="min"/>~<paramref name="max"/>。
        /// </summary>
        private static float ClampAxis(float position, float size, float min, float max)
        {
            // 弹窗在该轴上比可用区域还大：贴低边，并在文档里如实写明「无法保证完整可见」。
            // 不能改用 Mathf.Clamp：它先判 value < min 再判 value > max，
            // 当上界 (max - size) 因弹窗过大而小于 min 时，返回值取决于输入值，语义不可靠
            if (size >= max - min)
                return min;

            if (position < min)
                return min;

            if (position + size > max)
                return max - size;

            return position;
        }

        #endregion
    }
}
