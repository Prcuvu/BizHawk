namespace BizHawk.SrcGen.VIM;

using System.Collections.Generic;
using System.Linq;
using System.Text;

using BizHawk.Common;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

[Generator]
public sealed class VIMGenerator : ISourceGenerator
{
	private sealed class VIMGenSyntaxReceiver : ISyntaxReceiver
	{
		public readonly List<TypeDeclarationSyntax> Candidates = new();

		public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
		{
			if (syntaxNode is TypeDeclarationSyntax syn) Candidates.Add(syn);
		}
	}

	private class ImplNotes
	{
		public readonly string BaseImplNamePrefix;

		public readonly string InvokeCall;

		public readonly string MethodFullName;

		public readonly ISymbol MethodSym;

		public readonly string ReturnType;

		public ImplNotes(ISymbol intfSym, IMethodSymbol methodSym, AttributeData vimAttr)
		{
			string? baseImplMethodName = null;
			string? implsClassFullName = null;
			foreach (var kvp in vimAttr.NamedArguments) switch (kvp.Key)
			{
				case nameof(VirtualMethodAttribute.BaseImplMethodName):
					baseImplMethodName = kvp.Value.Value?.ToString();
					break;
				case nameof(VirtualMethodAttribute.ImplsClassFullName):
					implsClassFullName = kvp.Value.Value?.ToString();
					break;
			}
			if (string.IsNullOrEmpty(baseImplMethodName)) baseImplMethodName = methodSym.Name;
			if (string.IsNullOrEmpty(implsClassFullName)) implsClassFullName = $"{intfSym.FullNamespace()}.MethodDefaultImpls";
			BaseImplNamePrefix = $"{implsClassFullName}.{baseImplMethodName}";
			InvokeCall = $"(this{string.Concat(methodSym.Parameters.Select(static pSym => $", {pSym.Name}"))})";
			MethodFullName = $"{intfSym.FullNamespace()}.{methodSym.Name}({string.Join(", ", methodSym.Parameters.Select(static pSym => $"{pSym.Type.ToDisplayString()} {pSym.Name}"))})";
			MethodSym = methodSym;
			ReturnType = methodSym.ReturnType.ToDisplayString();
		}
	}

//	private static readonly DiagnosticDescriptor DiagNoEnum = new(
//		id: "BHI2000",
////		title: "Apply [VirtualMethod] to enums used with generator",
////		messageFormat: "Matching enum should have [VirtualMethod] to enable better analysis and codegen",
//		title: "debug",
//		messageFormat: "{0}",
//		category: "Usage",
//		defaultSeverity: DiagnosticSeverity.Warning,
//		isEnabledByDefault: true);

	public void Initialize(GeneratorInitializationContext context)
		=> context.RegisterForSyntaxNotifications(static () => new VIMGenSyntaxReceiver());

	public void Execute(GeneratorExecutionContext context)
	{
//		void DebugMsg(Location location, string msg)
//			=> context.ReportDiagnostic(Diagnostic.Create(DiagNoEnum, location, msg));

		if (context.SyntaxReceiver is not VIMGenSyntaxReceiver receiver) return;

		// boilerplate to get attr working
		var compilation = context.Compilation;
		var vimAttrSymbol = compilation.GetTypeByMetadataName("BizHawk.Common." + nameof(VirtualMethodAttribute));
		if (vimAttrSymbol is null)
		{
			var attributesSource = SourceText.From(typeof(VIMGenerator).Assembly.GetManifestResourceStream("BizHawk.SrcGen.VIM.VirtualMethodAttribute.cs")!, Encoding.UTF8, canBeEmbedded: true);
			context.AddSource("VirtualMethodAttribute.cs", attributesSource);
			compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(attributesSource, (CSharpParseOptions) ((CSharpCompilation) context.Compilation).SyntaxTrees[0].Options));
			vimAttrSymbol = compilation.GetTypeByMetadataName("BizHawk.Common." + nameof(VirtualMethodAttribute))!;
		}

		Dictionary<string, List<ImplNotes>> vimDict = new();
		List<ImplNotes> Lookup(INamedTypeSymbol intfSym)
		{
			var fqn = intfSym.FullNamespace();
			if (vimDict.TryGetValue(fqn, out var implNotes)) return implNotes;
			// else cache miss
			List<ImplNotes> implNotes1 = new();
			foreach (var methodSym in intfSym.GetMembers())
			{
				var vimAttr = methodSym.GetAttributes().FirstOrDefault(ad => vimAttrSymbol.Matches(ad.AttributeClass));
				if (vimAttr is not null) implNotes1.Add(new(intfSym: intfSym, methodSym: (IMethodSymbol) methodSym, vimAttr));
			}
			return vimDict[fqn] = implNotes1;
		}

		List<INamedTypeSymbol> seen = new();
		foreach (var tds in receiver.Candidates)
		{
			var cSym = compilation.GetSemanticModel(tds.SyntaxTree).GetDeclaredSymbol(tds)!;
			if (seen.Contains(cSym)) continue; // dedup partial classes
			seen.Add(cSym);
			var typeKeywords = tds.GetTypeKeywords(cSym);
			if (typeKeywords.Contains("enum") || typeKeywords.Contains("interface") || typeKeywords.Contains("static")) continue;

			List<string> innerText = new();
			foreach (var intfSym in cSym.BaseType is not null
				? cSym.AllInterfaces.Except(cSym.BaseType.AllInterfaces) // superclass (or its superclass, etc.) already has the delegated base implementations of these interfaces' virtual methods
				: cSym.AllInterfaces)
			{
				//TODO let an interface override a superinterface's virtual method -- may need to order above enumerable somehow
				foreach (var method in Lookup(intfSym))
				{
					if (cSym.FindImplementationForInterfaceMember(method.MethodSym) is not null) continue; // overridden
					innerText.Add($@"{method.ReturnType} {method.MethodFullName}
			=> {method.BaseImplNamePrefix}{method.InvokeCall};"); // set up this way so I can whack a "_get"/"_set" before the '(' for virtual props
				}
			}
			if (innerText.Count is not 0) context.AddSource(
				source: $@"#nullable enable

namespace {cSym.ContainingNamespace.ToDisplayString()}
{{
	public {string.Join(" ", typeKeywords)} {cSym.Name}
	{{
		{string.Join("\n\n", innerText)}
	}}
}}
",
				hintName: $"{cSym.Name}.VIMDelegation.cs");
		}
	}
}
