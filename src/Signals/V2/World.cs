using Signals.Core;
using System.Collections.Concurrent;
using System.Numerics;

namespace Signals.V2;

public static class ComponentRegistry {
    private static int nextId = 0;
    private static readonly Dictionary<Type, int> indices = new();
    public static int Count => nextId;
    
    public static int GetId<T>() where T : struct => GetId(typeof(T));

    public static int GetId(Type type) {
        lock (indices) {
            if (indices.TryGetValue(type, out int id)) return id;
            return indices[type] = nextId++;
        }
    }
}

public readonly struct Sender<TSignal>(World world) where TSignal : struct {
    private readonly World world = world;
    /* public void Send(in T message) => world.Send(message); */
}

public readonly struct Reciever<TSignal>(World world) where TSignal : struct {
    private readonly World world = world;
    /* public void Read(in T message) => world.Read(message); */
}

public sealed class World : IDisposable {
    internal int[] _generations = new int[1024];
    private Stack<int> _freeIds = new();
    private int _nextId = 0;

    private ISparseSet?[] _componentStores = new ISparseSet[ComponentRegistry.Count];
    
    internal Bitset256[] _masks = new Bitset256[1024];
    internal BitmaskArray256 PresenceMask = new();
    
    public readonly int Id;
    private static int worldIdCounter = 0;

    public World() {
        Id = Interlocked.Increment(ref worldIdCounter);
    }

    public Entity Create() {
        int id = _freeIds.TryPop(out int freeId) ? freeId : _nextId++;
        if (id >= _generations.Length) Grow(id);
        
        _generations[id]++;
        _masks[id] = default;
        PresenceMask.Set(id);
        return new Entity(id, _generations[id], this);
    }

    public void Destroy(int id, int generation) {
        if (!IsValid(id, generation)) return;
        _generations[id]++;
        PresenceMask.Unset(id);
        _freeIds.Push(id);
    }

    public bool IsValid(int id, int generation) => id < _nextId && _generations[id] == generation;

    public ref T Get<T>(int id) where T : struct {
        var store = (SparseSet<T>)_componentStores[ComponentRegistry.GetId<T>()]!;
        return ref store.Get(id);
    }

    public void Set<T>(int id, in T value) where T : struct {
        int compId = ComponentRegistry.GetId<T>();
        if (compId >= _componentStores.Length) Array.Resize(ref _componentStores, ComponentRegistry.Count);
        
        var store = (SparseSet<T>)(_componentStores[compId] ??= new SparseSet<T>());
        store.Set(id, value);
        _masks[id].Set(compId);
    }

    public bool Has<T>(int id) where T : struct => _masks[id].IsSet(ComponentRegistry.GetId<T>());

    private void Grow(int min) {
        int newSize = (int)BitOperations.RoundUpToPowerOf2((uint)min + 1);
        Array.Resize(ref _generations, newSize);
        Array.Resize(ref _masks, newSize);
    }
    
    public EntityQuery Query() => new EntityQuery(this);

    public void Dispose() {
        _componentStores = null!;
        _generations = null!;
    }
}