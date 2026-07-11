using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Generator;

internal static class SystemRegistration {
    private sealed record ParameterDescriptor(
        IParameterSymbol Parameter,
        RefKind RefKind,
        ParameterKind Kind
    );

    private enum ParameterKind {
        Component,
        Entity,
        Commands,
        Resource,
        MutableResource
    }

    private sealed record SystemDescriptor(
        IMethodSymbol Method,
        ParameterDescriptor[] Parameters,
        List<ITypeSymbol> Without
    );

    [Generator]
    public sealed class Generator : IIncrementalGenerator {
        void IIncrementalGenerator.Initialize(IncrementalGeneratorInitializationContext context) {
            var systemMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Signals.Systems.SystemAttribute",
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => (IMethodSymbol)ctx.TargetSymbol
            ).Collect().Combine(context.CompilationProvider);

            context.RegisterSourceOutput(systemMethods, EmitSystems);
        }
    }

    private static ParameterKind GetParameterKind(
        ITypeSymbol type,
        INamedTypeSymbol? resType,
        INamedTypeSymbol? mutType,
        INamedTypeSymbol entitySymbol,
        INamedTypeSymbol commandsSymbol
    ) {
        if (SymbolEqualityComparer.Default.Equals(type, entitySymbol))
            return ParameterKind.Entity;
        
        if (SymbolEqualityComparer.Default.Equals(type, commandsSymbol))
            return ParameterKind.Commands;

        if (type is INamedTypeSymbol namedType) {
            if (resType != null && SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, resType))
                return ParameterKind.Resource;
            
            if (mutType != null && SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, mutType))
                return ParameterKind.MutableResource;
        }

        return ParameterKind.Component;
    }

    private static SystemDescriptor CreateSystemDescriptor(
        INamedTypeSymbol withoutAttribute,
        IMethodSymbol method,
        INamedTypeSymbol? resType,
        INamedTypeSymbol? mutType,
        INamedTypeSymbol entitySymbol,
        INamedTypeSymbol commandsSymbol
    ) {
        var parameters = new ParameterDescriptor[method.Parameters.Length];
        var without = new List<ITypeSymbol>();

        for (var i = 0; i < method.Parameters.Length; i++) {
            var parameter = method.Parameters[i];
            var kind = GetParameterKind(parameter.Type, resType, mutType, entitySymbol, commandsSymbol);
            parameters[i] = new ParameterDescriptor(parameter, parameter.RefKind, kind);
        }

        foreach (var attribute in method.GetAttributes()) {
            if (attribute.AttributeClass is not { } attributeType)
                continue;

            if (SymbolEqualityComparer.Default.Equals(attributeType.ConstructedFrom, withoutAttribute)) {
                without.Add(attributeType.TypeArguments[0]);
            }
        }

        return new SystemDescriptor(method, parameters, without);
    }

    private static void EmitSystems(
        SourceProductionContext ctx,
        (ImmutableArray<IMethodSymbol>, Compilation) pair
    ) {
        var (systemMethods, compilation) = pair;

        var withoutAttributeSymbol = compilation.GetTypeByMetadataName("Signals.WithoutAttribute`1");
        if (withoutAttributeSymbol is null)
            return;

        var entitySymbol = compilation.GetTypeByMetadataName("Signals.Entity");
        if (entitySymbol is null)
            return;

        var commandsSymbol = compilation.GetTypeByMetadataName("Signals.Commands");
        if (commandsSymbol is null)
            return;

        var resSymbol = compilation.GetTypeByMetadataName("Signals.Systems.Res`1");
        var mutSymbol = compilation.GetTypeByMetadataName("Signals.Systems.Mut`1");

        if (systemMethods.IsDefaultOrEmpty)
            return;

        var queryIds = new Dictionary<string, int>();
        var systems = systemMethods.Select(x => CreateSystemDescriptor(
            withoutAttributeSymbol, x, resSymbol, mutSymbol, entitySymbol, commandsSymbol
        )).ToArray();

        using var writer = new IndentedStringWriter();

        writer.WriteLine("using System;");
        writer.WriteLine("using System.Reflection;");
        writer.WriteLine("using Signals;");
        writer.WriteLine("using Signals.Systems;");
        writer.WriteLine();

        using (writer.BeginScope($"namespace Signals")) {
            EmitDelegates(writer, systems);

            var generated = new HashSet<string>();
            foreach (var system in systems) {
                EmitExecutor(writer, system, entitySymbol, commandsSymbol, resSymbol, mutSymbol, generated);
                writer.WriteLine();
            }

            EmitRegistrationExtensions(writer, systems, queryIds);
        }

        foreach (var system in systems) {
            writer.WriteLine();
            EmitNamespace(writer, system.Method.ContainingType, () => {
                EmitContainingTypes(writer, system.Method.ContainingType, () => {
                    EmitBindingMethod(writer, system, GetQueryId(queryIds, system));
                });
            });
        }

        ctx.AddSource("SignalsGeneratedSystems.g.cs", SourceText.From(writer.Builder.ToString(), Encoding.UTF8));
    }

    private static void EmitRegistrationExtensions(
        IndentedStringWriter writer,
        SystemDescriptor[] systems,
        Dictionary<string, int> queryIds
    ) {
        using (writer.BeginScope($"internal static class SystemRegistrationExtensions")) {
            var first = true;
            foreach (var signatureGroup in systems.GroupBy(GetDelegateName)) {
                if (!first)
                    writer.WriteLine();
                else
                    first = false;

                EmitAddSystemOverload(writer, signatureGroup.ToArray(), queryIds);
            }
        }
    }

    private static void EmitAddSystemOverload(
        IndentedStringWriter writer,
        SystemDescriptor[] systems,
        Dictionary<string, int> queryIds
    ) {
        var delegateName = GetDelegateName(systems[0]);

        using (writer.BeginScope($"internal static SystemConfigurator AddSystem(this App app, {delegateName} system)")) {
            writer.WriteLine("var binding = system.Method.GetCustomAttribute<GeneratedSystemBindingAttribute>();");
            using (writer.BeginScope($"if (binding is null)")) {
                writer.WriteLine("throw new InvalidOperationException(\"Missing GeneratedSystemBindingAttribute.\");");
            }

            writer.WriteLine();

            using (writer.BeginScope($"switch (binding.QueryId)")) {
                var handledIds = new HashSet<int>();

                foreach (var system in systems) {
                    var queryId = GetQueryId(queryIds, system);
                    if (!handledIds.Add(queryId))
                        continue;

                    writer.WriteLine($"case {queryId}:");
                    writer.Indent++;
                    writer.WriteLine($"return app.AddGeneratedSystem(system, {GetExecutorName(system)}.Execute);");
                    writer.Indent--;
                }

                writer.WriteLine("default:");
                writer.Indent++;
                writer.WriteLine("throw new InvalidOperationException(\"Unknown generated query id.\");");
                writer.Indent--;
            }
        }
    }

    private static void EmitNamespace(IndentedStringWriter writer, INamedTypeSymbol type, Action action) {
        var ns = type.ContainingNamespace;

        if (ns.IsGlobalNamespace) {
            action();
        } else {
            using (writer.BeginScope($"namespace {ns.ToDisplayString()}")) {
                action();
            }
        }
    }

    private static ImmutableArray<INamedTypeSymbol> GetContainingTypes(INamedTypeSymbol type) {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

        while (type is not null) {
            builder.Add(type);
            type = type.ContainingType;
        }

        builder.Reverse();
        return builder.ToImmutable();
    }

    private static string GetTypeDeclaration(INamedTypeSymbol type) {
        var kind = type.TypeKind switch {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ => "class",
        };

        if (type.IsRecord)
            kind = "record " + kind;

        var generics = type.TypeParameters.Length == 0
            ? ""
            : "<" + string.Join(", ", type.TypeParameters.Select(x => x.Name)) + ">";

        return $"partial {kind} {type.Name}{generics}";
    }

    private static void EmitContainingTypes(IndentedStringWriter writer, INamedTypeSymbol type, Action action) {
        var scopes = new List<IndentedStringWriter.Scope>();

        foreach (var containingType in GetContainingTypes(type)) {
            scopes.Add(writer.BeginScope($"{GetTypeDeclaration(containingType)}"));
        }

        action();

        foreach (var scope in scopes.AsEnumerable().Reverse()) {
            scope.Dispose();
        }
    }

    private static string GetParameterDeclaration(IParameterSymbol parameter) {
        var modifier = parameter.RefKind switch {
            RefKind.Ref => "ref ",
            RefKind.In => "in ",
            RefKind.Out => "out ",
            _ => "",
        };

        return $"{modifier}{parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {parameter.Name}";
    }

    private static int GetQueryId(Dictionary<string, int> queryIds, SystemDescriptor system) {
        var key = GetNameFromFullQuery(system);

        if (queryIds.TryGetValue(key, out var id))
            return id;

        id = queryIds.Count;
        queryIds.Add(key, id);
        return id;
    }

    private static void EmitBindingMethod(IndentedStringWriter writer, SystemDescriptor system, int queryId) {
        var method = system.Method;
        var staticText = method.IsStatic ? "static " : "";

        writer.WriteLine($"[GeneratedSystemBinding({queryId})]");

        var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var parameters = string.Join(", ", method.Parameters.Select(GetParameterDeclaration));

        writer.WriteLine($"public {staticText}partial {returnType} {method.Name}({parameters});");
    }

    private static void EmitDelegates(IndentedStringWriter writer, SystemDescriptor[] systems) {
        var generated = new HashSet<string>();

        foreach (var system in systems) {
            var name = GetDelegateName(system);
            if (!generated.Add(name))
                continue;

            var parameters = string.Join(
                ", ",
                system.Parameters.Select(x =>
                    $"{GetParameterKey(x, identifier: false)} {x.Parameter.Name}"
                )
            );
            writer.WriteLine($"internal delegate void {name}({parameters});");
            writer.WriteLine();
        }
    }

    private static void EmitExecutor(
        IndentedStringWriter writer,
        SystemDescriptor descriptor,
        INamedTypeSymbol entitySymbol,
        INamedTypeSymbol commandsSymbol,
        INamedTypeSymbol? resSymbol,
        INamedTypeSymbol? mutSymbol,
        HashSet<string> generated
    ) {
        var executorName = GetExecutorName(descriptor);
        if (!generated.Add(executorName))
            return;

        var needsEntityIteration = descriptor.Parameters.Any(p => {
            return p.Kind switch {
                ParameterKind.Entity or ParameterKind.Component => true,
                ParameterKind.Commands or ParameterKind.Resource or ParameterKind.MutableResource => false,
                _ => false
            };
        });

        using (writer.BeginScope($"internal static class {executorName}")) {
            using (writer.BeginScope($"public static void Execute(Delegate system, World world, Commands commands)")) {
                writer.WriteLine($"var typed = ({GetDelegateName(descriptor)})system;");

                if (!needsEntityIteration) {
                    writer.Write("typed(");

                    for (var i = 0; i < descriptor.Parameters.Length; i++) {
                        if (i != 0)
                            writer.Write(", ");

                        var parameter = descriptor.Parameters[i];

                        switch (parameter.Kind) {
                            case ParameterKind.Commands:
                                writer.Write("commands");
                                break;
                            case ParameterKind.Resource:
                                var resTypeArg = GetResourceTypeArgument(parameter.Parameter.Type);
                                writer.Write(
                                    $"new global::Signals.Systems.Res<{resTypeArg}>(world.Resources.Get<{resTypeArg}>())"
                                );
                                break;
                            case ParameterKind.MutableResource:
                                var mutTypeArg = GetResourceTypeArgument(parameter.Parameter.Type);
                                writer.Write(
                                    $"new global::Signals.Systems.Mut<{mutTypeArg}> {{ Value = world.Resources.Get<{mutTypeArg}>() }}"
                                );
                                break;
                        }
                    }

                    writer.WriteLine(");");
                } else {
                    writer.WriteLine("var query = world.Query()");

                    foreach (var parameter in descriptor.Parameters) {
                        if (parameter.Kind != ParameterKind.Component)
                            continue;

                        writer.WriteLine(
                            $"    .With<{GetTypeKey(parameter.Parameter.Type, false)}>()");
                    }

                    foreach (var without in descriptor.Without) {
                        writer.WriteLine($"    .Without<{GetTypeKey(without, false)}>()");
                    }

                    writer.WriteLine("    .Iterate();");
                    writer.WriteLine();

                    using (writer.BeginScope($"while (query.Next() is {{ }} entity)")) {
                        writer.Write("typed(");

                        for (var i = 0; i < descriptor.Parameters.Length; i++) {
                            if (i != 0)
                                writer.Write(", ");

                            var parameter = descriptor.Parameters[i];

                            var prefix = parameter.RefKind switch {
                                RefKind.Ref => "ref ",
                                RefKind.In => "in ",
                                _ => "",
                            };

                            switch (parameter.Kind) {
                                case ParameterKind.Entity:
                                    writer.Write(prefix + "entity");
                                    break;
                                case ParameterKind.Commands:
                                    writer.Write(prefix + "commands");
                                    break;
                                case ParameterKind.Component:
                                    writer.Write(
                                        prefix + $"entity.Get<{GetTypeKey(parameter.Parameter.Type, false)}>()"
                                    );
                                    break;
                                case ParameterKind.Resource:
                                    var resTypeArg = GetResourceTypeArgument(parameter.Parameter.Type);
                                    writer.Write(
                                        $"new global::Signals.Systems.Res<{resTypeArg}>(world.Resources.Get<{resTypeArg}>())"
                                    );
                                    break;
                                case ParameterKind.MutableResource:
                                    var mutTypeArg = GetResourceTypeArgument(parameter.Parameter.Type);
                                    writer.Write(
                                        $"new global::Signals.Systems.Mut<{mutTypeArg}> {{ Value = world.Resources.Get<{mutTypeArg}>() }}"
                                    );
                                    break;
                            }
                        }

                        writer.WriteLine(");");
                    }
                }
            }
        }
    }

    private static string GetResourceTypeArgument(ITypeSymbol type) {
        if (type is INamedTypeSymbol named && named.TypeArguments.Length > 0) {
            return named.TypeArguments[0].ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
        }
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string GetExecutorName(SystemDescriptor descriptor) {
        return $"__Executor_{GetNameFromFullQuery(descriptor)}";
    }

    private static string GetDelegateName(SystemDescriptor descriptor) {
        return $"__Delegate_{GetNameFromParameters(descriptor)}";
    }

    private static string GetNameFromParameters(SystemDescriptor descriptor) {
        var sb = new StringBuilder();

        for (var i = 0; i < descriptor.Parameters.Length; i++) {
            if (i > 0)
                sb.Append("__");

            sb.Append(GetParameterKey(descriptor.Parameters[i], identifier: true));
        }

        return sb.ToString();
    }

    private static string GetParameterKey(ParameterDescriptor parameter, bool identifier) {
        var space = identifier ? '_' : ' ';
        var prefix = parameter.RefKind switch {
            RefKind.Ref => "ref" + space,
            RefKind.In => "in" + space,
            RefKind.Out => "out" + space,
            _ => "",
        };

        return prefix + GetTypeKey(parameter.Parameter.Type, identifier);
    }

    private static string GetNameFromFullQuery(SystemDescriptor descriptor) {
        var paramPart = GetNameFromParameters(descriptor);
        var withoutPart = GetWithoutKey(descriptor, identifier: true);

        var sb = new StringBuilder(paramPart);

        var parts = new[] { withoutPart };
        foreach (var part in parts) {
            if (string.IsNullOrEmpty(part))
                continue;

            sb.Append("__");
            sb.Append(part);
        }

        return sb.ToString();
    }

    private static string GetTypeKey(ITypeSymbol symbol, bool identifier) {
        var key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));

        if (identifier) {
            key = key.Replace('<', '_')
                    .Replace('>', '_')
                    .Replace(',', '_')
                    .Replace(' ', '_')
                    .Replace('.', '_');
        }

        return key;
    }

    private static string GetWithoutKey(SystemDescriptor descriptor, bool identifier) {
        if (descriptor.Without.Count == 0)
            return string.Empty;

        var ordered = descriptor.Without
            .Select(x => GetTypeKey(x, identifier))
            .OrderBy(x => x, StringComparer.Ordinal);
        return string.Join("__", ordered);
    }
}