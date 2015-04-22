namespace Akka.NodeTestRunner
{
    public class NodeRunnerSettings 
    {
        public string AssemblyName { get; set; }
        public string TestClass { get; set; }
        public string TestMethod { get; set; }
        public int MaxNodes { get; set; }
        public string ServerHost { get; set; }
        public string Host { get; set; }
        public int NodeIndex { get; set; }
    }
}