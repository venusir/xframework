using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 锁定「行类型用**显式接口实现**满足 <see cref="IConfigRow{TKey}"/>」这条接缝形状。
    /// <para>为什么需要这个守卫：外部配置表工具（Luban 等）生成的行类型把主键暴露为
    /// <c>public readonly</c> <b>字段</b>——字段不实现接口属性，而 <c>Id</c> 这个成员名已被生成代码占用，
    /// 所以补接口时唯一可行的写法是显式接口实现。包内 README 的「方式三」把这套写法当使用方契约教出去，
    /// 但框架侧没有任何东西锁住它：若 <see cref="ConfigTypeHelper"/> 哪天改成从**具体类型**上取
    /// <c>Id</c>（一个看起来很自然的「简化」），文档教的那条路就断了。<b>已实测</b>：把那处临时改成
    /// 「取具体类型的 <c>Id</c> 属性」后，本文件 6 例中 5 例当场变红（构造期抛
    /// <see cref="ConfigException"/>）；而既有用例全绿——它们的主键都是真属性，这正是本文件补上的差异。</para>
    /// <para>纯 C# 无依赖：不触碰任何静态门面，故无需复位。</para>
    /// </summary>
    class ExplicitInterfaceRowTests
    {
        #region Fixtures — 复刻生成物的形状

        /// <summary>
        /// 复刻生成物：<c>partial class</c> + <c>public readonly</c> 主键字段 + **只有带参构造**。
        /// <para>刻意不给无参构造——生成物就是这样，而 <see cref="ConfigTable{T}"/> 的约束里没有 <c>new()</c>。</para>
        /// </summary>
        private partial class GeneratedShape
        {
            public readonly int Id;
            public readonly string Name;

            public GeneratedShape(int id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        /// <summary>接缝：显式接口实现，与生成的同名字段不冲突。</summary>
        private partial class GeneratedShape : IConfigRow<int>
        {
            int IConfigRow<int>.Id => Id;
        }

        /// <summary>多键表：主键是 <see cref="ValueTuple"/>。</summary>
        private partial class CompositeShape
        {
            public readonly int SkillId;
            public readonly int Level;

            public CompositeShape(int skillId, int level)
            {
                SkillId = skillId;
                Level = level;
            }
        }

        /// <summary>
        /// 接缝（复合键）。<b>元组元素名必须与接口声明逐字一致</b>——写成
        /// <c>IConfigRow&lt;(int skillId, int level)&gt;</c> 而实现写 <c>(int, int)</c> 会触发 CS0540，
        /// 这是探针第一次编译就撞出来的坑。
        /// </summary>
        private partial class CompositeShape : IConfigRow<(int, int)>
        {
            (int, int) IConfigRow<(int, int)>.Id => (SkillId, Level);
        }

        #endregion

        #region 显式接口实现

        [Test]
        public void ExplicitInterfaceKey_ConstructsAndQueries()
        {
            var table = new ConfigTable<GeneratedShape>(new Dictionary<int, GeneratedShape>
            {
                [1001] = new GeneratedShape(1001, "Sword"),
            });

            Assert.AreEqual(1, table.Count);
            Assert.AreEqual("Sword", table.Get(1001).Name);
        }

        [Test]
        public void ExplicitInterfaceKey_KeyTypeIsExtractedFromTheInterface()
        {
            Assert.AreEqual(typeof(int), ConfigTypeHelper.GetKeyType(typeof(GeneratedShape)),
                "主键类型必须从 IConfigRow<TKey> 的泛型实参取——显式实现下具体类型上没有可用的 Id 成员");
        }

        [Test]
        public void ExplicitInterfaceKey_IsReadableThroughTheInterfacePropertyInfo()
        {
            var row = new GeneratedShape(1001, "Sword");
            var idProp = typeof(IConfigRow<int>).GetProperty("Id");

            Assert.IsNotNull(idProp, "IConfigRow<TKey> 必须提供 Id 成员");
            Assert.AreEqual(1001, idProp.GetValue(row),
                "经**接口**的 PropertyInfo 读得到显式实现的值——README 的反射批注册示例依赖这一点；" +
                "从具体类型上取（typeof(行类型).GetProperty(\"Id\")）则取不到");
        }

        [Test]
        public void ExplicitInterfaceKey_KeyTypeMismatch_StillThrows()
        {
            var ex = Assert.Throws<ConfigException>(() =>
                new ConfigTable<GeneratedShape>(new Dictionary<string, GeneratedShape>
                {
                    ["1001"] = new GeneratedShape(1001, "Sword"),
                }));

            StringAssert.Contains("declares 'Int32' keys", ex.Message,
                "显式实现不得让字典校验放宽——键类型不符仍须早失败并报出真实键类型");
        }

        #endregion

        #region 复合键与构造约束

        [Test]
        public void CompositeKey_ValueTuple_ConstructsAndQueries()
        {
            var table = new ConfigTable<CompositeShape>(new Dictionary<(int, int), CompositeShape>
            {
                [(1001, 5)] = new CompositeShape(1001, 5),
            });

            Assert.AreEqual(typeof((int, int)), ConfigTypeHelper.GetKeyType(typeof(CompositeShape)));
            Assert.AreEqual(1001, table.Get((1001, 5)).SkillId);
        }

        [Test]
        public void RowWithoutDefaultCtor_IsAcceptedByConfigTable()
        {
            Assert.IsNull(typeof(GeneratedShape).GetConstructor(Type.EmptyTypes),
                "本 fixture 刻意不给无参构造（复刻生成物形状）");

            // 这段能编过本身就是守卫：ConfigTable<T> / RegisterTable<T> 的约束里没有 new()。
            // 哪天给它加了 new()，本文件当场编译不过——而 Preload 路径本就有 new()，两者刻意不同。
            var table = new ConfigTable<GeneratedShape>(new Dictionary<int, GeneratedShape>
            {
                [1] = new GeneratedShape(1, "Sword"),
            });

            Assert.AreEqual(1, table.Count);
        }

        #endregion
    }
}
