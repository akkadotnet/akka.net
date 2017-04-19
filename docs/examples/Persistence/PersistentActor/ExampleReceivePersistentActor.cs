using System;
using Akka.Persistence;

namespace DocsExamples.Persistence.PersistentActor
{
    public class ExampleReceivePersistentActor : ReceivePersistentActor
    {
        private ExampleState _state = new ExampleState();

        private void UpdateState(Evt evt)
        {
            _state = _state.Updated(evt);
        }

        private int NumEvents => _state.Size;

        public ExampleReceivePersistentActor()
        {
            Recover<Evt>(evt =>
            {
                UpdateState(evt);
            });

            Recover<SnapshotOffer>(snapshot =>
            {
                _state = (ExampleState)snapshot.Snapshot;
            });

            Command<Cmd>(cmd =>
            {
                Persist(new Evt($"{cmd.Data}-{NumEvents}"), UpdateState);
                Persist(new Evt($"{cmd.Data}-{NumEvents + 1}"), evt =>
                {
                    UpdateState(evt);
                    Context.System.EventStream.Publish(evt);
                });
            });

            Command<string>(msg => msg == "snap", message =>
            {
                SaveSnapshot(_state);
            });

            Command<string>(msg => msg == "print", message =>
            {
                Console.WriteLine(_state);
            });
        }

        public override string PersistenceId { get; } = "sample-id-1";
    }
}
