//-----------------------------------------------------------------------
// <copyright file="IPublicHashingService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Samples.Akka.AspNetCore.Messages;

namespace Samples.Akka.AspNetCore.Services
{
    /// <summary>
    /// Service meant to be exposed directly to ASP.NET Core HTTP routes
    /// </summary>
    public interface IPublicHashingService
    {
        Task<HashReply> Hash(string input, CancellationToken token);
    }
}
