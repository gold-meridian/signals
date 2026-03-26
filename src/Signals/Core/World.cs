using Signals.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Signals;

[DebuggerDisplay("ID: {Id}, Size: {Size}")]
public readonly struct ComponentInfo {
    public readonly int Id;
    public readonly int Size;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentInfo(int id, int size) {
        Id = id;
        Size = size;
    }
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

public static class ComponentMeta<T> where T : struct {
    public static readonly ComponentInfo Info = new(ComponentStore.GetId<T>(), Unsafe.SizeOf<T>());
}

public sealed partial class World : IDisposable {
    internal ushort[] Generations = new ushort[1024];
    private readonly ConcurrentStack<uint> freeIds = new();
    private uint nextId = 0;

    private ISparseSet?[] componentStores = new ISparseSet[ComponentStore.Count];

    internal Bitset256[] Masks = new Bitset256[1024];
    internal BitmaskArray256 PresenceMask = new();

    public static readonly World[] AllWorlds = new World[ushort.MaxValue];
    public readonly ushort Id;
    private static int worldIdCounter = 0;

    public event Action<World, Entity>? OnEntityCreated;
    public event Action<World, Entity>? OnEntityDestroyed;
    
    private readonly object layoutLock = new();

    public World() {
        Id = (ushort)Interlocked.Increment(ref worldIdCounter);
        AllWorlds[Id] = this;
    }

    public Entity Create() {
        var id = freeIds.TryPop(out var freeId) ? freeId : Interlocked.Increment(ref nextId) - 1;
        if (id >= Generations.Length) Grow(id);
        
        Generations[id]++;
        Masks[id] = default;
        PresenceMask.Set((int)id);

        var entity = new Entity((uint)id, Generations[id], Id);
        
        OnEntityCreated?.Invoke(this, entity);
        return entity;
    }

    public void Destroy(uint id, ushort generation) {
        if (!IsValid(id, generation)) return;
        
        foreach (int componentId in Masks[id]) {
            if (componentId < componentStores.Length && componentStores[componentId] != null) {
                componentStores[componentId]!.Remove((int)id);
            }
        }
        Masks[id] = default;

        Generations[id]++;
        PresenceMask.Unset((int)id);
        freeIds.Push(id);

        OnEntityDestroyed?.Invoke(this, new Entity(id, generation, Id));
    }

    /// <summary>
    /// Checks if an entity handle (id and generation) is currently valid.
    /// </summary>
    public bool IsValid(uint id, ushort generation) => id < nextId && Generations[id] == generation;

    /// <summary>
    /// Checks if an entity ID currently points to an existing entity, ignoring generation.
    /// Useful for deferred commands where only the ID is known at queue time.
    /// </summary>
    public bool Exists(uint id) => id < nextId && PresenceMask.Get((int)id);
    
    public bool IsValid(uint id, uint generation) => id < nextId && Generations[id] == generation;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(uint id) where T : struct {
        return ref Unsafe.As<SparseSet<T>>(componentStores[ComponentMeta<T>.Info.Id]!).GetUnsafe((int)id);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(uint id, uint generation) where T : struct {
        if (!IsValid(id, generation)) {
            throw new InvalidOperationException($"entity {id} is dead or invalid!");
        }
    
        var store = componentStores[ComponentMeta<T>.Info.Id];
        if (store == null) {
            throw new KeyNotFoundException($"component {typeof(T).Name} not found in world!");
        }

        return ref Unsafe.As<SparseSet<T>>(store).GetUnsafe((int)id);
    }

    public void Set<T>(uint id, in T value) where T : struct {
        int cid = ComponentMeta<T>.Info.Id;
        if (cid >= componentStores.Length) 
            Array.Resize(ref componentStores, Math.Max(cid + 1, componentStores.Length * 2));
        
        var store = (SparseSet<T>)(componentStores[cid] ??= new SparseSet<T>());
        store.Set((int)id, value);
        Masks[id].Set(cid);
    }
    
    public void Remove<T>(uint id) where T : struct {
        int cid = ComponentMeta<T>.Info.Id;
        if (cid < componentStores.Length && componentStores[cid] != null && Has<T>(id))
        {
            componentStores[cid]!.Remove((int)id);
            Masks[id].Clear(cid);
        }
    }

    public bool Has<T>(uint id) where T : struct => Masks[id].IsSet(ComponentStore.GetId<T>());

    private void Grow(uint min) {
        lock (layoutLock) {
            if (min < Generations.Length) return;

            int newSize = (int)BitOperations.RoundUpToPowerOf2(min + 1);
            Array.Resize(ref Generations, newSize);
            Array.Resize(ref Masks, newSize);
        }
    }
    
    public EntityQuery Query() => new EntityQuery(this, default, default);

    public void Dispose() {
        componentStores = null!;
        Generations = null!;
    }
}