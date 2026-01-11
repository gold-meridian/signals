using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Signals.V2;

/// <summary>
///     A lightweight versioned handle representing an entity within a <see cref="World"/>.
/// </summary>
/// <remarks>
///     Entities are not containers, and they do not store components; they are indices
///     that point to component data stored in their parent <see cref="World"/>.
/// <br/>
///     They use a <see href="https://floooh.github.io/2018/06/17/handles-vs-pointers.html">generational handle</see> pattern:
///     It stores an <see cref="Id">index</see> and a <see cref="Generation">generation</see>. If an entity is destroyed, and
///     its index is reused / recycled, the old handle will have a mismatched generation and will be considered invalid.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 8)]
[DebuggerTypeProxy(typeof(DebugView))]
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct Entity(uint id, ushort generation, ushort world) {
    /// <summary>
    ///     The raw index of this entity in its parent worlds storage. 
    /// </summary>
    [FieldOffset(0)] public readonly uint Id = id;
    /// <summary>
    ///     The version of this entity. Used to prevent stale entity handles.
    /// </summary>
    [FieldOffset(4)] public readonly ushort Generation = generation;
    /// <summary>
    ///     The parent world that owns and manages the data for this entity.
    /// </summary>
    [FieldOffset(6)] public readonly ushort WorldId = world;
    
    public World World => World.AllWorlds[WorldId];
    
    /// <summary>
    ///     The size of the <see cref="Entity"/> struct in bytes.
    /// </summary>
    public static readonly int SIZE = GetSize();

    private static unsafe int GetSize() => sizeof(Entity);
    
    /// <summary>
    ///     Checks if an entity is currently active in its world.
    /// </summary>
    /// <value>
    ///     True if the entity exists and its generation matches the worlds record.
    /// </value>
    public bool IsAlive => World.IsValid(Id, Generation);
    
    /// <summary>
    ///     Gets a reference to a component of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The component.</typeparam>
    /// <returns>A reference to the component.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the entity is dead.</exception>
    public ref T Get<T>() where T : struct => ref World.Get<T>(Id, Generation);
    
    /// <summary>
    ///     Adds or updates a component of type <typeparamref name="T"/> for this entity.
    /// </summary>
    /// <typeparam name="T">The component.</typeparam>
    /// <param name="value">The data to give to the component.</param>
    public void Set<T>(in T value) where T : struct => World.Set<T>(Id, value);
    
    /// <summary>
    ///     Checks whether the entity has a component of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The component to check for.</typeparam>
    /// <returns>True if the component exists on this entity.</returns>
    public bool Has<T>() where T : struct => World.Has<T>(Id);
    
    /// <summary>
    ///     Removes the entity from the world and prepares its index for recycling.
    /// </summary>
    /// <remarks>
    ///     All components attached to this entity will be removed and their storage reclaimed.
    ///     Any existing <see cref="Entity"/> handles pointing to this ID will immediately become invalid.
    /// </remarks>
    public void Destroy() => World.Destroy(Id, Generation);
    
    internal object? GetDebug(Type t) {
        try {
            var method = typeof(World).GetMethod("Get", [typeof(uint), typeof(ushort)])
                ?.MakeGenericMethod(t);
            return method?.Invoke(World, [Id, Generation]);
        } catch {
            return null;
        }
    }

    internal sealed class DebugView(Entity target) {
        public uint Id => target.Id;
        public ushort Generation => target.Generation;
        public bool IsAlive => target.IsAlive;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Dictionary<Type, object> Components {
            get {
                var components = new Dictionary<Type, object>();
                if (!target.IsAlive) return components;

                var world = target.World;
                var mask = world._masks[target.Id];

                for (int i = 0; i < ComponentStore.Count; i++) {
                    if (mask.IsSet(i)) {
                        var type = ComponentStore.GetType(i);
                        var data = target.GetDebug(type);
                        if (data != null) 
                            components[type] = data;
                    }
                }
                return components;
            }
        }
    }
}