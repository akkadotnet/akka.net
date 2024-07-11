//-----------------------------------------------------------------------
// <copyright file="AkkaService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region akka-windows-service
using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AkkaWindowsService
{
    public sealed class AkkaService : BackgroundService
    {
        private readonly ILogger<AkkaService> _logger;
        private readonly JokeService _jokeService;
        private readonly ActorSystem _actorSystem;
        private readonly IActorRef _actorRef;

        public AkkaService(JokeService jokeService, ILogger<AkkaService> logger, IServiceProvider serviceProvider)
        {
            _jokeService = jokeService;
            _logger = logger;
            var bootstrap = BootstrapSetup.Create();


            // enable DI support inside this ActorSystem, if needed
            var diSetup = DependencyResolverSetup.Create(serviceProvider);

            // merge this setup (and any others) together into ActorSystemSetup
            var actorSystemSetup = bootstrap.And(diSetup);

            // start ActorSystem
            _actorSystem = ActorSystem.Create("akka-system", actorSystemSetup);
            _actorRef = _actorSystem.ActorOf(MyActor.Prop());
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var joke = await _jokeService.GetJokeAsync();
                _logger.LogWarning(joke);

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                _actorRef.Tell(joke);
            }
        }
    }
}
#endregion
