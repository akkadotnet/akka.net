//-----------------------------------------------------------------------
// <copyright file="AkkaService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region headless-akka-service

using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AkkaHeadlesssService
{
    public sealed class AkkaService : IHostedService
    {
        private IActorRef _actorRef;
        private ActorSystem _actorSystem;
        private readonly IServiceProvider _serviceProvider;

        private readonly IHostApplicationLifetime _applicationLifetime;

        public AkkaService(IServiceProvider sp, IHostApplicationLifetime applicationLifetime)
        {
            _serviceProvider = sp;
            _applicationLifetime = applicationLifetime;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var bootstrap = BootstrapSetup.Create();


            // enable DI support inside this ActorSystem, if needed
            var diSetup = DependencyResolverSetup.Create(_serviceProvider);

            // merge this setup (and any others) together into ActorSystemSetup
            var actorSystemSetup = bootstrap.And(diSetup);

            // start ActorSystem
            _actorSystem = ActorSystem.Create("headless-service", actorSystemSetup);
            _actorRef = _actorSystem.ActorOf(HeadlessActor.Prop());
            // add a continuation task that will guarantee shutdown of application if ActorSystem terminates
            _actorSystem.WhenTerminated.ContinueWith(_ => {
                _applicationLifetime.StopApplication();
            });
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // strictly speaking this may not be necessary - terminating the ActorSystem would also work
            // but this call guarantees that the shutdown of the cluster is graceful regardless
            await CoordinatedShutdown.Get(_actorSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
        }
    }
}

#endregion
