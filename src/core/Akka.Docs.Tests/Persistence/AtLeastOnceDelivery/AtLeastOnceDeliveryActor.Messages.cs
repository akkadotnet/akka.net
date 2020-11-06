//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliveryActor.Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace DocsExamples.Persistence.AtLeastOnceDelivery
{
    #region AtLeastOnceDelivery
    public class Msg
    {
        public Msg(long deliveryId, string message)
        {
            DeliveryId = deliveryId;
            Message = message;
        }

        public long DeliveryId { get; }

        public string Message { get; }
    }

    public class Confirm
    {
        public Confirm(long deliveryId)
        {
            DeliveryId = deliveryId;
        }

        public long DeliveryId { get; }
    }

    public interface IEvent
    {

    }

    public class MsgSent : IEvent
    {
        public MsgSent(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    public class MsgConfirmed : IEvent
    {
        public MsgConfirmed(long deliveryId)
        {
            DeliveryId = deliveryId;
        }

        public long DeliveryId { get; }
    }
    #endregion
}
