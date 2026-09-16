using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using XFramework.XMessage.Internal;
using XFramework.XReactive;

namespace XFramework.XSettings
{
    /// <summary>
    /// 设置字段句柄：把设置对象（或嵌套子对象）里的一个字段包装成可读可写、可订阅的响应式视图。
    /// <para><b>定位：</b>设置类保持纯 POCO（<c>public float MasterVolume = 1f;</c>），
    /// 句柄另行声明为静态字段，两者通过表达式关联。这样落盘 JSON 不含任何框架类型，
    /// 设置类可随时搬走，而 UI 绑定仍能复用 <c>BindToSlider</c> 等现成扩展方法
    /// （本类实现 <see cref="IReactiveProperty{T}"/>）。</para>
    /// <para><b>自动跟随实例替换：</b>每次读写都经 <see cref="SettingsManager.Settings{T}"/> 取<b>当前</b>
    /// 设置实例，因此 <c>Load</c>/<c>Reset</c>/<c>Apply</c> 换掉实例后无需重新绑定。</para>
    /// <para><b>常驻、无生命周期：</b>刻意不实现 <see cref="IDisposable"/>——它通常声明为静态字段并活到进程结束，
    /// 若带释放语义，「面板关闭时 Dispose 掉 ViewModel」这类正常操作会连带废掉句柄。
    /// 取消订阅请释放 <see cref="Subscribe"/> 返回的句柄。</para>
    /// <para><b>写入契约：</b>直接改 POCO 字段<b>不会</b>通知也不会置脏；要通知必须经本类的
    /// <see cref="Value"/> 写入。这与本模块改造前的行为一致（当时直改字段同样不通知），
    /// 属于保留的已知限制。</para>
    /// <para><b>线程：</b>读写须在主线程。</para>
    /// </summary>
    /// <typeparam name="T">设置对象类型。</typeparam>
    /// <typeparam name="TField">字段类型。</typeparam>
    public sealed class SettingRef<T, TField> : IReactiveProperty<TField>, IReactivePropertyWriter<TField>
        where T : class, new()
    {
        #region Private Fields

        private readonly Func<T, TField> _getter;
        private readonly Action<T, TField> _setter;
        private readonly string _path;
        private readonly EventStream<TField> _changedStream = new();

        #endregion

        #region Constructors

        private SettingRef(Func<T, TField> getter, Action<T, TField> setter, string path)
        {
            _getter = getter;
            _setter = setter;
            _path = path;
        }

        #endregion

        #region Factory

        /// <summary>
        /// 解析字段选择器并创建句柄。由 <see cref="SettingsManager.Ref{T, TField}"/> 调用。
        /// <para><b>调用一次并缓存返回值</b>（典型做法是 <c>static readonly</c> 字段）：
        /// 编译表达式有成本，且每次调用都会新建句柄与事件流，不可放入每帧路径。</para>
        /// </summary>
        internal static SettingRef<T, TField> Create(Expression<Func<T, TField>> selector)
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            var body = StripConversion(selector.Body);

            // 叶子必须是字段:属性不会被 JsonUtility 序列化,用它做设置项会「改了但没存」,
            // 所以这里直接拒绝并说明原因,而不是留下一个静默失效的句柄
            if (body is not MemberExpression leaf || leaf.Member is not FieldInfo)
            {
                throw new ArgumentException(
                    $"[SettingsManager] Ref 的选择器必须以字段结尾（收到的表达式：'{selector}'）。" +
                    "设置项不能用属性——JsonUtility 只序列化字段，属性值不会落盘。",
                    nameof(selector));
            }

            // 中间路径段只要求可读,字段或属性皆可(如 s => s.Audio.MasterVolume 中 Audio 是字段,
            // 若它是只读属性也成立:我们只在其返回值上写叶子字段)
            var path = BuildPathAndValidate(body, selector.Parameters[0]);

            var sourceParam = Expression.Parameter(typeof(T), "s");
            var valueParam = Expression.Parameter(typeof(TField), "v");
            var target = Rebind(body, selector.Parameters[0], sourceParam);

            // 赋值表达式的类型是 TField,而 Action<,> 要求 void 体——Lambda<TDelegate> 会因此拒绝。
            // 用 Block 把赋值与一个 void 表达式串起来,使整个体的类型为 void
            var assignBody = Expression.Block(
                Expression.Assign(target, valueParam),
                Expression.Empty());

            return new SettingRef<T, TField>(
                selector.Compile(),
                Expression.Lambda<Action<T, TField>>(assignBody, sourceParam, valueParam).Compile(),
                path);
        }

