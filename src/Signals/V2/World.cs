using Signals.Core;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Signals.V2;

public static class ComponentStore {
    private static int nextId = 0;
    private static readonly Dictionary<Type, int> indices = new();
    private static Type[] idToType = new Type[64];
    
    public static int Count => nextId;
    
    public static int GetId<T>() where T : struct => GetId(typeof(T));

    public static int GetId(Type type) {
        lock (indices) {
            if (indices.TryGetValue(type, out int id)) return id;
            
            if (nextId >= idToType.Length) 
                Array.Resize(ref idToType, idToType.Length * 2);
            
            idToType[nextId] = type;
            return indices[type] = nextId++;
        }
    }
    
    public static Type GetType(int id) => idToType[id];
}

public sealed class World : IDisposable {
    public static class Cache<T> where T : struct {
        public static readonly int Id = ComponentStore.GetId(typeof(T));
    }
    
    internal ushort[] _generations = new ushort[1024];
    private Stack<uint> _freeIds = new();
    private uint _nextId = 0;

    private ISparseSet?[] _componentStores = new ISparseSet[ComponentStore.Count];
    
    internal Bitset256[] _masks = new Bitset256[1024];
    internal BitmaskArray256 PresenceMask = new();
    
    public static readonly World[] AllWorlds = new World[ushort.MaxValue];
    public readonly ushort Id;
    private static int worldIdCounter = 0;

    public World() {
        Id = (ushort)Interlocked.Increment(ref worldIdCounter);
        AllWorlds[Id] = this;
    }

    public Entity Create() {
        var id = _freeIds.TryPop(out var freeId) ? freeId : _nextId++;
        if (id >= _generations.Length) Grow(id);
        
        _generations[id]++;
        _masks[id] = default;
        PresenceMask.Set((int)id);
        return new Entity((uint)id, _generations[id], Id);
    }

    public void Destroy(uint id, uint generation) {
        if (!IsValid(id, generation)) return;
        _generations[id]++;
        PresenceMask.Unset((int)id);
        _freeIds.Push(id);
    }

    public bool IsValid(uint id, uint generation) => id < _nextId && _generations[id] == generation;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(uint id) where T : struct {
        return ref Unsafe.As<SparseSet<T>>(_componentStores[Cache<T>.Id]!).GetUnsafe((int)id);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(uint id, uint generation) where T : struct {
        if (!IsValid(id, generation)) {
            throw new InvalidOperationException($"entity {id} is dead or invalid!");
        }
    
        var store = _componentStores[Cache<T>.Id];
        if (store == null) {
            throw new KeyNotFoundException($"component {typeof(T).Name} not found in world!");
        }

        return ref Unsafe.As<SparseSet<T>>(store).GetUnsafe((int)id);
    }

    public void Set<T>(uint id, in T value) where T : struct {
        int cid = Cache<T>.Id;
        if (cid >= _componentStores.Length) Array.Resize(ref _componentStores, Math.Max(cid + 1, _componentStores.Length * 2));
        var store = (SparseSet<T>)(_componentStores[cid] ??= new SparseSet<T>());
        store.Set((int)id, value);
        _masks[id].Set(cid);
    }

    public bool Has<T>(uint id) where T : struct => _masks[id].IsSet(ComponentStore.GetId<T>());

    private void Grow(uint min) {
        int newSize = (int)BitOperations.RoundUpToPowerOf2((uint)min + 1);
        Array.Resize(ref _generations, newSize);
        Array.Resize(ref _masks, newSize);
    }
    
    public SparseSet<T> GetStorage<T>() where T : struct {
        return (SparseSet<T>)_componentStores[ComponentStore.GetId<T>()]!;
    }
    
    public EntityQuery Query() => new EntityQuery(this, default, default);

    public void Dispose() {
        _componentStores = null!;
        _generations = null!;
    }
}