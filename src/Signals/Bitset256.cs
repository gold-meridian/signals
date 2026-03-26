using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Signals.Core;

public struct Bitset256(Vector256<ulong> val) : IEnumerable<int> {
    public const int CAPACITY = 256;
    private const ulong high_bit = 1UL << 63;

    private Vector256<ulong> _bits = val;

    public static Bitset256 Zero => default;

    public bool IsZero => _bits == Vector256<ulong>.Zero;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index) {
        ref ulong baseRef = ref Unsafe.As<Bitset256, ulong>(ref this);
        ref ulong lane = ref Unsafe.Add(ref baseRef, (nuint)(uint)index >> 6);
        lane |= high_bit >> (index & 63);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear(int index) {
        ref ulong baseRef = ref Unsafe.As<Bitset256, ulong>(ref this);
        ref ulong lane = ref Unsafe.Add(ref baseRef, (nuint)(uint)index >> 6);
        lane &= ~(high_bit >> (index & 63));
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsSet(int index) {
        ref ulong baseRef = ref Unsafe.As<Bitset256, ulong>(ref Unsafe.AsRef(in this));
        ulong lane = Unsafe.Add(ref baseRef, (nuint)(uint)index >> 6);
        return (lane & (high_bit >> (index & 63))) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Contains(in Bitset256 other) {
        if(Avx.IsSupported) {
            return Avx.TestC(_bits, other._bits);
        }
        return (_bits & other._bits) == other._bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly unsafe bool Contains(int index) {
        fixed(Vector256<ulong>* ptr = &_bits) {
            ulong* p = (ulong*)ptr;
            int vectorIndex = index >> 6;
            int bitOffset = index & 63;
            ulong mask = 1UL << bitOffset;
            return (p[vectorIndex] & mask) != 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AndAny(in Bitset256 other) {
        if(Avx.IsSupported) {
            return !Avx.TestZ(_bits, other._bits);
        }
        return (_bits & other._bits) != Vector256<ulong>.Zero;
    }

    public readonly int PopCount() {
        return BitOperations.PopCount(_bits.GetElement(0)) +
            BitOperations.PopCount(_bits.GetElement(1)) +
            BitOperations.PopCount(_bits.GetElement(2)) +
            BitOperations.PopCount(_bits.GetElement(3));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int FirstSetBit() {
        ulong e0 = _bits.GetElement(0);
        if(e0 != 0) return BitOperations.LeadingZeroCount(e0);
        ulong e1 = _bits.GetElement(1);
        if(e1 != 0) return 64 + BitOperations.LeadingZeroCount(e1);
        ulong e2 = _bits.GetElement(2);
        if(e2 != 0) return 128 + BitOperations.LeadingZeroCount(e2);
        ulong e3 = _bits.GetElement(3);
        if(e3 != 0) return 192 + BitOperations.LeadingZeroCount(e3);
        return CAPACITY;
    }

    public static Bitset256 operator &(Bitset256 left, Bitset256 right) => new(left._bits & right._bits);
    public static Bitset256 operator |(Bitset256 left, Bitset256 right) => new(left._bits | right._bits);
    public static Bitset256 operator ^(Bitset256 left, Bitset256 right) => new(left._bits ^ right._bits);
    public static Bitset256 operator ~(Bitset256 value) => new(~value._bits);
    public static bool operator ==(Bitset256 left, Bitset256 right) => left._bits == right._bits;
    public static bool operator !=(Bitset256 left, Bitset256 right) => left._bits != right._bits;

    public override bool Equals(object? obj) => obj is Bitset256 other && this == other;
    public override int GetHashCode() => _bits.GetHashCode();

    public readonly Enumerator GetEnumerator() => new(this);
    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override readonly string ToString() {
        Span<char> buffer = stackalloc char[CAPACITY];
        int length = ToString(buffer);
        return buffer.Slice(0, length).ToString();
    }

    public readonly int ToString(Span<char> buffer) {
        int length = 0;
        bool first = true;

        foreach(int index in this) {
            if(!first) {
                if(length + 2 > buffer.Length) break;
                buffer[length++] = ',';
                buffer[length++] = ' ';
            }

            if(!index.TryFormat(buffer.Slice(length), out int charsWritten)) {
                break;
            }
            length += charsWritten;
            first = false;
        }

        return length;
    }

    public struct Enumerator(in Bitset256 mask) : IEnumerator<int> {
        private Bitset256 _mask = mask;
        public int Current { get; private set; } = -1;
        object IEnumerator.Current => Current;
        public bool MoveNext() {
            int index = _mask.FirstSetBit();
            if(index >= CAPACITY) {
                Current = -1;
                return false;
            }
            Current = index;
            _mask.Clear(index);
            return true;
        }
        public void Reset() => throw new NotSupportedException();
        public readonly void Dispose() { }
    }
}

internal struct BitmaskArray256 {
    public Bitset256[]? Array;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int maskIndex, int bitIndex) DivRem(int index) => Math.DivRem(index, Bitset256.CAPACITY);

    public readonly bool Get(int index) {
        if(Array is null) return false;
        var (maskIndex, bitIndex) = DivRem(index);
        return maskIndex < Array.Length && Array[maskIndex].IsSet(bitIndex);
    }

    public void Set(int index) {
        var (maskIndex, bitIndex) = DivRem(index);
        if(Array is null || maskIndex >= Array.Length) {
            System.Array.Resize(ref Array, maskIndex + 1);
        }
        Array[maskIndex].Set(bitIndex);
    }

    public void Unset(int index) {
        if(Array is null) return;
        var (maskIndex, bitIndex) = DivRem(index);
        if(maskIndex < Array.Length) {
            Array[maskIndex].Clear(bitIndex);
        }
    }

    public readonly IEnumerable<int> GetSetBits() {
        if(Array is null) yield break;
        for(int i = 0; i < Array.Length; i++) {
            int baseIndex = i * Bitset256.CAPACITY;
            foreach(int bitIndex in Array[i]) {
                yield return baseIndex + bitIndex;
            }
        }
    }

    public readonly bool Intersects(in BitmaskArray256 other) {
        if(Array is null || other.Array is null) return false;
        int minLength = Math.Min(Array.Length, other.Array.Length);
        for(int i = 0; i < minLength; i++) {
            if(Array[i].AndAny(in other.Array[i])) {
                return true;
            }
        }
        return false;
    }

    public void Union(in BitmaskArray256 other) {
        if(other.Array is null) return;
        if(Array is null || Array.Length < other.Array.Length) {
            System.Array.Resize(ref Array, other.Array.Length);
        }
        for(int i = 0; i < other.Array.Length; i++) {
            Array[i] |= other.Array[i];
        }
    }

    public readonly BitmaskArray256 CloneAndSet(int index) {
        var (maskIndex, bitIndex) = DivRem(index);

        int currentLength = Array?.Length ?? 0;
        int requiredLength = maskIndex + 1;
        var newArray = new Bitset256[requiredLength > currentLength ? requiredLength : currentLength];

        if(currentLength > 0) {
            System.Array.Copy(Array!, newArray, currentLength);
        }

        newArray[maskIndex].Set(bitIndex);
        return new BitmaskArray256 { Array = newArray };
    }

    public readonly ReadOnlySpan<Bitset256> AsSpan() => Array;
}