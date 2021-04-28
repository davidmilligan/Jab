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

        private static readonly DiagnosticDescriptor MissingServiceRule = new(
                "JAB0002",
                "Dependency Not Found",
                "A required dependency '{0}' does not appear to be registered. Did you forget to register it?",
                "Usage",
                DiagnosticSeverity.Warning,
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
                var alreadyChecked = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach (var service in receiver.Services)
                {
                    if (!alreadyChecked.Contains(service.ImplementationType))
                    {
                        alreadyChecked.Add(service.ImplementationType);
                        foreach (var constructorParameter in service.ImplementationType.InstanceConstructors.SelectMany(t => t.Parameters))
                        {
                            if (!receiver.Services.Any(t => SymbolEqualityComparer.Default.Equals(t.ServiceType, constructorParameter.Type)))
                            {
                                var location = service.Syntax.Members.OfType<ConstructorDeclarationSyntax>().SelectMany(t => t.ParameterList.Parameters).FirstOrDefault(t => t.Type.ToString() == constructorParameter?.Type?.Name && t.Identifier.ToString() == constructorParameter.Name)?.Type.GetLocation();
                                context.ReportDiagnostic(Diagnostic.Create(MissingServiceRule, location ?? service.Syntax.GetLocation(), constructorParameter.Type?.ToDisplayString()));
                            }
                        }
                        foreach (var (symbol, serviceType) in service.ImplementationType.GetMembers().Where(t => t.HasAttribute(JabGenerator.Namespace, JabGenerator.AttributeName)).Select(t => (t, t is IPropertySymbol p ? p.Type : t is IFieldSymbol f ? f.Type : null)))
                        {
                            if (!receiver.Services.Any(t => SymbolEqualityComparer.Default.Equals(t.ServiceType, serviceType)))
                            {
                                var location = service.Syntax.Members.OfType<PropertyDeclarationSyntax>().FirstOrDefault(t => t.Identifier.ToString() == symbol.Name)?.Type.GetLocation() ??
                                    service.Syntax.Members.OfType<FieldDeclarationSyntax>().FirstOrDefault(t => t.Declaration.Variables.Any(v => v.Identifier.ToString() == symbol.Name))?.Declaration.Type.GetLocation();
                                context.ReportDiagnostic(Diagnostic.Create(MissingServiceRule, location ?? service.Syntax.GetLocation(), serviceType?.ToDisplayString()));
                            }
                        }
                    }
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

        private static string FormatServiceRegistration((INamedTypeSymbol ServiceType, INamedTypeSymbol ImplementationType, string Lifetime, TypeDeclarationSyntax Syntax) service)
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
            public List<(INamedTypeSymbol ServiceType, INamedTypeSymbol ImplementationType, string Lifetime, TypeDeclarationSyntax Syntax)> Services { get; } = new();
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
                        Services.Add((type, type, lifetime, typeSyntax));
                        foreach (var serviceType in type.AllInterfaces)
                        {
                            Services.Add((serviceType, type, lifetime, typeSyntax));
                        }
                        // handle auto-generated interfaces
                        if (type.BaseType?.Name == "I" + type.Name)
                        {
                            Services.Add((type.BaseType, type, lifetime, typeSyntax));
                        }
                    }
                }
            }
        }
    }
}
