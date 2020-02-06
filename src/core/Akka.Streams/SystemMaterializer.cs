//-----------------------------------------------------------------------
// <copyright file="ActorMaterializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Streams
{
    public sealed class SystemMaterializer : IExtension
    {
        private readonly ExtendedActorSystem _system;
        
        public IMaterializer Materializer { get; }
        
        public SystemMaterializer(ExtendedActorSystem system)
        {
            _system = system;
            var settings = ActorMaterializerSettings.Create(_system);
            Materializer = ActorMaterializer.SystemMaterializer(settings, "default", _system);
        }
    }
}
