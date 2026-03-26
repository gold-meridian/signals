using System.Numerics;
using System.Runtime.CompilerServices;

namespace Signals;

public interface ISparseSet {
    bool Has(int entityId);
    void Remove(int entityId);
    void EnsureCapacity(int capacity);
}

public sealed class SparseSet<T> : ISparseSet where T : struct {
    private int[] _sparse = Array.Empty<int>();
    private T[] _dense = Array.Empty<T>();
    private int[] _denseToSparse = Array.Empty<int>();
    private int _count;

    public int Count => _count;

    public bool Has(int entityId) => entityId < _sparse.Length && _sparse[entityId] != -1;

    public ref T Get(int entityId) => ref _dense[_sparse[entityId]];

    public void Set(int entityId, in T data) {
        if (entityId >= _sparse.Length) GrowSparse(entityId);

        int index = _sparse[entityId];
        if (index == -1) {
            index = _count++;
            if (index >= _dense.Length) GrowDense();
            _sparse[entityId] = index;
            _denseToSparse[index] = entityId;
        }
        _dense[index] = data;
    }

    public void Remove(int entityId) {
        if (!Has(entityId)) return;
        int index = _sparse[entityId];
        int lastIndex = --_count;
        
        // Swap back to maintain density
        T lastVal = _dense[lastIndex];
        int lastSparseIdx = _denseToSparse[lastIndex];
        
        _dense[index] = lastVal;
        _denseToSparse[index] = lastSparseIdx;
        _sparse[lastSparseIdx] = index;
        
        _sparse[entityId] = -1;
    }

    public void EnsureCapacity(int capacity) {
        if (capacity > _sparse.Length) GrowSparse(capacity - 1);
    }

    private void GrowSparse(int entityId) {
        int newSize = (int)BitOperations.RoundUpToPowerOf2((uint)entityId + 1);
        int oldSize = _sparse.Length;
        Array.Resize(ref _sparse, newSize);
        Array.Fill(_sparse, -1, oldSize, newSize - oldSize);
    }

    private void GrowDense() {
        int newSize = Math.Max(4, _dense.Length * 2);
        Array.Resize(ref _dense, newSize);
        Array.Resize(ref _denseToSparse, newSize);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref T GetUnsafe(int entityId) {
        fixed (int* sPtr = _sparse)
        fixed (T* dPtr = _dense) {
            return ref dPtr[sPtr[entityId]];
        }
    }
}