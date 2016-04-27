//-----------------------------------------------------------------------
// <copyright file="InitializableActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor.Internal
{
    /// <summary>
    /// Marks that the actor needs to be initialized directly after it has been created.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public interface IInitializableActor
    {
        void Init();
    }
}

