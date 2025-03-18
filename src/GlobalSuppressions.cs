// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// Suppress CS8632 warnings about nullable reference type annotations
[assembly: SuppressMessage("Style", "CS8632:The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.", Justification = "Files with #nullable enable directives use nullable annotations correctly")] 