using System;
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
        private const string Namespace = "Jab";
        private const string AttributeName = "JabAttribute";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization(t => t.AddAttribute(Namespace, AttributeName, AttributeTargets.Field, AttributeTargets.Property));

            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxContextReceiver is SyntaxReceiver receiver)
            {
                var attributeSymbol = context.Compilation.GetTypeByMetadataName($"{Namespace}.{AttributeName}");

                foreach (var type in receiver.Fields.GroupBy(t => t.Symbol.ContainingType))
                {
                    var source = $@"
namespace {type.Key.ContainingNamespace.ToDisplayString()}
{{
    public partial class {type.Key.Name}
    {{
        public {type.Key.Name}({string.Join(", ", type.Select(t => $"{t.Type.ToDisplayString()} {t.Symbol.Name}"))})
        {{
            {string.Join(@"
            ", type.Select(t => $"this.{t.Symbol.Name} = {t.Symbol.Name};"))}
        }}
    }}
}}
";
                    context.AddSource($"JabGenerated_{type.Key.Name}.cs", SourceText.From(source, Encoding.UTF8));
                }
            }
        }

        private class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<(ITypeSymbol Type, ISymbol Symbol)> Fields = new();
            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                static bool HasAttribute(ISymbol symbol) => symbol?.GetAttributes().Any(t => t.AttributeClass?.ToDisplayString() == $"{Namespace}.{AttributeName}") == true;
                if (context.Node is FieldDeclarationSyntax fieldDeclarationSyntax
                    && fieldDeclarationSyntax.AttributeLists.Count > 0)
                {
                    foreach (var variable in fieldDeclarationSyntax.Declaration.Variables)
                    {
                        if (context.SemanticModel.GetDeclaredSymbol(variable) is IFieldSymbol field
                            && HasAttribute(field))
                        {
                            Fields.Add((field.Type, field));
                        }
                    }
                }
                else if (context.Node is PropertyDeclarationSyntax propertyDeclarationSyntax
                    && propertyDeclarationSyntax.AttributeLists.Count > 0
                    && context.SemanticModel.GetDeclaredSymbol(propertyDeclarationSyntax) is IPropertySymbol property
                    && HasAttribute(property))
                {
                    Fields.Add((property.Type, property));
                }
            }
        }
    }
}
