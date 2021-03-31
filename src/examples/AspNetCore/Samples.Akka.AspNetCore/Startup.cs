//-----------------------------------------------------------------------
// <copyright file="Startup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Samples.Akka.AspNetCore.Actors;
using Samples.Akka.AspNetCore.Services;

namespace Samples.Akka.AspNetCore
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        // <DiSetup>
        public void ConfigureServices(IServiceCollection services)
        {
            // set up a simple service we're going to hash
            services.AddScoped<IHashService, HashServiceImpl>();

            // creates instance of IPublicHashingService that can be accessed by ASP.NET
            services.AddSingleton<IPublicHashingService, AkkaService>();

            // starts the IHostedService, which creates the ActorSystem and actors
            services.AddHostedService<AkkaService>(sp => (AkkaService)sp.GetRequiredService<IPublicHashingService>());

        }
        // </DiSetup>

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var hashing = context.RequestServices.GetRequiredService<IPublicHashingService>();

                    var hash = await hashing.Hash(context.TraceIdentifier, cts.Token);

                    await context.Response.WriteAsync(
                        $"Actor [{hash.Hasher}] hashed TraceIdentifier [{context.TraceIdentifier}] into [{hash.Hash}]");
                });
            });
        }
    }
}
