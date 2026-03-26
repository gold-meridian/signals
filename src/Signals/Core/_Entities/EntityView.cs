namespace Signals;

/// <summary>
///     A view of an entity that lives on the stack, containing pre-cached component references.
///     Reduces lookup cost by resolving all component storage locations once upon creation.
/// </summary>
/// <typeparam name="T1"></typeparam>
public readonly ref partial struct EntityView<T1> where T1 : struct {
    public readonly Entity Entity;
    public readonly ref T1 Component1;

    public EntityView(Entity entity) {
        if (!entity.IsAlive) 
            throw new InvalidOperationException("cannot create view for dead entity!");
            
        Entity = entity;
        Component1 = ref entity.Get<T1>();
    }
}