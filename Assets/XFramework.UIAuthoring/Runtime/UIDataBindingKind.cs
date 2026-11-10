namespace XFramework.XUI.Authoring
{
    /// <summary>
    /// 一条数据绑定的目标种类。
    /// <para>显式编号：序列化在定义资产里，增删必须保持既有值不变。</para>
    /// </summary>
    public enum UIDataBindingKind
    {
        /// <summary>文本：<c>TMP_Text</c> 优先，否则 UGUI <c>Text</c>；期望 <c>IReactiveProperty&lt;string&gt;</c>。</summary>
        Text = 0,

        /// <summary>图片：<c>Image.sprite</c>；期望 <c>IReactiveProperty&lt;Sprite&gt;</c>。</summary>
        Sprite = 1,

        /// <summary>图片颜色：<c>Image.color</c>；期望 <c>IReactiveProperty&lt;Color&gt;</c>。</summary>
        Color = 2,

        /// <summary>图片填充量：<c>Image.fillAmount</c>；期望 <c>IReactiveProperty&lt;float&gt;</c>。</summary>
        FillAmount = 3,

        /// <summary>滑条：<c>Slider.value</c>；期望 <c>IReactiveProperty&lt;float&gt;</c>；支持双向。</summary>
        Slider = 4,

        /// <summary>开关：<c>Toggle.isOn</c>；期望 <c>IReactiveProperty&lt;bool&gt;</c>；支持双向。</summary>
        Toggle = 5,

        /// <summary>节点显隐：<c>GameObject.SetActive</c>；期望 <c>IReactiveProperty&lt;bool&gt;</c>。</summary>
        Active = 6,
    }
}
