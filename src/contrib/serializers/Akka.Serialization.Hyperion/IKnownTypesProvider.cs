//-----------------------------------------------------------------------
// <copyright file="IKnownTypesProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Serialization
{
    /// <summary>
    /// Interface that can be implemented in order to determine some 
    /// custom logic, that's going to provide a list of types that 
    /// are known to be shared for all corresponding parties during 
    /// remote communication.
    /// </summary>
    public interface IKnownTypesProvider
    {
        IEnumerable<Type> GetKnownTypes();
    }

    internal sealed class NoKnownTypes : IKnownTypesProvider
    {
        public IEnumerable<Type> GetKnownTypes() => new Type[0];
    }
}
