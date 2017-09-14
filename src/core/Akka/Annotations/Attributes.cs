//-----------------------------------------------------------------------
// <copyright file="Attributes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public class InternalApiAttribute : Attribute
    {
    }
}