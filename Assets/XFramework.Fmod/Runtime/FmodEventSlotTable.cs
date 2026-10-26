using XFramework.XAudio;

namespace XFramework.XAudio.Fmod
{
    /// <summary>
    /// provider 的槽位表：下标 + 代际，零 GC。provider 句柄的 <see cref="AudioProviderHandle.Id"/> 存槽位下标，
    /// <see cref="AudioProviderHandle.Generation"/> 存代际；代际从 <c>1</c> 起，<c>0</c> 保留给 default。
    /// </summary>
    internal sealed class FmodEventSlotTable
    {
        #region Nested

        private struct Slot
        {
            internal int Generation;
            internal int BackendId;
            internal bool Used;
        }

        #endregion

        #region Private Fields

        private readonly Slot[] _slots;

        #endregion

        #region Constructors

        internal FmodEventSlotTable(int capacity)
        {
            _slots = new Slot[capacity < 1 ? 1 : capacity];
        }

        #endregion

        #region Internal

        /// <summary>槽位容量。</summary>
        internal int Capacity => _slots.Length;

        /// <summary>预占一个空槽；容量满返回 <c>false</c>。</summary>
        internal bool TryReserve(out int slotIndex, out AudioProviderHandle handle)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Used)
                    continue;

                int generation = _slots[i].Generation + 1;
                if (generation == 0)
                    generation = 1;

                _slots[i].Generation = generation;
                _slots[i].BackendId = 0;
                _slots[i].Used = true;

                slotIndex = i;
                handle = new AudioProviderHandle(i, generation);
                return true;
            }

            slotIndex = -1;
            handle = default;
            return false;
        }

        /// <summary>按句柄取槽位与 backend 实例 id；句柄无效/过期返回 <c>false</c>。</summary>
        internal bool TryGet(AudioProviderHandle handle, out int slotIndex, out int backendId)
        {
            slotIndex = -1;
            backendId = 0;

            if (handle.Generation == 0 || handle.Id < 0 || handle.Id >= _slots.Length)
                return false;

            if (!_slots[handle.Id].Used || _slots[handle.Id].Generation != handle.Generation)
                return false;

            slotIndex = handle.Id;
            backendId = _slots[handle.Id].BackendId;
            return true;
        }

        /// <summary>记录槽位对应的 backend 实例 id。</summary>
        internal void SetBackendId(int slotIndex, int backendId)
        {
            if (slotIndex >= 0 && slotIndex < _slots.Length)
                _slots[slotIndex].BackendId = backendId;
        }

        /// <summary>释放槽位（不触碰 backend）；代际保留，下次预占递增。</summary>
        internal void ReleaseSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Length)
                return;

            _slots[slotIndex].Used = false;
            _slots[slotIndex].BackendId = 0;
        }

        /// <summary>取仍在使用中的槽位 backend 实例 id（Dispose 用）。</summary>
        internal bool TryGetLive(int slotIndex, out int backendId)
        {
            backendId = 0;

            if (slotIndex < 0 || slotIndex >= _slots.Length || !_slots[slotIndex].Used)
                return false;

            backendId = _slots[slotIndex].BackendId;
            return backendId != 0;
        }

        /// <summary>把所有槽位置为空闲；代际保留。</summary>
        internal void Clear()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Used = false;
                _slots[i].BackendId = 0;
            }
        }

        #endregion
    }
}
