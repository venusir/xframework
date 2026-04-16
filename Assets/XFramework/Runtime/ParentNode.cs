using System.Collections.Generic;
using UnityEngine;

namespace XFramework
{
    public abstract class ParentNode : BaseNode
    {
        public int ChildCount => children.Count;

        public BaseNode this[int index] => children[index];

        protected List<BaseNode> children;

        internal void AddChild(BaseNode node)
        {
            if (node != null && !children.Contains(node))
            {
                children.Add(node);
                node.SetParent(this);
                OnChildAdded(node);
            }
        }

        internal void RemoveChild(BaseNode node)
        {
            if (node != null && children.Contains(node))
            {
                children.Remove(node);
                OnChildRemoved(node);
                node.DestroyInternal();
            }
        }

        #region override
        protected virtual void OnChildAdded(BaseNode node) { }

        protected virtual void OnChildRemoved(BaseNode node) { }

        internal sealed override void AwakeInternal()
        {
            base.AwakeInternal();

            children = new List<BaseNode>();
        }

        internal sealed override void DestroyInternal()
        {
            foreach (var child in children)
            {
                child.DestroyInternal();
            }
            children.Clear();
            children = null;

            base.DestroyInternal();
        }
        #endregion
    }
}
