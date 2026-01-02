namespace Signals.V2;

/// <summary>
///     A lightweight versioned handle representing an entity within a <see cref="World"/>.
/// </summary>
/// <remarks>
///     Entities are not containers, and they do not store components; they are indices
///     that point to component data stored in their parent <see cref="World"/>.
/// <br/> <br/>
///     They use a <see href="https://floooh.github.io/2018/06/17/handles-vs-pointers.html">generational handle</see> pattern:
///     It stores an <see cref="Id">index</see> and a <see cref="Generation">generation</see>. If an entity is destroyed, and
///     its index is reused / recycled, the old handle will have a mismatched generation and will be considered invalid.
/// </remarks>
public readonly struct Entity(uint id, uint generation, World world) {
    /// <summary>
    ///     The raw index of this entity in its parent worlds storage. 
    /// </summary>
    public readonly uint Id = id;
    /// <summary>
    ///     The version of this entity. Used to prevent stale entity handles.
    /// </summary>
    public readonly uint Generation = generation;
    /// <summary>
    ///     The parent world that owns and manages the data for this entity.
    /// </summary>
    public readonly World World = world;
    
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

    internal class DebugView(Entity entity) {
        
    }
}

public static class EntityExt {
    extension(Entity entity) {
        public EntityView<T1, T2> View<T1, T2>() where T1 : struct where T2 : struct {
            return new(entity);
        }
    }
}

/// <summary>
///     A view of an entity that lives on the stack, containing pre-cached component references.
///     Reduces lookup cost by resolving all component storage locations once upon creation.
/// </summary>
public readonly ref struct EntityView<T1, T2> 
    where T1 : struct 
    where T2 : struct
{
    public readonly Entity Entity;
    public readonly ref T1 Component1;
    public readonly ref T2 Component2;

    public EntityView(Entity entity) {
        if (!entity.IsAlive) 
            throw new InvalidOperationException("cannot create view for dead entity!");
            
        Entity = entity;
        Component1 = ref entity.Get<T1>();
        Component2 = ref entity.Get<T2>();
    }
}