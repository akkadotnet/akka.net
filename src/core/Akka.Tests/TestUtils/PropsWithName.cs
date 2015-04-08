using Akka.Actor;

namespace Akka.Tests.TestUtils
{
    public class PropsWithName
    {
        private readonly Props _props;
        private readonly string _name;

        public PropsWithName(Props props, string name)
        {
            _props = props;
            _name = name;
        }

        public Props Props { get { return _props; } }

        public string Name { get { return _name; } }
    }
}