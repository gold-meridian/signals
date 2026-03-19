using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Signals.V2;

namespace Sample;

struct Tag1;
struct Tag2;
struct TestComponent { public int Value; }

public readonly ref struct ScopedStopwatch : IDisposable {
    private readonly long start;
    private readonly string label;

    public ScopedStopwatch(string label) {
        this.label = label;
        start = Stopwatch.GetTimestamp();
    }

    public void Dispose() {
        var elapsed = Stopwatch.GetElapsedTime(start);
        Console.WriteLine($"[{label}] {elapsed.TotalMilliseconds:F3}ms");
    }
}

unsafe class Program {
    static void Main() {
        using var world = new World();
        var cmds = new Commands();
        cmds.Fetch(world);

        const int entityCount = 1_000_000;
        const int threadCount = 12;

        Console.WriteLine($"spawning {entityCount:N0} entities on {threadCount} threads...");

        using (new ScopedStopwatch("Recording")) {
            Parallel.For(0, entityCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, i => {
                cmds.Spawn().Set(new TestComponent { Value = i });
            });
        }

        using (new ScopedStopwatch("Apply")) {
            cmds.Apply();
        }
    }
}