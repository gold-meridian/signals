namespace Signals.Systems;

public readonly ref struct Res<T> where T : class {
    private readonly T value;
    public Res(T value) => this.value = value;
    public T Value => value;
}

public readonly ref struct Mut<T>(T value) where T : class {
    private readonly T value = value;
    
    public T Value => value;
}

public sealed class ResourceManager : IDisposable {
    internal static class Lookup {
        private static int counter;
        private static readonly Dictionary<Type, int> ids = new();

        public static int ID<T>() => Cache<T>.Id;

        private static class Cache<T> {
            public static readonly int Id = System.Threading.Interlocked.Increment(ref counter);
        }
    }
    
    private readonly Dictionary<int, object> resources = new();
    private bool isDisposed;

    public void Add<T>(T resource) where T : class {
        if (resource == null) throw new ArgumentNullException(nameof(resource));
        resources[Lookup.ID<T>()] = resource;
    }

    public void Init<T>() where T : class, new() {
        resources[Lookup.ID<T>()] = new T();
    }

    public T Get<T>() where T : class {
        if (!resources.TryGetValue(Lookup.ID<T>(), out var resource)) {
            throw new KeyNotFoundException($"resource of type {typeof(T).Name} was not found!");
        }
        return (T)resource;
    }

    public bool TryGet<T>(out T? resource) where T : class {
        if (resources.TryGetValue(Lookup.ID<T>(), out var res)) {
            resource = (T)res;
            return true;
        }
        resource = null;
        return false;
    }

    public bool Has<T>() => resources.ContainsKey(Lookup.ID<T>());

    public void Dispose() {
        if (isDisposed) return;
        isDisposed = true;

        foreach (var resource in resources.Values) {
            if (resource is IDisposable disposable) {
                disposable.Dispose();
            }
        }
        resources.Clear();
    }
}