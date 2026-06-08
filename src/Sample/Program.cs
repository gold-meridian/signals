using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Signals;
using Signals.Systems;

namespace Sample;

struct Tag1;
struct Tag2;
struct TestComponent { public int Value; }

unsafe class Program {
    static void Main() { 
        using var world = new World();

        var app = new App(world);

        app
            .AddSystem(TestUpdate)
            .InStage(stage: Stage.Update)
            .Label("UpdateTest")
            .Build();
        
        app
            .AddSystem(TestUpdate2)
            .InStage(stage: Stage.Update)
            .Label("a")
            .Before("UpdateTest")
            .Build();
        
        var cmds = new Commands();
        cmds.Fetch(world);
        
        const int entityCount = 10_000;
        
        app.Run();
        
        Console.WriteLine(world.PresenceMask.GetSetBits().Count());
        
    }

    [System]
    static void TestUpdate(Commands cmds) {
        Console.WriteLine("fuh");
    }
    
    [System]
    static void TestUpdate2(ref Commands cmds) {
        Console.WriteLine("fuh2");
    }
    
    [System]
    static void TestUpdate3(Tag1 tag) {
        Console.WriteLine("fuh3");
    }
    
    [System]
    static void TestUpdate4(Tag2 tag2, TestComponent component) {
        Console.WriteLine("fuh4");
    }
}