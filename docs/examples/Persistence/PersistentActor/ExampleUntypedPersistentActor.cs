using System;
using Akka.Persistence;

namespace DocsExamples.Persistence.PersistentActor
{
    public class ExampleUntypedPersistentActor : UntypedPersistentActor
    {
        private ExampleState _state = new ExampleState();

        private void UpdateState(Evt evt)
        {
            _state = _state.Updated(evt);
        }

        private int NumEvents => _state.Size;

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case Evt evt:
                    UpdateState(evt);
                    break;
                case SnapshotOffer snapshot when snapshot.Snapshot is ExampleState:
                    _state = (ExampleState)snapshot.Snapshot;
                    break;
            }
        }

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case Cmd cmd:
                    Persist(new Evt($"{cmd.Data}-{NumEvents}"), UpdateState);
                    Persist(new Evt($"{cmd.Data}-{NumEvents + 1}"), evt =>
                    {
                        UpdateState(evt);
                        Context.System.EventStream.Publish(evt);
                    });
                    break;
                case "snap":
                    SaveSnapshot(_state);
                    break;
                case "print":
                    Console.WriteLine(_state);
                    break;
            }
        }

        public override string PersistenceId { get; } = "sample-id-1";
    }
}
