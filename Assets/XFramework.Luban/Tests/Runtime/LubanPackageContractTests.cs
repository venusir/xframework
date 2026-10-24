using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace XFramework.XConfig.Luban.Tests
{
    /// <summary>
    /// 扩展包骨架与运行时契约的形状守卫。
    /// </summary>
    [TestFixture]
    public sealed class LubanPackageContractTests
    {
        [Test]
        public void AdapterAssembly_HasExpectedName()
        {
            Assert.That(typeof(ILubanConfigAdapter).Assembly.GetName().Name,
                Is.EqualTo("Venusy609.Xframework.Luban"));
        }

        [Test]
        public void AdapterInterface_ExposesExpectedMembers()
        {
            var type = typeof(ILubanConfigAdapter);

            var dataFileExtension = type.GetProperty("DataFileExtension");
            Assert.That(dataFileExtension, Is.Not.Null);
            Assert.That(dataFileExtension.PropertyType, Is.EqualTo(typeof(string)));
            Assert.That(dataFileExtension.CanRead, Is.True);
            Assert.That(dataFileExtension.CanWrite, Is.False);

            var tableNames = type.GetProperty("TableNames");
            Assert.That(tableNames, Is.Not.Null);
            Assert.That(tableNames.PropertyType, Is.EqualTo(typeof(IReadOnlyList<string>)));
            Assert.That(tableNames.CanWrite, Is.False);

            var createTables = type.GetMethod("CreateTables");
            Assert.That(createTables, Is.Not.Null);
            Assert.That(createTables.ReturnType, Is.EqualTo(typeof(object)));
            var createParameters = createTables.GetParameters();
            Assert.That(createParameters.Length, Is.EqualTo(1));
            Assert.That(createParameters[0].ParameterType, Is.EqualTo(typeof(Func<string, byte[]>)));

            var register = type.GetMethod("RegisterToConfigManager");
            Assert.That(register, Is.Not.Null);
            Assert.That(register.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(register.GetParameters().Length, Is.EqualTo(1));
            Assert.That(register.GetParameters()[0].ParameterType, Is.EqualTo(typeof(object)));

            var unregister = type.GetMethod("UnregisterFromConfigManager");
            Assert.That(unregister, Is.Not.Null);
            Assert.That(unregister.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(unregister.GetParameters().Length, Is.EqualTo(0));
        }

        [Test]
        public void DataLoadMode_HasExpectedValues()
        {
            Assert.That((int)LubanDataLoadMode.TextAsset, Is.EqualTo(0));
            Assert.That((int)LubanDataLoadMode.RawFile, Is.EqualTo(1));
        }

        [Test]
        public void Options_HaveExpectedDefaults()
        {
            var options = new LubanOptions();

            Assert.That(options.DataRoot, Is.EqualTo("configs"));
            Assert.That(options.LoadMode, Is.EqualTo(LubanDataLoadMode.TextAsset));
            Assert.That(options.LocationResolver, Is.Null);
        }

        [Test]
        public void InitReport_StoresValues()
        {
            var report = new LubanInitReport(0.5f, "loading");

            Assert.That(report.Progress, Is.EqualTo(0.5f));
            Assert.That(report.Description, Is.EqualTo("loading"));
        }

        [Test]
        public void Exception_PreservesMessageAndInner()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new LubanConfigException("outer", inner);

            Assert.That(ex.Message, Is.EqualTo("outer"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }
    }
}
