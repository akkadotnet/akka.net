//-----------------------------------------------------------------------
// <copyright file="ExampleView.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Persistence;

namespace PersistenceExample
{
    public class ExampleView : PersistentView
    {
        private int _numReplicated = 0;

        public override string PersistenceId { get { return "sample-id-4"; } }
        public override string ViewId { get { return "sample-view-id-4"; } }
        
        protected override bool Receive(object message)
        {
            if (message as string == "snap")
            {
                Console.WriteLine("View saving snapshot");
                SaveSnapshot(_numReplicated);
            }
            else if (message is SnapshotOffer)
            {
                var offer = (SnapshotOffer) message;
                _numReplicated = Convert.ToInt32(offer.Snapshot);
                Console.WriteLine("View received snapshot offer {0} (metadata = {1})", _numReplicated, offer.Metadata);
            }
            else if (IsPersistent)
            {
                _numReplicated++;
                Console.WriteLine("View replayed event {0} (num replicated = {1})", message, _numReplicated);
            }
            else if (message is SaveSnapshotSuccess)
            {
                var fail = (SaveSnapshotSuccess) message;
                Console.WriteLine("View snapshot success (metadata = {0})", fail.Metadata);
            }
            else if (message is SaveSnapshotFailure)
            {
                var fail = (SaveSnapshotFailure) message;
                Console.WriteLine("View snapshot failure (metadata = {0}), caused by {1}", fail.Metadata, fail.Cause);
            }
            else
            {
                Console.WriteLine("View received other message " + message);
            }

            return true;
        }
    }
}

