using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Generator;

internal class EntityViews {
    internal record struct Descriptor(int Count) {
        public string Parameters => string.Join(", ", Enumerable.Range(1, Count).Select(i => $"T{i}"));
        public string Constraints => string.Join(" ", Enumerable.Range(1, Count).Select(i => $"where T{i} : struct"));

        private const string xml_doc = "/// <summary>" +
                                       "\n///     A view of an entity that lives on the stack, containing pre-cached component references." +
                                       "\n///     Reduces lookup cost by resolving all component storage locations once upon creation." +
                                       "\n/// </summary>";
        
        public void Document(IndentedStringWriter writer) {
            writer.WriteLine(xml_doc);
            for (int i = 1; i <= Count; i++) {
                writer.WriteLine($"/// <typeparam name=\"T{i}\"></typeparam>");
            }
        }
    }
    
    [Generator]
    internal class Generator() : IIncrementalGenerator {
        private const int max_count = 32;
        
        void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context) {
            var descriptors = Enumerable.Range(2, max_count)
                .Select(i => new Descriptor(i))
                .ToImmutableArray();

            context.RegisterPostInitializationOutput(ctx => {
                using var writer = new IndentedStringWriter();
                writer.WriteLine("using System;");
                writer.WriteLine("");
                writer.WriteLine("namespace Signals.V2;");

                foreach (var desc in descriptors) {
                    desc.Document(writer);
                    using (writer.BeginScope($"public readonly ref struct EntityView<{desc.Parameters}> {desc.Constraints}")) {
                        writer.WriteLine("public readonly Entity Entity;");
                        for (int i = 1; i <= desc.Count; i++) writer.WriteLine($"public readonly ref T{i} Component{i};");
                        
                        writer.WriteLine();
                        using (writer.BeginScope($"public EntityView(Entity entity)")) {
                            writer.WriteLine("if (!entity.IsAlive) throw new InvalidOperationException(\"dead entity\");");
                            writer.WriteLine("Entity = entity;");
                            for (int i = 1; i <= desc.Count; i++) writer.WriteLine($"Component{i} = ref entity.Get<T{i}>();");
                        }
                    }
                    writer.WriteLine();
                }
                ctx.AddSource("EntityViews.Structs.g.cs", writer.Builder.ToString());
            });

            context.RegisterPostInitializationOutput(ctx => {
                using var writer = new IndentedStringWriter();
                writer.WriteLine("using Signals.V2; \n\n namespace Signals.V2; \n");

                using (writer.BeginScope($"public static partial class EntityExt")) {
                    using (writer.BeginScope($"extension(Entity entity)")) {
                        foreach (var desc in descriptors) {
                            using (writer.BeginScope($"public EntityView<{desc.Parameters}> View<{desc.Parameters}>() {desc.Constraints}")) {
                                writer.WriteLine($"return new EntityView<{desc.Parameters}>(entity);");
                            }
                            writer.WriteLine();
                        }
                    }
                }
                ctx.AddSource("EntityViews.Extensions.g.cs", writer.Builder.ToString());
            });
        }
    } 
}