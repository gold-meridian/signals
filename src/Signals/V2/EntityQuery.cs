using Signals.Core;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Signals.V2;

public readonly struct EntityQuery(World world, Bitset256 req, Bitset256 ex) {
    private readonly World _world = world;
    public readonly Bitset256 RequiredMask = req;
    public readonly Bitset256 ExcludedMask = ex;

    public EntityQuery With<T>() where T : struct {
        var r = RequiredMask; r.Set(World.Cache<T>.Id);
        return new EntityQuery(_world, r, ExcludedMask);
    }

    public Iterator Iterate() => new Iterator(_world, this);

    public unsafe ref struct Iterator {
        private readonly World _world;
        private readonly Bitset256 _req;
        private readonly Bitset256 _ex;
        private readonly Bitset256[] _presence;
        private readonly Bitset256* _masksPtr;
        private readonly ushort* _gensPtr;

        private int _chunkIdx;
        private Bitset256 _currentChunk;
        private int _index;

        public Iterator(World world, EntityQuery q) {
            _world = world;
            _req = q.RequiredMask;
            _ex = q.ExcludedMask;
            _presence = world.PresenceMask.Array ?? Array.Empty<Bitset256>();
            _masksPtr = (Bitset256*)Unsafe.AsPointer(ref world._masks[0]);
            _gensPtr = (ushort*)Unsafe.AsPointer(ref world._generations[0]);
            _chunkIdx = 0;
            _index = -1;
            if (_presence.Length > 0) _currentChunk = _presence[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity? Next() {
            while (_chunkIdx < _presence.Length) {
                int bit = _currentChunk.FirstSetBit();
                if (bit >= Bitset256.CAPACITY) {
                    _chunkIdx++;
                    if (_chunkIdx < _presence.Length) _currentChunk = _presence[_chunkIdx];
                    continue;
                }

                _currentChunk.Clear(bit);
                _index = (_chunkIdx << 8) + bit;

                if (_masksPtr[_index].Contains(_req) && !_masksPtr[_index].AndAny(_ex)) {
                    return new Entity((uint)_index, _gensPtr[_index], _world.Id);
                }
            }
            return null;
        }
    }
}