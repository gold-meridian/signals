namespace Signals.Systems;


//right now Res and Mut are useless since this is singlethreaded, but eventually move to multithreading and stricter api contracts.

public readonly struct Res<T> where T : class {
    public readonly T Value;
    
    public Res(T value) => Value = value;
}

public struct Mut<T> where T : class {
    public T Value;
    
    public Mut(T value) => Value = value;
}

public sealed class Resources {
    private readonly Dictionary<Type, object> resources = new();

    public void Add<T>(T resource) where T : class {
        if (resource == null)
            throw new ArgumentNullException(nameof(resource));
        
        resources[typeof(T)] = resource;
    }

    public T Get<T>() where T : class {
        if (!resources.TryGetValue(typeof(T), out var resource))
            throw new InvalidOperationException($"resource of type {typeof(T).Name} not found!");
        
        return (T)resource;
    }

    public bool TryGet<T>(out T resource) where T : class {
        if (resources.TryGetValue(typeof(T), out var res)) {
            resource = (T)res;
            return true;
        }
        
        resource = null!;
        return false;
    }

    public Res<T> GetResource<T>() where T : class => new(Get<T>());
    
    public Mut<T> GetMutable<T>() where T : class => new(Get<T>());

    public bool Contains<T>() where T : class => resources.ContainsKey(typeof(T));

    public bool Remove<T>() where T : class => resources.Remove(typeof(T));

    public void Clear() => resources.Clear();
}