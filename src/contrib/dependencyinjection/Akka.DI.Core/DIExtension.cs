//-----------------------------------------------------------------------
// <copyright file="DIExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// This class represents an <see cref="ActorSystem"/> provider used to create the dependency injection (DI) extension.
    /// </summary>
    public class DIExtension : ExtensionIdProvider<DIExt>
    {
        /// <summary>
        /// A static reference to the current provider.
        /// </summary>
        public static DIExtension DIExtensionProvider = new DIExtension();

        /// <summary>
        /// Creates the dependency injection extension using a given actor system.
        /// </summary>
        /// <param name="system">The actor system to use when creating the extension.</param>
        /// <returns>The extension created using the given actor system.</returns>
        public override DIExt CreateExtension(ExtendedActorSystem system)
        {
            var extension = new DIExt();
            return extension;
        }
    }
}
