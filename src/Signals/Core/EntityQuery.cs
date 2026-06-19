using Signals.Core;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Signals;

/*
[AttributeUsage(AttributeTargets.Method)]
public sealed class WithAttribute<T> : Attribute where T : struct;
*/

[AttributeUsage(AttributeTargets.Method)]
public sealed class WithoutAttribute<T> : Attribute where T : struct;

public readonly struct EntityQuery(World world, ComponentMask req, ComponentMask ex) {
    private readonly World world = world;
    public readonly ComponentMask RequiredMask = req;
    public readonly ComponentMask ExcludedMask = ex;

    public EntityQuery With<T>() where T : struct {
        var r = RequiredMask; 
        r.Set(Component.Lookup<T>.Info.Id);
        return new EntityQuery(world, r, ExcludedMask);
    }

    public EntityQuery Without<T>() where T : struct {
        var e = ExcludedMask; 
        e.Set(Component.Lookup<T>.Info.Id);
        return new EntityQuery(world, RequiredMask, e);
    }
    
    public Iterator Iterate() => new Iterator(world, this);

    public unsafe ref struct Iterator {
        private readonly World world;
        private readonly ComponentMask required;
        private readonly ComponentMask excluded;
        private readonly Bitset256[] presenceMask;
        private readonly ComponentMask* maskPtr;
        private readonly ushort* generationPtr;

        private int chunkIndex;
        private Bitset256 currentChunk;
        private int index;

        public Iterator(World world, EntityQuery q) {
            this.world = world;
            required = q.RequiredMask;
            excluded = q.ExcludedMask;
            presenceMask = world.PresenceMask.Array ?? Array.Empty<Bitset256>();
            maskPtr = (ComponentMask*)Unsafe.AsPointer(ref world.Masks[0]);
            generationPtr = (ushort*)Unsafe.AsPointer(ref world.Generations[0]);
            chunkIndex = 0;
            index = -1;
            if (presenceMask.Length > 0) currentChunk = presenceMask[0];
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity? Next() {
            while (chunkIndex < presenceMask.Length) {
                int bit = currentChunk.FirstSetBit();
                if (bit >= Bitset256.CAPACITY) {
                    chunkIndex++;
                    if (chunkIndex < presenceMask.Length) 
                        currentChunk = presenceMask[chunkIndex];
                    continue;
                }

                currentChunk.Clear(bit);
                index = (chunkIndex << 8) + bit;

                if (maskPtr[index].Contains(required) && 
                    !maskPtr[index].AndAny(excluded)) {
                    return new Entity((uint)index, generationPtr[index], 
                        world.Id);
                }
            }
            return null;
        }
    }
}