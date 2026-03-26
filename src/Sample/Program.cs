using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Signals;

namespace Sample;

struct Tag1;
struct Tag2;
struct TestComponent { public int Value; }

unsafe class Program {
    static void Main() { 
        using var world = new World();
        
        var cmds = new Commands();
        cmds.Fetch(world);
        
        const int entityCount = 10_000;
         
        Parallel.For(0, 
            entityCount, 
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount - 1, 1) }, 
            i => {
                     
                cmds.Spawn()
                    .Set(new TestComponent { Value = i })
                    .Set(new Tag2());
            });
        
        cmds.Apply();

        var query = world.Query()
            .With<TestComponent>()
            .Iterate();

        int counter = 0;
        
        while (query.Next() is { } entity) {
            ref var tc = ref entity.Get<TestComponent>();
            counter++;
        }
        
        Console.WriteLine($"entities found in query: {counter}");
        
        Console.WriteLine(world.PresenceMask.GetSetBits().Count());
    }
}