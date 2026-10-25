using System;
using System.Collections.Generic;
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

        #region 会话复位（R4-PRB-18）

        [Test]
        public void ResetForNewSession_ClearsCustomFormatRegisteredBeforeHook()
        {
            Serializer.Register(new FakeSerializer("pre-session"));

            Serializer.ResetForNewSession();

            Assert.IsFalse(Serializer.TryGet("pre-session", out _),
                "ResetForNewSession 之前的注册会被清掉（README / XML 已写明）");
        }

        [Test]
        public void ResetForNewSession_RestoresBuiltinsImmediately()
        {
            Serializer.Shutdown();

            Serializer.ResetForNewSession();

            Assert.AreEqual("json", Serializer.Get("json").Format);
            Assert.AreEqual("json-utility", Serializer.Get("json-utility").Format);
            Assert.IsNotNull(Serializer.Default);
        }

        [Test]
        public void ResetForNewSession_ClearsUserOverrideOfBuiltinFormat()
        {
            Serializer.Register(new FakeSerializer("json"));

            Serializer.ResetForNewSession();

            Assert.IsFalse(Serializer.Get("json") is FakeSerializer,
                "会话复位后同名内置覆盖也被清掉，内置实现恢复");
            Assert.AreEqual("json", Serializer.Default.Format);
        }

        #endregion

        #region 失败面（SER-005）

        [Test]
        public void Default_Uninitialized_ThrowsInvalidOperationException()
        {
            // SetUp 已 Shutdown；未注册默认格式时取 Default 必须是 IOE，而不是 KeyNotFound
            Assert.Throws<InvalidOperationException>(() => _ = Serializer.Default);
        }

        [Test]
        public void Default_AfterUnregisterJson_ThrowsInvalidOperationException()
        {
            Serializer.Initialize();
            Assert.IsTrue(Serializer.Unregister("json"));

            Assert.Throws<InvalidOperationException>(() => _ = Serializer.Default,
                "注销内置 json 后 Default 不可用；重新 Initialize 是 no-op，不会恢复");
        }

        [Test]
        public void TryGet_UnregisteredFormat_ReturnsFalse()
        {
            Serializer.Initialize();

            Assert.IsFalse(Serializer.TryGet("nope", out var serializer), "未注册格式经 TryGet 返回 false");
            Assert.IsNull(serializer);
        }

        [Test]
        public void TryGet_NullFormat_ThrowsArgumentNullException()
        {
            Serializer.Initialize();

            Assert.Throws<ArgumentNullException>(() => Serializer.TryGet(null, out _),
                "「不抛」只针对未注册；null 格式仍抛 ANE（SER-004）");
        }

        [Test]
        public void Unregister_MissingFormat_ReturnsFalse()
        {
            Serializer.Initialize();

            Assert.IsFalse(Serializer.Unregister("nope"), "未注册格式的 Unregister 返回 false，不抛");
        }

        [Test]
        public void Shutdown_ThenGet_ThrowsKeyNotFound()
        {
            Serializer.Initialize();
            Serializer.Shutdown();

            Assert.Throws<KeyNotFoundException>(() => Serializer.Get("json"),
                "Shutdown 之后连内置格式都没有，Get 全抛");
            Assert.Throws<InvalidOperationException>(() => _ = Serializer.Default);
        }

        #endregion
    }
}
