using Signals.Core;

namespace Signals.V2;

public readonly struct EntityQuery(World world) {
    private readonly World _world = world;
    internal readonly Bitset256 RequiredMask = default;
    internal readonly Bitset256 ExcludedMask = default;

    private EntityQuery(World world, Bitset256 req, Bitset256 ex) : this(world) {
        RequiredMask = req;
        ExcludedMask = ex;
    }

    public EntityQuery With<T>() where T : struct {
        var req = RequiredMask;
        req.Set(ComponentRegistry.GetId<T>());
        return new EntityQuery(_world, req, ExcludedMask);
    }

    public EntityQuery Without<T>() where T : struct {
        var ex = ExcludedMask;
        ex.Set(ComponentRegistry.GetId<T>());
        return new EntityQuery(_world, RequiredMask, ex);
    }

    public Iterator GetEnumerator() => new Iterator(_world, this);

    public ref struct Iterator {
        private readonly World _world;
        private readonly Bitset256 _req;
        private readonly Bitset256 _ex;
        private int _index;
        private readonly int _maxId;

        public Iterator(World world, EntityQuery entityQuery) {
            _world = world;
            _req = entityQuery.RequiredMask;
            _ex = entityQuery.ExcludedMask;
            _index = -1;
            
            int arrayLength = world.PresenceMask.Array?.Length ?? 0;
            _maxId = arrayLength * Bitset256.CAPACITY;
        }

        public Entity Current => new(_index, _world._generations[_index], _world);

        public bool MoveNext() {
            while (++_index < _maxId) {
                if (!_world.PresenceMask.Get(_index)) continue;
                ref var mask = ref _world._masks[_index];
                if (mask.Contains(_req) && !mask.AndAny(_ex)) return true;
            }
            return false;
        }
    }
}