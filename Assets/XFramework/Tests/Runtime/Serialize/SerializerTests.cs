using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSerialize;

namespace XFramework.XSerialize.Tests
{
    /// <summary>
    /// 序列化门面的注册语义：内置注册与使用方注册撞名时**保留使用方的**并告警（不再静默覆盖），
    /// 以及 <c>Register</c> 本身的「同名覆盖」承诺。
    /// <para><b>夹具纪律</b>：<c>Serializer</c> 是静态门面，且 <c>AutoInit</c> 已在进入播放时注册过内置项——
    /// 每个用例先 <c>Shutdown()</c> 复位（清空 + 复位初始化标志），TearDown 再 <c>Initialize()</c>
    /// 把内置项还给后续用例。</para>
    /// </summary>
    class SerializerTests
    {
        #region Test Doubles

        private sealed class FakeSerializer : ISerializer
        {
            public FakeSerializer(string format)
            {
                Format = format;
            }

            public string Format { get; }

            public byte[] Serialize(object obj, Type type) => Array.Empty<byte>();

            public object Deserialize(byte[] data, Type type) => null;
        }

        #endregion

        #region Setup / Teardown

        [SetUp]
        public void SetUp()
        {
            Serializer.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            Serializer.Shutdown();
            Serializer.Initialize();   // 把内置项还给后续用例
        }

        #endregion

        #region 注册语义

        [Test]
        public void Initialize_RegistersBuiltins()
        {
            Serializer.Initialize();

            Assert.AreEqual("json", Serializer.Get("json").Format);
            Assert.AreEqual("json-utility", Serializer.Get("json-utility").Format);
        }

        [Test]
        public void Initialize_WithPreRegisteredFormat_KeepsUserRegistrationAndWarns()
        {
            var mine = new FakeSerializer("json");
            Serializer.Register(mine);

            LogAssert.Expect(LogType.Warning, new Regex(@"\[Serialize\] 已存在 format = 'json' 的注册"));

            Serializer.Initialize();

            Assert.AreSame(mine, Serializer.Get("json"),
                "使用方在 Initialize 之前注册的同名序列化器必须被保留——直接覆盖会让那次注册静默失效");
            Assert.IsNotNull(Serializer.Get("json-utility"), "未被占用的内置项照常注册");
        }

        [Test]
        public void Register_SameFormat_Overwrites()
        {
            Serializer.Initialize();
            var mine = new FakeSerializer("json");

            Serializer.Register(mine);   // 显式注册即覆盖（文档承诺「同名覆盖」）

            Assert.AreSame(mine, Serializer.Get("json"), "Register 是显式动作，覆盖是它的承诺");
        }

        #endregion
    }
}
