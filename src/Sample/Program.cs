using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Signals.V2;

namespace Sample;

public struct Position { public float X; public float Y; }
public struct Velocity { public float X; public float Y; }

class Program {
    static void Main() {
        using var world = new World();
        
        const int total_count = 100_000;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < total_count; i++) {
            var entity = world.Create();
            entity.Set(new Position { X = i, Y = i });
            
            if (i % 2 == 0) {
                entity.Set(new Velocity { X = 1.0f, Y = 1.0f });
            }
        }
        
        Console.WriteLine($"spawned {total_count} entities in: {sw.ElapsedMilliseconds}ms\n");

        sw.Restart();
        int manualUpdates = 0;
        for (uint i = 0; i < total_count; i++) {
            if (world.Has<Position>(i) && world.Has<Velocity>(i)) {
                ref var pos = ref world.Get<Position>(i);
                ref var vel = ref world.Get<Velocity>(i);
                pos.X += vel.X;
                manualUpdates++;
            }
        }
        Console.WriteLine($"manual iter loop ({manualUpdates} entities): {sw.Elapsed.TotalMilliseconds}ms");

        sw.Restart();
        var query = world.Query().With<Position>().With<Velocity>().Iterate();
        
        int updates = 0;
        while (query.Next() is { } entity) {
            ref var pos = ref entity.Get<Position>();
            ref var vel = ref entity.Get<Velocity>();
            pos.X += vel.X;
            updates++;
        }
        
        Console.WriteLine($"bitmask query loop  ({updates} entities): {sw.Elapsed.TotalMilliseconds}ms\n");
    }
}