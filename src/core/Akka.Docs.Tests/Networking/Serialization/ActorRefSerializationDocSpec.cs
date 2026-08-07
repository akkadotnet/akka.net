//-----------------------------------------------------------------------
// <copyright file="ActorRefSerializationDocSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Serialization;

namespace DocsExamples.Networking.Serialization
{
    public class ActorRefSerializationDocSpec
    {
        public void SerializeAndDeserializeActorRef(ActorSystem system, IActorRef theActorRef)
        {
            #region serialize-actorref
            // Serialize — use the absolute actor path string
            string path = Serialization.SerializedActorPath(theActorRef);

            // Then serialize `path` however you like (custom serializer, database, etc.)
            #endregion

            #region deserialize-actorref
            // Deserialize — prefer Serialization.DeserializeActorRef (available since Akka.NET v1.5.24)
            IActorRef deserializedActorRef = system.Serialization.DeserializeActorRef(path);
            #endregion

            // Equivalent lower-level call (still valid):
            // ((ExtendedActorSystem)system).Provider.ResolveActorRef(path);
            _ = deserializedActorRef;
        }
    }
}
