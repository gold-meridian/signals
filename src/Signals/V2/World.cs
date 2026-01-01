using Standard;
using System.Collections.Concurrent;

namespace Signals.V2;

public class World {
    internal struct EntityMetadata {
        public uint Generation;
        public Bitset256 Mask;
    }
    
    public int NextEntityIndex = 0;

    public BitmaskArray256 EntityPresenceMasks = new();
    public Bitset256[] EntityComponentMasks = Array.Empty<Bitset256>();
    public uint[] EntityGenerations = Array.Empty<uint>();
    public ConcurrentBag<int> FreeEntityIndices = new();

    private uint worldId;
}