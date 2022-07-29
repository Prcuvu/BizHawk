namespace BizHawk.SrcGen.ServiceProps;

using System;
using System.Collections.Generic;

using Microsoft.CodeAnalysis;

internal static class Extensions
{
	public static T? FirstOrNull<T>(this IEnumerable<T> list, Func<T, bool> predicate)
		where T : struct
	{
		foreach (var e in list) if (predicate(e)) return e;
		return null;
	}

	public static string FullNamespace(this ISymbol? sym)
	{
		if (sym is null) return string.Empty;
		var s = sym.Name;
		var ns = sym.ContainingNamespace;
		while (ns is { IsGlobalNamespace: false })
		{
			s = $"{ns.Name}.{s}";
			ns = ns.ContainingNamespace;
		}
		return s;
	}
}
