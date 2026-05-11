using System;
using NUnit.Framework;
using XFramework.XCore;

namespace XFramework.XCore.Tests
{
    [TestFixture]
    public class EntityNodeTests
    {
        #region Test Helpers

        private interface ICustomService : IBaseNode
        {
            void DoSomething();
        }

        private sealed class CustomServiceNode : BaseNode, ICustomService
        {
            public bool DidSomething { get; private set; }
            public void DoSomething() => DidSomething = true;
        }

        private sealed class AnotherService : BaseNode { }

        private sealed class TestEntity : EntityNode
        {
            public new void Awake() => base.Awake();
            public new void Start() => base.Start();
            public new void Destroy() => base.Destroy();
        }

        #endregion

        #region AddNode

        [Test]
        public void AddNode_AddsTypeToCache()
        {
            var entity = CreateEntity();
            var service = entity.AddNode<CustomServiceNode>();
            Assert.IsNotNull(service);
            Assert.AreSame(service, entity.GetNode<CustomServiceNode>());
            entity.Destroy();
        }

        [Test]
        public void AddNode_DuplicateType_ReturnsExisting()
        {
            var entity = CreateEntity();
            var first = entity.AddNode<CustomServiceNode>();
            var second = entity.AddNode<CustomServiceNode>();
            Assert.AreSame(first, second);
            entity.Destroy();
        }

        [Test]
        public void AddNode_WithArg_PassesInitParam()
        {
            var entity = CreateEntity();
            var service = entity.AddNode<CustomServiceNode>("testArg");
            Assert.IsNotNull(service);
            entity.Destroy();
        }

        [Test]
        public void AddNode_ByType_Works()
        {
            var entity = CreateEntity();
            var node = entity.AddNode(typeof(CustomServiceNode));
            Assert.IsNotNull(node);
            Assert.IsInstanceOf<CustomServiceNode>(node);
            entity.Destroy();
        }

        [Test]
        public void AddNode_ByType_WithArg_Works()
        {
            var entity = CreateEntity();
            var node = entity.AddNode(typeof(CustomServiceNode), "arg");
            Assert.IsNotNull(node);
            entity.Destroy();
        }

        [Test]
        public void AddNode_ByType_NullType_Throws()
        {
            var entity = CreateEntity();
            Assert.Throws<ArgumentNullException>(() => entity.AddNode(null));
            entity.Destroy();
        }

        [Test]
        public void AddNode_ByType_NonBaseNode_Throws()
        {
            var entity = CreateEntity();
            Assert.Throws<ArgumentException>(() => entity.AddNode(typeof(object)));
            entity.Destroy();
        }

        #endregion

        #region GetNode

        [Test]
        public void GetNode_ReturnsFromCache()
        {
            var entity = CreateEntity();
            entity.AddNode<CustomServiceNode>();
            var found = entity.GetNode<CustomServiceNode>();
            Assert.IsNotNull(found);
            entity.Destroy();
        }

        [Test]
        public void GetNode_AutoCreate_WorksForConcreteType()
        {
            var entity = CreateEntity();
            var found = entity.GetNode<CustomServiceNode>(autoCreate: true);
            Assert.IsNotNull(found);
            entity.Destroy();
        }

        [Test]
        public void GetNode_AutoCreateFalse_ReturnsNullForMissing()
        {
            var entity = CreateEntity();
            var found = entity.GetNode<CustomServiceNode>(autoCreate: false);
            Assert.IsNull(found);
            entity.Destroy();
        }

        [Test]
        public void GetNode_InterfaceType_NoAutoCreate()
        {
            var entity = CreateEntity();
            // 接口类型无法自动创建，即使 autoCreate=true
            var found = entity.GetNode<ICustomService>(autoCreate: true);
            Assert.IsNull(found);
            entity.Destroy();
        }

        [Test]
        public void GetNode_Interface_ReturnsFromCache()
        {
            var entity = CreateEntity();
            entity.AddNode<CustomServiceNode>();
            var found = entity.GetNode<ICustomService>(autoCreate: false);
            Assert.IsNotNull(found);
            entity.Destroy();
        }

        #endregion

        #region RemoveNode

        [Test]
        public void RemoveNode_ByType_RemovesAndDestroys()
        {
            var entity = CreateEntity();
            var service = entity.AddNode<CustomServiceNode>();
            var result = entity.RemoveNode<CustomServiceNode>();
            Assert.IsTrue(result);
            Assert.IsNull(entity.GetNode<CustomServiceNode>(autoCreate: false));
            entity.Destroy();
        }

        [Test]
        public void RemoveNode_ByType_NotFound_ReturnsFalse()
        {
            var entity = CreateEntity();
            var result = entity.RemoveNode<CustomServiceNode>();
            Assert.IsFalse(result);
            entity.Destroy();
        }

        [Test]
        public void RemoveNode_ByInterfaceType_Works()
        {
            var entity = CreateEntity();
            entity.AddNode<CustomServiceNode>();
            var result = entity.RemoveNode<ICustomService>();
            Assert.IsTrue(result);
            entity.Destroy();
        }

        [Test]
        public void RemoveNode_ByRuntimeType_Works()
        {
            var entity = CreateEntity();
            entity.AddNode<CustomServiceNode>();
            var result = entity.RemoveNode(typeof(CustomServiceNode));
            Assert.IsTrue(result);
            entity.Destroy();
        }

        [Test]
        public void RemoveNode_ByInstance_Works()
        {
            var entity = CreateEntity();
            var service = entity.AddNode<CustomServiceNode>();
            var result = entity.RemoveNode(service);
            Assert.IsTrue(result);
            entity.Destroy();
        }

        [Test]
        public void RemoveNode_ByInstance_Null_ReturnsFalse()
        {
            var entity = CreateEntity();
            var result = entity.RemoveNode((BaseNode)null);
            Assert.IsFalse(result);
            entity.Destroy();
        }

        [Test]
        public void RemoveNode_ByRuntimeType_Null_ReturnsFalse()
        {
            var entity = CreateEntity();
            var result = entity.RemoveNode((Type)null);
            Assert.IsFalse(result);
            entity.Destroy();
        }

        #endregion

        #region Type Cache Lifecycle

        [Test]
        public void Destroy_ClearsTypeCache()
        {
            var entity = CreateEntity();
            entity.AddNode<CustomServiceNode>();
            entity.Destroy();

            // 销毁后不应再有缓存引用（新节点无法通过缓存找到）
            Assert.IsNull(entity.GetNode<CustomServiceNode>(autoCreate: false));
        }

        [Test]
        public void ChildDestroy_ClearsFromParentCache()
        {
            var entity = CreateEntity();
            var service = entity.AddNode<CustomServiceNode>();
            service.Destroy();

            // 子节点被销毁应从父节点缓存中自动移除
            Assert.IsNull(entity.GetNode<CustomServiceNode>(autoCreate: false));
            entity.Destroy();
        }

        #endregion

        #region Private Helpers

        private static TestEntity CreateEntity()
        {
            var entity = new TestEntity();
            entity.Awake();
            return entity;
        }

        #endregion
    }
}