using Signals.Core;
using Signals.Core.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Signals;

public static partial class Component {
    [DebuggerDisplay("ID: {Id}, Size: {Size}")]
    public readonly record struct Info(int Id, int Size, Type Type, string TypeName) {
        public static implicit operator int(Info cid) => cid.Id;
    }
    
    public static class Lookup<T> where T : struct {
        public static readonly Info Info;

        static Lookup() {
            Info = new(GetId<T>(), Unsafe.SizeOf<T>(), typeof(T), typeof(T).Name);
        }
    }
    
    private static int nextId = 0;
    private static readonly ConcurrentDictionary<Type, int> typeToId = new();
    private static readonly ConcurrentDictionary<int, Info> infoById = new();
    
    private static Info Register<T>() where T : struct {
        var type = typeof(T);
        
        if (typeToId.TryGetValue(type, out int cachedId))
            return infoById[cachedId];

        lock (typeToId) {
            if (typeToId.TryGetValue(type, out cachedId))
                return infoById[cachedId];

            int id = nextId++;
            var info = new Info(id, Unsafe.SizeOf<T>(), type, type.Name);

            typeToId[type] = id;
            infoById[id] = info;
            return info;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetId<T>() where T : struct => Lookup<T>.Info.Id;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Info GetInfo<T>() where T : struct => Lookup<T>.Info;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Info GetInfo(int id) {
        if (!infoById.TryGetValue(id, out var info))
            throw new KeyNotFoundException($"component id {id} not registered!");
        return info;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Info GetInfo(Type type) {
        if (!typeToId.TryGetValue(type, out int id) || !infoById.TryGetValue(id, out var info)) throw new KeyNotFoundException($"component {type.Name} not registered!");
        
        return info;
    }

    public static int Count => nextId;
}

public sealed partial class World : IDisposable {
    internal ushort[] Generations = new ushort[1024];
    private readonly ConcurrentStack<uint> freeIds = new();
    private uint nextId = 0;

    private ISparseSet?[] componentStores = new ISparseSet[Component.Count];

    internal Bitset256[] Masks = new Bitset256[1024];
    internal BitmaskArray256 PresenceMask = new();

    public static readonly World[] AllWorlds = new World[ushort.MaxValue];
    public readonly ushort Id;
    private static int worldIdCounter = 0;
    
    private readonly object layoutLock = new();

    private readonly Queue<Commands> cmdBuffersQueue;
    private readonly Pool<Commands> cmdBuffers;

    public World() {
        Id = (ushort)Interlocked.Increment(ref worldIdCounter);
        AllWorlds[Id] = this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Create() {
        var id = freeIds.TryPop(out var freeId) ? freeId : Interlocked.Increment(ref nextId) - 1;
        if (id >= Generations.Length) Grow(id);
        
        Generations[id]++;
        Masks[id] = default;
        PresenceMask.Set((int)id);

        var entity = new Entity((uint)id, Generations[id], Id);
        
        return entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    }

    /// <summary>
    /// Checks if an entity handle (id and generation) is currently valid.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(uint id, ushort generation) => id < nextId && Generations[id] == generation;

    /// <summary>
    /// Checks if an entity ID currently points to an existing entity, ignoring generation.
    /// Useful for deferred commands where only the ID is known at queue time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Exists(uint id) => id < nextId && PresenceMask.Get((int)id);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(uint id, uint generation) => id < nextId && Generations[id] == generation;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(uint id) where T : struct {
        return ref Unsafe.As<SparseSet<T>>(componentStores[Component.Lookup<T>.Info.Id]!).GetUnsafe((int)id);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(uint id, uint generation) where T : struct {
        if (!IsValid(id, generation)) {
            throw new InvalidOperationException($"entity {id} is dead or invalid!");
        }
    
        var store = componentStores[Component.Lookup<T>.Info.Id];
        if (store == null) {
            throw new KeyNotFoundException($"component {typeof(T).Name} not found in world!");
        }

        return ref Unsafe.As<SparseSet<T>>(store).GetUnsafe((int)id);
    }
   
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set<T>(uint id, in T value) where T : struct {
        int cid = Component.Lookup<T>.Info.Id;
        if (cid >= componentStores.Length) 
            Array.Resize(ref componentStores, Math.Max(cid + 1, componentStores.Length * 2));
        
        var store = (SparseSet<T>)(componentStores[cid] ??= new SparseSet<T>());
        store.Set((int)id, value);
        Masks[id].Set(cid);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove<T>(uint id) where T : struct {
        int cid = Component.Lookup<T>.Info.Id;
        if (cid < componentStores.Length && componentStores[cid] != null && Has<T>(id))
        {
            componentStores[cid]!.Remove((int)id);
            Masks[id].Clear(cid);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has<T>(uint id) where T : struct => Masks[id].IsSet(Component.GetId<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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