        #endregion

        #region Value

        /// <summary>
        /// 读取当前值；写入时回写设置对象并通知所有订阅者。
        /// <para>写入会与设置对象中的<b>实时值</b>比较，相等则直接返回——去重基准不缓存，
        /// 因此不存在陈旧锚点。</para>
        /// </summary>
        /// <summary>
        /// 尝试写入值。设置句柄不持有释放语义（它通常活到进程结束），故永远返回 true。
        /// </summary>
        /// <param name="value">要写入的值。</param>
        /// <returns>恒为 true。</returns>
        public bool TryWriteValue(TField value)
        {
            Value = value;
            return true;
        }

        public TField Value
        {
            get => _getter(Current());

            set
            {
                var settings = Current();
                if (EqualityComparer<TField>.Default.Equals(_getter(settings), value))
                    return;

                _setter(settings, value);

                // 先置脏再通知:订阅者在回调里查 IsDirty 时应看到一致的状态
                SettingsManager.MarkDirty<T>();
                _changedStream.OnNext(value);
            }
        }

        #endregion

        #region Subscribe

        /// <summary>
        /// 订阅值变化。订阅时立即同步回调当前值（与 <c>ReactiveProperty&lt;T&gt;</c> 契约一致），
        /// 之后每次经 <see cref="Value"/> 写入且值确实改变时回调。
        /// </summary>
        /// <param name="onNext">值变化时的回调。</param>
        /// <returns>取消订阅的句柄。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="onNext"/> 为 <c>null</c> 时抛出。</exception>
        public IDisposable Subscribe(Action<TField> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            // 先注册再立即回调:与 ReactiveProperty 相同顺序,确保回调中的订阅不会丢失后续消息
            var handle = _changedStream.Subscribe(onNext);
            onNext(Value);
            return handle;
        }

        #endregion

        #region Object

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{nameof(SettingRef<T, TField>)}<{typeof(T).Name}, {typeof(TField).Name}>(\"{_path}\")";
        }

        #endregion

        #region Internal

        /// <summary>取当前设置实例。句柄不缓存实例，这正是它能自动跟随 Load/Reset/Apply 的原因。</summary>
        private static T Current()
        {
            return SettingsManager.Settings<T>();
        }

        /// <summary>剥掉表达式树外层可能存在的装箱/转换节点。</summary>
        private static Expression StripConversion(Expression node)
        {
            while (node is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
                node = unary.Operand;

            return node;
        }

        /// <summary>
        /// 自叶子向根遍历成员访问链，校验结构并拼出可读的字段路径（如 <c>Audio.MasterVolume</c>）。
        /// </summary>
        private static string BuildPathAndValidate(Expression leaf, ParameterExpression root)
        {
            var segments = new List<string>();
            var cursor = leaf;

            while (cursor is MemberExpression member)
            {
                if (member.Member is not FieldInfo && member.Member is not PropertyInfo)
                {
                    throw new ArgumentException(
                        $"[SettingsManager] Ref 的路径上含不支持的成员 '{member.Member.Name}'。", nameof(leaf));
                }

                segments.Insert(0, member.Member.Name);
                cursor = member.Expression;
            }

            // 链的根部必须正好是选择器参数:否则说明表达式中混入了方法调用、索引器或闭包捕获的常量,
            // 这些形状无法安全地重建写回表达式
            if (!ReferenceEquals(cursor, root))
            {
                throw new ArgumentException(
                    "[SettingsManager] Ref 的选择器必须是对设置对象自身成员的连续访问" +
                    "（不支持方法调用、索引器与闭包捕获）。", nameof(leaf));
            }

            return string.Join(".", segments);
        }

        /// <summary>
        /// 把成员访问链上的原参数替换成本次生成的参数，用于构造写回表达式。
        /// </summary>
        private static Expression Rebind(Expression node, Expression original, ParameterExpression replacement)
        {
            if (ReferenceEquals(node, original))
                return replacement;

            if (node is MemberExpression member)
                return Expression.MakeMemberAccess(Rebind(member.Expression, original, replacement), member.Member);

            throw new ArgumentException(
                "[SettingsManager] Ref 的选择器含有无法重建为写回表达式的节点。", nameof(node));
        }

        #endregion
    }
}
