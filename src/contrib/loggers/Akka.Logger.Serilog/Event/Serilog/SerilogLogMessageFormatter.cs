using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace Akka.Serilog.Event.Serilog
{    
	[Obsolete("Use Akka.Logger.Serilog.SerilogLogMessageFormatter instead. This class will be removed in a future version.")]
	public class SerilogLogMessageFormatter : global::Akka.Logger.Serilog.SerilogLogMessageFormatter
	{
	}
}
