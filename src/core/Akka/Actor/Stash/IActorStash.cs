﻿//-----------------------------------------------------------------------
// <copyright file="IActorStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;

namespace Akka.Actor
{
    /// <summary>
    /// Marker interface for adding stash support
    /// </summary>
    public interface IActorStash
    {

        /// <summary>
        /// Gets or sets the stash. This will be automatically populated by the framework AFTER the constructor has been run.
        /// Implement this as an auto property.
        /// </summary>
        /// <value>
        /// The stash.
        /// </value>
        IStash Stash { get; set; }
    }
}

