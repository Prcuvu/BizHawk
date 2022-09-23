#nullable enable // for when this file is embedded

using System;

namespace BizHawk.Common
{
	[AttributeUsage(AttributeTargets.Method, Inherited = false)]
	public sealed class VirtualMethodAttribute : Attribute
	{
		/// <remarks>if unset, uses annotated method's name</remarks>
		public string? BaseImplMethodName { get; set; } = null;

		/// <remarks>if unset, uses <c>$"{interfaceFullName}.MethodDefaultImpls"</c></remarks>
		public string? ImplsClassFullName { get; set; } = null;
	}
}
