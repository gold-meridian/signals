using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Generator;

internal static class SystemRegistration
{
    private sealed record ParameterDescriptor(
        IParameterSymbol Parameter,
        RefKind RefKind
    );

    private sealed record SystemDescriptor(
        IMethodSymbol Method,
        ParameterDescriptor[] Parameters,
        List<ITypeSymbol> Without
    );

    [Generator]
    public sealed class Generator : IIncrementalGenerator
    {
        void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context)
        {
            var systemMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Signals.Systems.SystemAttribute",
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => (IMethodSymbol)ctx.TargetSymbol
            ).Collect().Combine(context.CompilationProvider);

            context.RegisterSourceOutput(
                systemMethods,
                EmitSystems
            );
        }
    }

    private static SystemDescriptor CreateSystemDescriptor(INamedTypeSymbol withoutAttribute, IMethodSymbol method)
    {
        var parameters = new ParameterDescriptor[method.Parameters.Length];
        var without = new List<ITypeSymbol>();

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            parameters[i] = new ParameterDescriptor(parameter, parameter.RefKind);
        }

        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeType)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(attributeType.ConstructedFrom, withoutAttribute))
            {
                without.Add(attributeType.TypeArguments[0]);
            }
        }

        return new SystemDescriptor(method, parameters, without);
    }

    private static void EmitSystems(SourceProductionContext ctx, (ImmutableArray<IMethodSymbol>, Compilation) pair)
    {
        var (systemMethods, compilation) = pair;

        var withoutAttributeSymbol = compilation.GetTypeByMetadataName("Signals.WithoutAttribute`1");
        if (withoutAttributeSymbol is null)
        {
            return;
        }

        var entitySymbol = compilation.GetTypeByMetadataName("Signals.Entity");
        if (entitySymbol is null)
        {
            return;
        }

        var commandsSymbol = compilation.GetTypeByMetadataName("Signals.Commands");
        if (commandsSymbol is null)
        {
            return;
        }

        if (systemMethods.IsDefaultOrEmpty)
        {
            return;
        }

        var systems = systemMethods.Select(x => CreateSystemDescriptor(withoutAttributeSymbol, x)).ToArray();

        using var writer = new IndentedStringWriter();

        writer.WriteLine("using System;");
        writer.WriteLine("using Signals;");
        writer.WriteLine();
        writer.WriteLine("namespace Signals;");
        writer.WriteLine();

        EmitDelegates(writer, systems);

        writer.WriteLine();
        using (writer.BeginScope($"internal static class SystemRegistrationExtensions")) { }

        foreach (var system in systems)
        {
            EmitExecutor(writer, system, entitySymbol, commandsSymbol);
        }

        ctx.AddSource("SignalsGeneratedSystems.g.cs", SourceText.From(writer.Builder.ToString(), Encoding.UTF8));
    }

    private static void EmitDelegates(IndentedStringWriter writer, SystemDescriptor[] systems)
    {
        var generated = new HashSet<string>();

        foreach (var system in systems)
        {
            var name = GetDelegateName(system);
            if (!generated.Add(name))
            {
                continue;
            }

            var parameters = string.Join(", ", system.Parameters.Select(x => $"{GetParameterKey(x, identifier: false)} {x.Parameter.Name}"));
            writer.WriteLine($"internal delegate void {name}({parameters});");
        }
    }

    private static void EmitExecutor(IndentedStringWriter writer, SystemDescriptor descriptor, INamedTypeSymbol entitySymbol, INamedTypeSymbol commandsSymbol)
    {
        var executorName = GetExecutorName(descriptor);

        using (writer.BeginScope($"internal static class {executorName}"))
        {
            using (writer.BeginScope($"public static void Execute(Delegate system, World world, Commands commands)"))
            {
                writer.WriteLine($"var typed = ({GetDelegateName(descriptor)})system;");

                writer.WriteLine("var query = world.Query()");

                foreach (var parameter in descriptor.Parameters)
                {
                    var type = parameter.Parameter.Type;

                    if (type.Name is "Entity" or "Commands")
                    {
                        continue;
                    }

                    writer.WriteLine($"    .With<{GetTypeKey(type, false)}>()");
                }

                foreach (var without in descriptor.Without)
                {
                    writer.WriteLine($"    .Without<{GetTypeKey(without, false)}>()");
                }

                writer.WriteLine("    .Iterate();");
                writer.WriteLine();

                using (writer.BeginScope($"while (query.Next() is {{ }} entity)"))
                {
                    writer.Write("typed(");

                    for (var i = 0; i < descriptor.Parameters.Length; i++)
                    {
                        if (i != 0)
                        {
                            writer.Write(", ");
                        }

                        var parameter = descriptor.Parameters[i];
                        var type = parameter.Parameter.Type;

                        var prefix = parameter.RefKind switch
                        {
                            RefKind.Ref => "ref ",
                            RefKind.In => "in ",
                            _ => "",
                        };

                        if (SymbolEqualityComparer.Default.Equals(type, entitySymbol))
                        {
                            writer.Write(prefix + "entity");
                            continue;
                        }

                        if (SymbolEqualityComparer.Default.Equals(type, commandsSymbol))
                        {
                            writer.Write(prefix + "commands");
                            continue;
                        }

                        writer.Write(prefix + $"entity.Get<{GetTypeKey(type, false)}>()");
                    }

                    writer.WriteLine(");");
                }
            }
        }
    }

    private static string GetExecutorName(SystemDescriptor descriptor)
    {
        return $"__Executor_{GetNameFromFullQuery(descriptor)}";
    }

    private static string GetDelegateName(SystemDescriptor descriptor)
    {
        return $"__Delegate_{GetNameFromParameters(descriptor)}";
    }

    private static string GetNameFromParameters(SystemDescriptor descriptor)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < descriptor.Parameters.Length; i++)
        {
            if (i > 0)
            {
                sb.Append("__");
            }

            sb.Append(GetParameterKey(descriptor.Parameters[i], identifier: true));
        }

        return sb.ToString();
    }

    private static string GetParameterKey(ParameterDescriptor parameter, bool identifier)
    {
        var space = identifier ? '_' : ' ';
        var prefix = parameter.RefKind switch
        {
            RefKind.Ref => "ref" + space,
            RefKind.In => "in" + space,
            RefKind.Out => "out" + space,
            _ => "",
        };

        return prefix + GetTypeKey(parameter.Parameter.Type, identifier);
    }

    private static string GetNameFromFullQuery(SystemDescriptor descriptor)
    {
        var paramPart = GetNameFromParameters(descriptor);
        var withoutPart = GetWithoutKey(descriptor, identifier: true);

        var sb = new StringBuilder(paramPart);

        var parts = new[] { withoutPart };
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            sb.Append("__");
            sb.Append(part);
        }

        return sb.ToString();
    }

    private static string GetTypeKey(ITypeSymbol symbol, bool identifier)
    {
        var key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));

        if (identifier)
        {
            key = key.Replace('.', '_');
        }

        return key;
    }

    private static string GetWithoutKey(SystemDescriptor descriptor, bool identifier)
    {
        if (descriptor.Without.Count == 0)
        {
            return string.Empty;
        }

        var ordered = descriptor.Without.Select(x => GetTypeKey(x, identifier)).OrderBy(x => x, StringComparer.Ordinal);
        return string.Join("__", ordered);
    }
}
