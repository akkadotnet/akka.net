//-----------------------------------------------------------------------
// <copyright file="HasherActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Microsoft.Extensions.DependencyInjection;
using Samples.Akka.AspNetCore.Messages;
using Samples.Akka.AspNetCore.Services;

namespace Samples.Akka.AspNetCore.Actors
{
    // <HasherActor>
    public class HasherActor : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IServiceScope _scope;
        private readonly IHashService _hashService;

        public HasherActor(IServiceProvider sp)
        {
            _scope = sp.CreateScope();
            _hashService = _scope.ServiceProvider.GetRequiredService<IHashService>();

            Receive<string>(str =>
            {
                var hash = _hashService.Hash(str);
                Sender.Tell(new HashReply(hash, Self));
            });
        }

        protected override void PostStop()
        {
            _scope.Dispose();

            // _hashService should be disposed once the IServiceScope is disposed too
            _log.Info("Terminating. Is ScopedService disposed? {0}", _hashService.IsDisposed);
        }
    }
    // </HasherActor>
}
