//-----------------------------------------------------------------------
// <copyright file="IPublicHashingService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.Hosting;
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
