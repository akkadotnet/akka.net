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

		[Serializable]
	    class ActorCellSlot : ISerializable
	    {
		    public ActorCell Cell;
			//Hack for xUnit
		    public void GetObjectData(SerializationInfo info, StreamingContext context)
		    {
			    var type = typeof (Exception);
			    info.AssemblyName = type.Assembly.FullName;
			    info.FullTypeName = type.FullName;
				new Exception("Why are you serializing me?").GetObjectData(info, context);
		    }
	    }
        /// <summary>INTERNAL!
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static ActorCell Current {
	        get
	        {
		        var slot = CallContext.LogicalGetData(Key) as ActorCellSlot;
		        return slot != null ? slot.Cell : null;
	        }
	        set
	        {
		        var slot = CallContext.LogicalGetData(Key) as ActorCellSlot;
		        if (slot == null)
			        CallContext.LogicalSetData(Key, new ActorCellSlot {Cell = value});
		        else
			        slot.Cell = value;
	        }
        }
    }
}

