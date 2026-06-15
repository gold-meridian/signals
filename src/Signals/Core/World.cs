using Signals.Core;
using Signals.Core.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Signals;

public static partial class Component {
    [DebuggerDisplay("ID: {Id}, Size: {Size}")]
    public readonly record struct Info(i32 Id, i32 Size, Type Type, string TypeName) {
        public static implicit operator i32(Info cid) => cid.Id;
    }
    
    public static class Lookup<T> where T : struct {
        public static readonly Info Info;

        static Lookup() {
            Info = Register<T>();
        }
    }
    
    private static i32 nextId = 0;
    private static readonly ConcurrentDictionary<Type, i32> typeToId = new();
    private static readonly ConcurrentDictionary<i32, Info> infoById = new();
    
    public static i32 Count => nextId;
    
    private static Info Register<T>() where T : struct {
        var type = typeof(T);
        
        if (typeToId.TryGetValue(type, out int cachedId))
            return infoById[cachedId];

        lock (typeToId) {
            if (typeToId.TryGetValue(type, out cachedId))
                return infoById[cachedId];

            i32 id = nextId++;
            var info = new Info(id, Unsafe.SizeOf<T>(), type, type.Name);

            typeToId[type] = id;
            infoById[id] = info;
            return info;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static i32 GetId<T>() where T : struct => Lookup<T>.Info.Id;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Info GetInfo<T>() where T : struct => Lookup<T>.Info;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Info GetInfo(i32 id) {
        if (!infoById.TryGetValue(id, out var info))
            throw new KeyNotFoundException($"component id {id} not registered!");
        return info;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Info GetInfo(Type type) {
        if (!typeToId.TryGetValue(type, out var id) || !infoById.TryGetValue(id, out var info)) throw new KeyNotFoundException($"component {type.Name} not registered!");
        
        return info;
    }
}

public sealed partial class World : IDisposable {
    internal u16[] Generations = new u16[1024];
    private readonly ConcurrentStack<uint> freeIds = new();
    private u32 nextId = 0;

    private ISparseSet?[] componentStores = new ISparseSet[Component.Count];

    internal Bitset256[] Masks = new Bitset256[1024];
    internal BitmaskArray256 PresenceMask = new();

    public static readonly World[] AllWorlds = new World[u16.MaxValue];
    public readonly u16 Id;
    private static i32 worldIdCounter = 0;
    
    private readonly object layoutLock = new();
    
    private readonly Pool<Commands> commandBufferPool;
    private readonly Stack<Commands> availableBuffers = new();

    public World() {
        Id = (u16)Interlocked.Increment(ref worldIdCounter);
        AllWorlds[Id] = this;
        
        commandBufferPool = new Pool<Commands>(() => new Commands(), capacity: 8);

        for (int i = 0; i < 8; i++) {
            availableBuffers.Push(commandBufferPool.Rent());
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Commands AcquireCommandBuffer() {
        if (availableBuffers.TryPop(out var buffer)) {
            buffer.Fetch(this);
            return buffer;
        }

        var newBuffer = commandBufferPool.Rent();
        newBuffer.Fetch(this);
        return newBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ReleaseCommandBuffer(Commands buffer) {
        availableBuffers.Push(buffer);
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
    public void Destroy(u32 id, u16 generation) {
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
    public bool IsValid(u32 id, u16 generation) => id < nextId && Generations[id] == generation;

    /// <summary>
    /// Checks if an entity ID currently points to an existing entity, ignoring generation.
    /// Useful for deferred commands where only the ID is known at queue time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Exists(u32 id) => id < nextId && PresenceMask.Get((int)id);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(u32 id, u32 generation) => id < nextId && Generations[id] == generation;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(u32 id) where T : struct {
        return ref Unsafe.As<SparseSet<T>>(componentStores[Component.Lookup<T>.Info.Id]!).GetUnsafe((int)id);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(u32 id, u32 generation) where T : struct {
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
    public void Set<T>(u32 id, in T value) where T : struct {
        int cid = Component.Lookup<T>.Info.Id;
        if (cid >= componentStores.Length) 
            Array.Resize(ref componentStores, Math.Max(cid + 1, componentStores.Length * 2));
        
        var store = (SparseSet<T>)(componentStores[cid] ??= new SparseSet<T>());
        store.Set((int)id, value);
        Masks[id].Set(cid);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove<T>(u32 id) where T : struct {
        int cid = Component.Lookup<T>.Info.Id;
        if (cid < componentStores.Length && componentStores[cid] != null && Has<T>(id))
        {
            componentStores[cid]!.Remove((int)id);
            Masks[id].Clear(cid);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has<T>(u32 id) where T : struct => Masks[id].IsSet(Component.GetId<T>());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Grow(u32 min) {
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