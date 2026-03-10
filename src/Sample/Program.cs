using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Signals.V2;
using YamlDotNet.Core.Tokens;

namespace Sample;

struct Tag1;

unsafe class Program {
    static void Main() {
        using var world = new World();
        var stopwatch = new Stopwatch();

        int count = 1000;
        
        stopwatch.Start();
        for (int i = 0; i < count; i++) {
            world.Create().Set(new Tag1());
        }
        stopwatch.Stop();
        Console.WriteLine($"time to create {count}: {stopwatch.ElapsedMilliseconds} ms");
        
        stopwatch.Start();
        var query1 = world.Query().With<Tag1>().Iterate();

        while (query1.Next() is { } entity)
        {
            
        }
        stopwatch.Stop();
        Console.WriteLine($"time to iterate query: {stopwatch.ElapsedMilliseconds} ms");
    }
}