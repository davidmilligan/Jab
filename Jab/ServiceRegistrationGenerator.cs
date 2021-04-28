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

        private static readonly DiagnosticDescriptor DuplicateAttributeRule = new(
                "JAB0001",
                "Duplicate Lifetime Attributes Not Allowed",
                "More than one lifetime attribute is specified on the type. A service may only have one type of lifetime.",
                "Usage",
                DiagnosticSeverity.Error,
                true
            );

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
                && receiver.AssemblyNamespace != null)
            {
                foreach (var duplicate in receiver.DuplicateAttributes.Distinct())
                {
                    context.ReportDiagnostic(Diagnostic.Create(DuplicateAttributeRule, duplicate));
                }
                var source = $@"
using Microsoft.Extensions.DependencyInjection;

namespace {receiver.AssemblyNamespace}
{{
    public static class JabServiceRegistrations
    {{
        public static IServiceCollection Jab(this IServiceCollection services) => services{string.Join(@"
            ", receiver.Services.Select(FormatServiceRegistration))};
    }}
}}
";
                context.AddSource($"{receiver.AssemblyNamespace}_JabServiceRegistrations.cs", SourceText.From(source, Encoding.UTF8));
            }
        }

        private static readonly SymbolDisplayFormat GenericServiceDisplayFormat = new(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.None
            );

        private static string FormatServiceRegistration((INamedTypeSymbol ServiceType, INamedTypeSymbol ImplementationType, string Lifetime) service)
        {
            if (service.ServiceType.IsGenericType)
            {
                return $".Add{service.Lifetime}(typeof({service.ServiceType.ToDisplayString(GenericServiceDisplayFormat)}<>), typeof({service.ImplementationType.ToDisplayString(GenericServiceDisplayFormat)}<>))";
            }
            else
            {
                return $".Add{service.Lifetime}<{service.ServiceType.ToDisplayString()},{service.ImplementationType.ToDisplayString()}>()";
            }
        }

        private class SyntaxReceiver : ISyntaxContextReceiver
        {
            public string? AssemblyNamespace { get; private set; }
            public List<(INamedTypeSymbol ServiceType, INamedTypeSymbol ImplementationType, string Lifetime)> Services { get; } = new();
            public List<Location> DuplicateAttributes { get; } = new();
            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is TypeDeclarationSyntax typeSyntax
                    && typeSyntax.AttributeLists.Count > 0
                    && context.SemanticModel.GetDeclaredSymbol(typeSyntax) is INamedTypeSymbol type)
                {
                    Location AttributeLocation() => typeSyntax.AttributeLists
                        .SelectMany(t => t.Attributes)
                        .LastOrDefault(t => t.Name.NormalizeWhitespace().ToFullString() == TransientAttribute || t.Name.NormalizeWhitespace().ToFullString() == ScopedAttribute || t.Name.NormalizeWhitespace().ToFullString() == SingletontAttribute)?
                        .GetLocation() ?? typeSyntax.GetLocation();
                    string? lifetime = null;
                    if (type.HasAttribute(Namespace, TransientAttribute))
                    {
                        lifetime = "Transient";
                    }
                    if (type.HasAttribute(Namespace, ScopedAttribute))
                    {
                        if (lifetime != null)
                        {
                            DuplicateAttributes.Add(AttributeLocation());
                        }
                        else
                        {
                            lifetime = "Scoped";
                        }
                    }
                    if (type.HasAttribute(Namespace, SingletontAttribute))
                    {
                        if (lifetime != null)
                        {
                            DuplicateAttributes.Add(AttributeLocation());
                        }
                        else
                        {
                            lifetime = "Singleton";
                        }
                    }

                    if (lifetime != null)
                    {
                        AssemblyNamespace ??= type.ContainingAssembly.Name;
                        Services.Add((type, type, lifetime));
                        foreach (var serviceType in type.AllInterfaces)
                        {
                            Services.Add((serviceType, type, lifetime));
                        }
                        // handle auto-generated interfaces
                        if (type.BaseType?.Name == "I" + type.Name)
                        {
                            Services.Add((type.BaseType, type, lifetime));
                        }
                    }
                }
            }
        }
    }
}
