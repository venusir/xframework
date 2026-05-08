using System;

namespace XFramework
{
    /// <summary>
    /// 逻辑锁服务节点。作为 <see cref="LeafNode"/> 挂载到节点树中，提供按类型引用计数的锁管理能力。
    /// <para>游戏工程自行定义锁类型（如 Update、Input、Display 等），通过 int 标识。</para>
    /// <para>其他节点通过 <see cref="BaseNode.Get{T}"/> 获取此服务。</para>
    /// <para>内部委托到 <see cref="LockSystem"/> 的全局 <see cref="ILockService"/> 实例。</para>
    /// </summary>
    public class LockNode : LeafNode, ILockNode
    {
        #region Private Fields

        private ILockService _service;

        #endregion

        #region Lifecycle

        protected override void OnStart()
        {
            base.OnStart();
            _service = LockSystem.Instance;
        }

        protected override void OnDestroy()
        {
            _service = null;
            base.OnDestroy();
        }

        #endregion

        #region ILockNode Implementation

        public LockHandle Acquire(int lockType, object owner)
            => _service.Acquire(lockType, owner);

        public void Release(int lockType, object owner)
            => _service.Release(lockType, owner);

        public bool IsLocked(int lockType)
            => _service.IsLocked(lockType);

        public int GetLockCount(int lockType)
            => _service.GetLockCount(lockType);

        public event Action<int, object> OnLocked
        {
            add => _service.OnLocked += value;
            remove => _service.OnLocked -= value;
        }

        public event Action<int, object> OnUnlocked
        {
            add => _service.OnUnlocked += value;
            remove => _service.OnUnlocked -= value;
        }

        #endregion
    }
}


