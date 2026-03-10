using Signals.Core;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Signals.V2;

public struct WorldState {
    public int Locks;
    
    public int Lock() => Locks = -1;
    public int Unlock() => Locks = 0;
    public int Begin() => Interlocked.Increment(ref Locks);
    public int End() => Interlocked.Decrement(ref Locks);
}

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

    internal ushort[] Generations = new ushort[1024];
    private readonly ConcurrentStack<uint> freeIds = new();
    private uint nextId = 0;

    private ISparseSet?[] componentStores = new ISparseSet[ComponentStore.Count];

    internal Bitset256[] Masks = new Bitset256[1024];
    internal BitmaskArray256 PresenceMask = new();

    public static readonly World[] AllWorlds = new World[ushort.MaxValue];
    public readonly ushort Id;
    private static int worldIdCounter = 0;

    private WorldState state = new()
    {
        Locks = 0,
    };

    public event Action<World, Entity> OnEntityCreated;
    public event Action<World, Entity> OnEntityDeleted;

    public World() {
        Id = (ushort)Interlocked.Increment(ref worldIdCounter);
        AllWorlds[Id] = this;
    }
    
    public void Playback(Commands cmd) {
        
    }

    public Entity Create() {
        var id = freeIds.TryPop(out var freeId) ? freeId : Interlocked.Increment(ref nextId) - 1;
        if (id >= Generations.Length) Grow(id);
        
        Generations[id]++;
        Masks[id] = default;
        PresenceMask.Set((int)id);

        var entity = new Entity((uint)id, Generations[id], Id);
        
        //OnEntityCreated.Invoke(this, entity);
        return entity;
    }

    public void Destroy(uint id, uint generation) {
        if (!IsValid(id, generation)) return;
        Generations[id]++;
        PresenceMask.Unset((int)id);
        freeIds.Push(id);
    }

    public bool IsValid(uint id, uint generation) => id < nextId && Generations[id] == generation;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(uint id) where T : struct {
        return ref Unsafe.As<SparseSet<T>>(componentStores[Cache<T>.Id]!).GetUnsafe((int)id);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(uint id, uint generation) where T : struct {
        if (!IsValid(id, generation)) {
            throw new InvalidOperationException($"entity {id} is dead or invalid!");
        }
    
        var store = componentStores[Cache<T>.Id];
        if (store == null) {
            throw new KeyNotFoundException($"component {typeof(T).Name} not found in world!");
        }

        return ref Unsafe.As<SparseSet<T>>(store).GetUnsafe((int)id);
    }

    public void Set<T>(uint id, in T value) where T : struct {
        int cid = Cache<T>.Id;
        if (cid >= componentStores.Length) 
            Array.Resize(ref componentStores, Math.Max(cid + 1, componentStores.Length * 2));
        
        var store = (SparseSet<T>)(componentStores[cid] ??= new SparseSet<T>());
        store.Set((int)id, value);
        Masks[id].Set(cid);
    }

    public bool Has<T>(uint id) where T : struct => Masks[id].IsSet(ComponentStore.GetId<T>());

    private void Grow(uint min) {
        int newSize = (int)BitOperations.RoundUpToPowerOf2((uint)min + 1);
        Array.Resize(ref Generations, newSize);
        Array.Resize(ref Masks, newSize);
    }
    
    public EntityQuery Query() => new EntityQuery(this, default, default);

    public void Dispose() {
        componentStores = null!;
        Generations = null!;
    }
}