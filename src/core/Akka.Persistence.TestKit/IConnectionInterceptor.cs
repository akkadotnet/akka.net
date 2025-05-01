//-----------------------------------------------------------------------
// <copyright file="IConnectionInterceptor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System.Threading.Tasks;

namespace Akka.Persistence.TestKit;

public interface IConnectionInterceptor
{
    Task InterceptAsync();
}
