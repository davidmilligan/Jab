using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Jab
{
    [Generator]
    public class JabGenerator : ISourceGenerator
    {
        public const string Namespace = "Jab";
        public const string AttributeName = "JabAttribute";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization(t => t.AddAttribute(Namespace, AttributeName, AttributeTargets.Field, AttributeTargets.Property, AttributeTargets.Class));
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxContextReceiver is SyntaxReceiver receiver)
            {
                var attributeSymbol = context.Compilation.GetTypeByMetadataName($"{Namespace}.{AttributeName}");

                foreach (var type in receiver
                    .Fields
                    .GroupBy(t => t.Symbol.ContainingType)
                    .Concat(receiver.Classes
                        .Where(t => !receiver.Fields.Any(f => SymbolEqualityComparer.Default.Equals(f.Symbol.ContainingType, t)))
                        .Select(t => new EmptyGrouping<INamedTypeSymbol, (ITypeSymbol, ISymbol)>(t))))
                {
                    // special cases (e.g. ILogger<T>)
                    string ParameterType(ITypeSymbol parameterType) => parameterType.ToDisplayString() switch
                    {
                        "Microsoft.Extensions.Logging.ILogger" => $"Microsoft.Extensions.Logging.ILogger<{type.Key.Name}>",
                        _ => parameterType.ToDisplayString()
                    };
                    // find parent constructor with least number of parameters
                    var baseTypes = new List<INamedTypeSymbol>();
                    for (var current = type.Key.BaseType; current != null; current = current.BaseType)
                    {
                        baseTypes.Add(current);
                    }
                    var baseParameters = baseTypes.SelectMany(t => receiver.Fields.Where(f => SymbolEqualityComparer.Default.Equals(t, f.Symbol.ContainingType)));
                    var source = $@"
namespace {type.Key.ContainingNamespace.ToDisplayString()}
{{
    public partial class {type.Key.Name}
    {{
        public {type.Key.Name}({string.Join(", ", type.Concat(baseParameters).Select(t => $"{ParameterType(t.Type)} {t.Symbol.Name}"))})
            : base({string.Join(", ", baseParameters.Select(t => t.Symbol.Name))})
        {{
            {string.Join(@"
            ", type.Select(t => $"this.{t.Symbol.Name} = {t.Symbol.Name};"))}
            OnInit();
        }}

        partial void OnInit();
    }}
}}
";
                    context.AddSource($"JabGenerated_{type.Key.Name}.cs", SourceText.From(source, Encoding.UTF8));
                }
            }
        }

        private class EmptyGrouping<TKey, TElement> : IGrouping<TKey, TElement>
        {
            public TKey Key { get; set; }

            public EmptyGrouping(TKey key) => Key = key;

            public IEnumerator<TElement> GetEnumerator() => Enumerable.Empty<TElement>().GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => Enumerable.Empty<TElement>().GetEnumerator();
        }

        private class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<INamedTypeSymbol> Classes = new();
            public List<(ITypeSymbol Type, ISymbol Symbol)> Fields = new();
            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is FieldDeclarationSyntax fieldDeclarationSyntax
                    && fieldDeclarationSyntax.AttributeLists.Count > 0)
                {
                    foreach (var variable in fieldDeclarationSyntax.Declaration.Variables)
                    {
                        if (context.SemanticModel.GetDeclaredSymbol(variable) is IFieldSymbol field
                            && field.HasAttribute(Namespace, AttributeName))
                        {
                            Fields.Add((field.Type, field));
                        }
                    }
                }
                else if (context.Node is PropertyDeclarationSyntax propertyDeclarationSyntax
                    && propertyDeclarationSyntax.AttributeLists.Count > 0
                    && context.SemanticModel.GetDeclaredSymbol(propertyDeclarationSyntax) is IPropertySymbol property
                    && property.HasAttribute(Namespace, AttributeName))
                {
                    Fields.Add((property.Type, property));
                }
                else if (context.Node is ClassDeclarationSyntax classDeclarationSyntax
                    && classDeclarationSyntax.AttributeLists.Count > 0
                    && context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax) is INamedTypeSymbol type
                    && type.HasAttribute(Namespace, AttributeName))
                {
                    Classes.Add(type);
                }
            }
        }
    }
}
