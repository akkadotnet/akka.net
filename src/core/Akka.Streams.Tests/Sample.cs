//-----------------------------------------------------------------------
// <copyright file="Sample.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;

namespace Akka.Streams.Tests
{
    public class Sample
    {
        public static async Task Main()
        {
            var text = @"
                Lorem Ipsum is simply dummy text of the printing and typesetting industry.
                Lorem Ipsum has been the industry's standard dummy text ever since the 1500s,
                when an unknown printer took a galley of type and scrambled it to make a type
                specimen book.";

            using (var system = ActorSystem.Create("streams-example"))
            using (var materializer = system.Materializer())
            {
                await Source
                    .From(text)
                    .Map(char.ToUpper) 
                    .RunForeach(Console.WriteLine, materializer);
            }
        }
    }
}