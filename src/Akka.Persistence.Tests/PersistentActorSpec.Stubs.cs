using System.Collections.Generic;

namespace Akka.Persistence.Tests
{
    public partial class PersistentActorSpec
    {
        struct Cmd
        {
            public Cmd(object data)
                : this()
            {
                Data = data;
            }

            public object Data { get; private set; }
        }
        struct Evt
        {
            public Evt(object data)
                : this()
            {
                Data = data;
            }

            public object Data { get; private set; }
        }

        abstract class TestPersistentActor : PersistentActorBase
        {
            private readonly List<object> _events = new List<object>();

        }
    }
}