using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 逻辑锁服务节点。作为 <see cref="LeafNode"/> 挂载到节点树中，提供基于 lockSubject、lockType、lock 三要素的锁管理能力。
    /// <para>游戏工程自行定义锁类型（如 Update、Input、Display 等），通过 int 标识。</para>
    /// <para>其他节点通过 <see cref="BaseNode.Get{T}"/> 获取此服务。</para>
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
            _service = new LockService();
        }

        protected override void OnDestroy()
        {
            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }
            base.OnDestroy();
        }

        #endregion

        #region ILockNode Implementation

        public LockHandle Acquire(int lockType, object lockObj)
            => _service.Acquire(lockType, lockObj);

        public LockHandle Acquire(object lockSubject, int lockType, object lockObj)
            => _service.Acquire(lockSubject, lockType, lockObj);

        public void Release(int lockType, object lockObj)
            => _service.Release(lockType, lockObj);

        public void Release(object lockSubject, int lockType, object lockObj)
            => _service.Release(lockSubject, lockType, lockObj);

        public bool IsLocked(int lockType)
            => _service.IsLocked(lockType);

        public bool IsLocked(object lockSubject, int lockType)
            => _service.IsLocked(lockSubject, lockType);

        public int GetLockCount(int lockType)
            => _service.GetLockCount(lockType);

        public IReadOnlyList<object> GetLockObjects(int lockType)
            => _service.GetLockObjects(lockType);

        public event Action<object, int, object> OnLocked
        {
            add => _service.OnLocked += value;
            remove => _service.OnLocked -= value;
        }

        public event Action<object, int, object> OnUnlocked
        {
            add => _service.OnUnlocked += value;
            remove => _service.OnUnlocked -= value;
        }

        #endregion
    }
}
