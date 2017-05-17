using Akka.Actor;
using Akka.Persistence;

namespace DocsExamples.Persistence.PersistentActor
{
    public static class Defer
    {
        public class MyPersistentActor : UntypedPersistentActor
        {
            public override string PersistenceId => "my-stable-persistence-id";

            protected override void OnRecover(object message)
            {
                // handle recovery here
            }

            protected override void OnCommand(object message)
            {
                if (message is string c)
                {
                    Sender.Tell(c);
                    PersistAsync($"evt-{c}-1", e => Sender.Tell(e));
                    PersistAsync($"evt-{c}-2", e => Sender.Tell(e));
                    DeferAsync($"evt-{c}-3", e => Sender.Tell(e));
                }
            }
        }
    }
}
