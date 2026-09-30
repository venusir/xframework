using System;
using NUnit.Framework;
using XFramework.XConfig;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="CsvLoader"/> 的解析路径测试。
    /// <para>走 <c>CsvLoader.ParseTable</c>（与资源加载解耦的那个内部入口），而不是门面或
    /// <c>IConfigLoader</c>——装载器此前**整体不可测**：测试进程里 <c>AssetManager</c> 从未初始化，
    /// 任何真实 Loader 路径都会在 <c>ConfigTextLoader</c> 抛错，而 <c>AssetHandle&lt;T&gt;</c> 只有一个
    /// internal 构造函数、必须包一个真的 YooAsset 句柄，替身造不出来。</para>
    /// </summary>
    [TestFixture]
    public class CsvLoaderTests
    {
        /// <summary>值类型行——README 的「struct vs class 行类型」表明确推荐 struct。</summary>
        [Serializable]
        private struct CsvStructRow : IConfigRow<int>
        {
            public int Id { get; set; }
            public string Name;
        }

        /// <summary>引用类型行——作为 struct 那条的对照。</summary>
        [Serializable]
        private sealed class CsvClassRow : IConfigRow<int>
        {
            public int Id { get; set; }
            public string Name;
        }

        [Test]
        public void ParseTable_MultipleRows_ClassRow_ParsesEveryRow()
        {
            // 对照组：引用类型行一直是对的（引用传给 object 形参不产生副本）
            var text = "Id,Name\n1,alpha\n2,beta\n";

            var table = CsvLoader.ParseTable<CsvClassRow, int>(text, "test.csv");

            Assert.AreEqual(2, table.Count);
            Assert.AreEqual("alpha", table.Get(1).Name);
            Assert.AreEqual("beta", table.Get(2).Name);
        }

        [Test]
        public void ParseTable_MultipleRows_StructRow_ParsesEveryRow()
        {
            // 复现用例：值类型行 + 多行 CSV。
            // 赋值走 SetMemberValue(..., object target) 反射，而 `var item = new T()` 对 struct 是
            // **值类型局部变量**——传进 object 形参会装箱，反射只改装箱副本，局部恒为 default(T)。
            // 于是第二行读到同一个默认键，撞 CsvLoader 自己的 Duplicate Id 检查。
            var text = "Id,Name\n1,alpha\n2,beta\n";

            var table = CsvLoader.ParseTable<CsvStructRow, int>(text, "test.csv");

            Assert.AreEqual(2, table.Count, "两行数据都该进表");
            Assert.AreEqual("alpha", table.Get(1).Name);
            Assert.AreEqual("beta", table.Get(2).Name);
        }

        [Test]
        public void ParseTable_SingleRow_StructRow_IsNotAllDefaults()
        {
            // 单行时不会撞 Duplicate Id——它**静默**产出一条全默认值的行（更隐蔽的那半）
            var text = "Id,Name\n7,gamma\n";

            var table = CsvLoader.ParseTable<CsvStructRow, int>(text, "test.csv");

            Assert.AreEqual(1, table.Count);
            Assert.AreEqual(7, table.GetAll()[0].Id, "Id 应是 7 而不是默认值 0");
            Assert.AreEqual("gamma", table.GetAll()[0].Name);
        }
    }
}
