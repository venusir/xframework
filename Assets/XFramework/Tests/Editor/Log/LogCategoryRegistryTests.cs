using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using XFramework.XLog;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 分类注册表：驻留（同名同一个状态对象）、未注册句柄的合法呈现、内置分类表的完整性。
    /// <para><b>驻留为什么要测</b>：档位存在被引用的状态对象里，「同名 = 同一对象」正是
    /// 「<c>SetCategoryLevel</c> 对已复制出去的句柄立即生效」的前提。前者若破，后者静默失效。</para>
    /// </summary>
    [TestFixture]
    public class LogCategoryRegistryTests
    {
        #region 驻留与未注册

        [Test]
        public void Get_SameName_ReturnsSameCategory()
        {
            LogCategory first = LogCategory.Get("RegistryTest.Same");
            LogCategory second = LogCategory.Get("RegistryTest.Same");

            Assert.IsTrue(first.IsValid);
            Assert.AreEqual(first, second);
            Assert.IsTrue(first == second);
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }

        [Test]
        public void Get_NullOrWhitespace_ReturnsUnregistered()
        {
            Assert.IsFalse(LogCategory.Get(null).IsValid);
            Assert.IsFalse(LogCategory.Get("   ").IsValid);
            Assert.AreEqual("Unregistered", LogCategory.Get(null).Name);
        }

        [Test]
        public void Default_IsValidFalse_AndToStringIsSafe()
        {
            LogCategory unregistered = default;

            Assert.IsFalse(unregistered.IsValid);
            Assert.AreEqual("Unregistered", unregistered.ToString());
        }

        [Test]
        public void Get_NewName_IncrementsRegisteredCount()
        {
            int before = LogCategory.RegisteredCount;
            LogCategory.Get("RegistryTest.Counted" + Guid.NewGuid().ToString("N"));
            Assert.AreEqual(before + 1, LogCategory.RegisteredCount);
        }

        #endregion

        #region 内置分类表

        /// <summary>
        /// 内置分类必须全部合法、名字唯一，且与字段名一致（名字是唯一真相，字段名只是取用入口）。
        /// </summary>
        [Test]
        public void LogCategories_AllValid_WithUniqueNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var failures = new List<string>();

            foreach (FieldInfo field in typeof(LogCategories).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var category = (LogCategory)field.GetValue(null);

                if (!category.IsValid)
                    failures.Add($"{field.Name} 未注册");
                else if (!string.Equals(category.Name, field.Name, StringComparison.Ordinal))
                    failures.Add($"{field.Name} 的名字是 '{category.Name}'，与字段名不一致");
                else if (!names.Add(category.Name))
                    failures.Add($"{field.Name} 的名字 '{category.Name}' 重复");
            }

            Assert.IsEmpty(failures, string.Join("\n  ", failures));
        }

        #endregion
    }
}
