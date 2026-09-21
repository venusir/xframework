namespace XFramework.XSettings
{
    /// <summary>
    /// 设置载荷校验钩子：把不可信的数据校正回合法范围。
    /// <para><b>解决什么：</b>持久层里的值不能当作可信——玩家手改过 JSON、磁盘位翻转、
    /// 迁移实现写错了字段、<c>JsonUtility</c> 给新增字段填了类型默认值（新加的
    /// <c>int range = 3</c> 在旧存档上会变成 <c>0</c>）。这些都会让越界值静默进入内存，
    /// 然后在下游某处以更难查的方式发作。</para>
    /// <para><b>何时被调用：</b><c>Load</c> / <c>LoadAsync</c>、<c>Reset</c>、以及 <c>Apply</c>——
    /// 即注册之后的每一条产出当前设置对象的路径。迁移在格式版本化路径上先于本钩子执行，
    /// 故迁移的产出同样被校验。全部发生在订阅者被通知之前，因此校验过程中写值不会产生多余通知。</para>
    /// <para><b>不覆盖构造期：</b>管理器构造时那次加载/建默认值发生在任何钩子注册<b>之前</b>
    /// （属性只能在拿到实例后才设），故那份数据未经校验。补救是一行：
    /// <c>SetValidator(...)</c> 之后调用一次 <c>Load</c>。这与 <see cref="ISettingsMigrator{T}"/>
    /// 的处境相同，也是同一个理由。</para>
    /// <para><b>不覆盖字段写入：</b>经 <see cref="SettingRef{T,TField}"/> 写入的值不经过本钩子——
    /// 那是进程内的显式赋值，可信且每帧可能发生。需要逐次拦截请在写入侧自己做。</para>
    /// <para>由 <see cref="ISettingsManager{T}.Validator"/> 或门面
    /// <see cref="SettingsManager.SetValidator{T}"/> 注册。为 <c>null</c>（默认）时零行为。</para>
    /// </summary>
    /// <typeparam name="T">设置对象类型。</typeparam>
    public interface ISettingsValidator<T> where T : class, new()
    {
        /// <summary>
        /// 校验并<b>就地校正</b> <paramref name="settings"/>。
        /// <para>实现应当钳制越界值、修复非法组合、把 <c>null</c> 容器换成空容器——
        /// 而不是抛异常：本模块的既有取舍是「损坏的配置不该让游戏启动失败」
        /// （见 <see cref="JsonFileStore"/> 的失败语义）。</para>
        /// <para>本方法不返回值，因为纠正后的结果就是对象本身；没有「校验失败」这一分支——
        /// 失败的含义在设置场景下只能是「回退到某个合法值」，而那正是本方法要做的事。</para>
        /// </summary>
        /// <param name="settings">待校验的设置对象。</param>
        void Validate(T settings);
    }
}
