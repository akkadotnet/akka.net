using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Event
{
    public class LoggingSpec : AkkaSpec
    {
        public LoggingSpec() : base(GetConfig())
        {
            
        }

        private static string GetConfig()
        {
            return @"
akka.stdout-logtemplate = ""TestConfig [{LogLevel}][{Timestamp}][Thread {ThreadId}][{LogSource}] {Message}""
";
        }        

        [Fact]
        public void Default_stdout_logging_template_is_read_from_config()
        {
            Sys.Settings.StdoutLogTemplate.ShouldBe("TestConfig [{LogLevel}][{Timestamp}][Thread {ThreadId}][{LogSource}] {Message}");
        }

        [Fact]
        public void LogEvent_can_be_rendered_with_custom_templates()
        {
            var debug = new Debug("abc",typeof(string),123);
            var res = debug.ToString("{LogSource}{Message}");
            res.ShouldBe("abc123");
        }

        [Fact]
        public void LogEvent_can_be_rendered_with_formatters()
        {
            var debug = new Debug("abc", typeof(string), 123);
            var res = debug.ToString("{Timestamp:yyyy}");
            res.Length.ShouldBe(4);
        }
    }
}
