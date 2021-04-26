using Microsoft.CodeAnalysis;
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
    public class InterfaceGenerator : ISourceGenerator
    {
        private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );
        private static readonly SymbolDisplayFormat ImplementedTypeDisplayFormat = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
        );
        private static readonly SymbolDisplayFormat MethodDisplayFormat = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeParamsRefOut,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );
        private static readonly SymbolDisplayFormat PropertyDisplayFormat = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType,
            propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );
        private static readonly SymbolDisplayFormat EventsDisplayFormat = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            memberOptions: SymbolDisplayMemberOptions.IncludeType,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxContextReceiver is SyntaxReceiver receiver)
            {
                var types = receiver.Types.ToList();
                foreach (var interfaceSymbol in receiver.Interfaces)
                {
                    // if an interface is actually manually defined, don't autogenerate it
                    var match = types.FirstOrDefault(t => "I" + t.Name == interfaceSymbol.Name && SymbolEqualityComparer.Default.Equals(t.ContainingNamespace, interfaceSymbol.ContainingNamespace));
                    if (match != null)
                    {
                        types.Remove(match);
                    }
                }
                foreach (var type in types)
                {
                    var inheritedSymbols = Interfaces(type).SelectMany(t => t.GetMembers());
                    var publicMembers = type.GetMembers().Where(t => t.DeclaredAccessibility == Accessibility.Public && !inheritedSymbols.Any(i => i.ToDisplayString(MethodDisplayFormat) == t.ToDisplayString(MethodDisplayFormat)));
                    var methods = publicMembers.OfType<IMethodSymbol>().Where(t => t.MethodKind == MethodKind.Ordinary);
                    var properties = publicMembers.OfType<IPropertySymbol>();
                    var events = publicMembers.Where(t => t.Kind == SymbolKind.Event);
                    var baseTypeAutoInterface = types.Any(t => t.Name == type.BaseType?.Name);
                    var source = $@"
namespace {type.ContainingNamespace.ToDisplayString()}
{{
    public interface {BuildTypeName(type, baseTypeAutoInterface)}
    {{
        {string.Join(@"
        ", properties.Select(t => t.ToDisplayString(PropertyDisplayFormat)))}
        {string.Join(@"
        ", methods.Select(t => t.ToDisplayString(MethodDisplayFormat) + ";"))}
        {string.Join(@"
        ", events.Select(t => $"event {t.ToDisplayString(EventsDisplayFormat)};"))}
    }}
}}
";
                    File.WriteAllText($"C:\\temp\\JabGenerated_I{type.Name}.cs", source);
                    context.AddSource($"JabGenerated_I{type.Name}.cs", SourceText.From(source, Encoding.UTF8));
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> Interfaces(INamedTypeSymbol type) => type.Interfaces.Where(t => t.Name != $"I{type.Name}");
        private static IEnumerable<string> InterfaceNames(INamedTypeSymbol type) => Interfaces(type).Select(t => t.ToDisplayString(ImplementedTypeDisplayFormat));

        private static string BuildTypeName(INamedTypeSymbol type, bool baseTypeAutoInterface)
        {
            var sb = new StringBuilder("I");
            foreach (var part in type.ToDisplayParts(TypeDisplayFormat))
            {
                if (part.ToString() == "where" && (baseTypeAutoInterface || Interfaces(type).Any()))
                {
                    var interfaceNames = InterfaceNames(type).ToList();
                    if (baseTypeAutoInterface && type.BaseType != null)
                    {
                        var baseType = new StringBuilder();
                        foreach (var baseTypePart in type.BaseType.ToDisplayParts(ImplementedTypeDisplayFormat))
                        {
                            if (baseTypePart.Kind == SymbolDisplayPartKind.ClassName && baseTypePart.ToString() == type.BaseType.Name)
                            {
                                baseType.Append("I");
                            }
                            baseType.Append(baseTypePart.ToString());
                        }
                        interfaceNames.Insert(0, baseType.ToString());
                    }
                    sb.Append($": {string.Join(", ", interfaceNames)} ");
                }
                sb.Append(part.ToString());
            }
            return sb.ToString();
        }

        private class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<INamedTypeSymbol> Types = new();
            public List<INamedTypeSymbol> Interfaces = new();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is InterfaceDeclarationSyntax interfaceDeclarationSyntax
                    && context.SemanticModel.GetDeclaredSymbol(interfaceDeclarationSyntax) is INamedTypeSymbol interfaceSymbol)
                {
                    Interfaces.Add(interfaceSymbol);
                }
                if (context.Node is ClassDeclarationSyntax classDeclarationSyntax
                    && context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax) is INamedTypeSymbol type
                    // the compiler doesn't know if the first type after the ":" is an interface if it isn't defined yet so it shows up as the base type rather than in the interface list even if it starts with an "I"
                    && (type.BaseType?.Name == "I" + type.Name || type.Interfaces.Any(i => i.Name == "I" + type.Name)))
                {
                    Types.Add(type);
                }
            }
        }
    }
}
