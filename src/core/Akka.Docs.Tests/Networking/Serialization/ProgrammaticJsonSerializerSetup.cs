//-----------------------------------------------------------------------
// <copyright file="ProgrammaticJsonSerializerSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Serialization;
using Newtonsoft.Json;

namespace DocsExamples.Networking.Serialization
{
    public class ProgrammaticJsonSerializerSetup
    {
        public ProgrammaticJsonSerializerSetup()
        {
            #region CustomJsonSetup
            var jsonSerializerSetup = NewtonSoftJsonSerializerSetup.Create(
                settings =>
                {
                    settings.NullValueHandling = NullValueHandling.Include;
                    settings.PreserveReferencesHandling = PreserveReferencesHandling.None;
                    settings.Formatting = Formatting.None;
                });

            var systemSetup = ActorSystemSetup.Create(jsonSerializerSetup);

            var system = ActorSystem.Create("MySystem", systemSetup);
            #endregion

            system.Terminate().Wait();
        }
    }
}
