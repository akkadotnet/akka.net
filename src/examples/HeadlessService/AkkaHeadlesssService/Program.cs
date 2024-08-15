﻿//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region headless-service-program

// See https://aka.ms/new-console-template for more information
using AkkaHeadlesssService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddLogging();
                    services.AddHostedService<AkkaService>();

                })
                .ConfigureLogging((_, configLogging) =>
                {
                    configLogging.AddConsole();

                })
                .UseConsoleLifetime()
                .Build();

await host.RunAsync();
#endregion
