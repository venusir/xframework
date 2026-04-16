namespace XFramework
{
    public abstract class BaseNode
    {
        public int Depth { get; private set; }
        public bool Destroyed { get; private set; }
        public ParentNode Parent { get; private set; }

        public bool IsRoot => Parent == null;

        public void Awake()
        {
            AwakeInternal();
        }

        public void Destroy()
        {
            DestroyInternal();
        }

        internal void SetParent(ParentNode parent)
        {
            if (Parent != parent)
            {
                Parent = parent;
                Depth = parent?.Depth + 1 ?? 0;
            }
        }

        internal virtual void AwakeInternal()
        {
            Depth = 0;
            Parent = null;
            Destroyed = false;

            OnAwake();
        }

        internal virtual void DestroyInternal()
        {
            if (Destroyed) return;

            Depth = 0;
            Parent = null;
            Destroyed = true;

            OnDestroyed();
        }

        protected virtual void OnAwake() { }

        protected virtual void OnDestroyed() { }
    }
}
