//-----------------------------------------------------------------------
// <copyright file="Attributes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Annotations
{
    /// <summary>
    /// Marks APIs that are considered internal to Akka and may change at any point in time without any warning.
    /// 
    /// For example, this annotation should be used for code that should be inherently internal, but it cannot be
    /// due to limitations of .NET encapsulation in areas such as inheritance or serialization.
    /// 
    /// If a method/class annotated with this method has a xdoc comment, the first line MUST include 
    /// in order to be easily identifiable from generated documentation. Additional information
    /// may be put on the same line as the INTERNAL API comment in order to clarify further.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface | AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Module, Inherited = true, AllowMultiple = false)]
    public sealed class InternalApiAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks APIs that are meant to evolve towards becoming stable APIs, but are not stable APIs yet.
    /// 
    /// Evolving interfaces MAY change from one patch release to another (i.e. 1.3.0 to 1.3.1) without up-front notice.
    /// A best-effort approach is taken to not cause more breakage than really neccessary, and usual deprecation techniques 
    /// are utilised while evolving these APIs, however there is NO strong guarantee regarding the source or binary 
    /// compatibility of APIs marked using this annotation. 
    /// 
    /// It MAY also change when promoting the API to stable, for example such changes may include removal of deprecated 
    /// methods that were introduced during the evolution and final refactoring that were deferred because they would 
    /// have introduced to much breaking changes during the evolution phase. 
    /// 
    /// Promoting the API to stable MAY happen in a patch release.
    /// 
    /// It is encouraged to document in xmldoc how exactly this API is expected to evolve.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface | AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Module, Inherited = true, AllowMultiple = false)]
    public sealed class ApiMayChangeAttribute : Attribute
    {        
    }

    /// <summary>
    /// <para>
    /// Marks APIs that are designed under a closed-world assumption for and are NOT meant to be
    /// extended by user-code. It is fine to extend these classes within Akka itself however.
    /// </para>
    /// <para>
    /// This is most useful for binary compatibility purposes when a set of classes and interfaces
    /// assume a "closed world" between them, and gain the ability to add methods to the interfaces
    /// without breaking binary compatibility for users of this code. Specifically this assumption may be
    /// understood intuitively: as all classes that implement this interface are in this compilation unit
    /// artifact, it is impossible to obtain a "old" class with a "new" interface, as they are part of
    /// the same dependency.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true, AllowMultiple = false)]
    public sealed class DoNotInheritAttribute: Attribute
    {
    }
}
