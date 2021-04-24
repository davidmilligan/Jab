using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Jab
{
    [Generator]
    public class ServiceRegistrationGenerator : ISourceGenerator
    {
        private const string Namespace = "Jab";
        private const string TransientAttribute = "Transient";
        private const string ScopedAttribute = "Scoped";
        private const string SingletontAttribute = "Singleton";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization(t => t
                .AddAttribute(Namespace, TransientAttribute, AttributeTargets.Class)
                .AddAttribute(Namespace, ScopedAttribute, AttributeTargets.Class)
                .AddAttribute(Namespace, SingletontAttribute, AttributeTargets.Class)
            );
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxContextReceiver is SyntaxReceiver receiver
                && receiver.GlobalNamespace != null)
            {
                var source = $@"
using Microsoft.Extensions.DependencyInjection;

namespace {receiver.GlobalNamespace}
{{
    public static class JabServiceRegistrations
    {{
        public static IServiceCollection Jab(this IServiceCollection services) => services{string.Join(@"
            ", receiver.Services.Select(s => $".Add{s.Lifetime}<{s.ServiceType.ToDisplayString()},{s.ImplementationType.ToDisplayString()}>()"))};
    }}
}}
";
                context.AddSource($"{receiver.GlobalNamespace}_JabServiceRegistrations.cs", SourceText.From(source, Encoding.UTF8));
            }
        }

        private class SyntaxReceiver : ISyntaxContextReceiver
        {
            public string? GlobalNamespace { get; private set; }
            public List<(INamedTypeSymbol ServiceType, INamedTypeSymbol ImplementationType, string Lifetime)> Services { get; } = new();
            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is TypeDeclarationSyntax typeSyntax
                    && typeSyntax.AttributeLists.Count > 0
                    && context.SemanticModel.GetDeclaredSymbol(typeSyntax) is INamedTypeSymbol type)
                {
                    var lifetime =
                        type.HasAttribute(Namespace, TransientAttribute) ? "Transient" :
                        type.HasAttribute(Namespace, ScopedAttribute) ? "Scoped" :
                        type.HasAttribute(Namespace, SingletontAttribute) ? "Singleton" :
                        null;
                    // TODO: issue diagnostic if more than one attribute is on the class
                    if (lifetime != null)
                    {
                        GlobalNamespace ??= type.ContainingAssembly.Name;
                        Services.Add((type, type, lifetime));
                        foreach (var serviceType in type.AllInterfaces)
                        {
                            Services.Add((serviceType, type, lifetime));
                        }
                    }
                }
            }
        }
    }
}
