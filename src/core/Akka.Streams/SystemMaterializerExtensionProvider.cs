//-----------------------------------------------------------------------
// <copyright file="ActorMaterializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Streams
{
    public sealed class SystemMaterializerExtensionProvider : ExtensionIdProvider<SystemMaterializer>
    {
        public override SystemMaterializer CreateExtension(ExtendedActorSystem system)
        {
            return new SystemMaterializer(system);
        }
    }
}
