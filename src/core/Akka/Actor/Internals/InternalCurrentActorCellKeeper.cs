//-----------------------------------------------------------------------
// <copyright file="InternalCurrentActorCellKeeper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Serialization;

namespace Akka.Actor.Internal
{
    /// <summary>INTERNAL!
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public static class InternalCurrentActorCellKeeper
    {
	    private static readonly string Key = Guid.NewGuid().ToString();
        /// <summary>INTERNAL!
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static ActorCell Current {
	        get
	        {
		        var r = CallContext.LogicalGetData(Key) as ActorCell.ReferenceHolder;
	            return r == null ? null : r.Cell;

	        }
	        set { CallContext.LogicalSetData(Key, value == null ? null : value.Reference); }
        }
    }
}

