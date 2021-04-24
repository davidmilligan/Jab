using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jab
{
    internal static class AttributeGenerator
    {
        public static GeneratorPostInitializationContext AddAttribute(this GeneratorPostInitializationContext context, string attributeNamespace, string name, params AttributeTargets[] targets)
        {
            context.AddSource(name, $@"
using System;
namespace {attributeNamespace}
{{
    [AttributeUsage({string.Join(" | ", targets.Select(t => $"AttributeTargets.{t}"))}, Inherited = false, AllowMultiple = false)]
    internal sealed class {name} : Attribute {{ }}
}}
");
            return context;
        }

        public static bool HasAttribute(this ISymbol symbol, string attributeNamespace, string name) => symbol?.GetAttributes().Any(t => t.AttributeClass?.ToDisplayString() == $"{attributeNamespace}.{name}") == true;
    }
}
