using Akka.Actor;

namespace Akka.TestKit
{
    public abstract class TestKitBase
    {
        private ActorSystem _system;
        private TestKitSettings _testKitSettings;

        protected ActorSystem System
        {
            get { return _system; }
            set
            {
                if(_system!=value)
                {
                    _system = value;
                    _testKitSettings = TestKitExtension.For(_system);
                }
            }
        }

        public TestKitSettings TestKitSettings { get { return _testKitSettings; } }
    }
}