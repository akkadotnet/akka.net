// //-----------------------------------------------------------------------
// // <copyright file="TimelineLogCollectorActor.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Event;

namespace Akka.MultiNode.TestAdapter.Internal.Sinks
{
    internal class TimelineLogCollectorActor : ReceiveActor
    {
        private readonly bool _appendLogOutput;
        private readonly SortedList<DateTime, HashSet<LogMessageInfo>> _timeline = new SortedList<DateTime, HashSet<LogMessageInfo>>();
        
        public TimelineLogCollectorActor(bool appendLogOutput)
        {
            _appendLogOutput = appendLogOutput;
            
            Receive<LogMessage>(msg =>
            {
                var parsedInfo = new LogMessageInfo(msg);
                if (_timeline.ContainsKey(parsedInfo.When))
                    _timeline[parsedInfo.When].Add(parsedInfo);
                else
                    _timeline.Add(parsedInfo.When, new HashSet<LogMessageInfo>() { parsedInfo });
            });
            
            Receive<SendMeAll>(_ => Sender.Tell(_timeline.Values.ToList()));

            Receive<GetSpecLog>(_ =>
            {
                var log = new SpecLog()
                {
                    AggregatedTimelineLog = _timeline.Select(pairs => pairs.Value).SelectMany(msg => msg).Select(m => m.ToString()).ToList(),
                    NodeLogs = _timeline.Select(pairs => pairs.Value).SelectMany(msg => msg).GroupBy(msg => msg.Node).Select(nodeMessages =>
                    {
                        var node = nodeMessages.Key;
                        return (NodeIndex: node.Index, NodeRole: node.Role, Logs: nodeMessages.Select(m => m.ToString()).ToList());
                    }).ToList()
                };
                
                Sender.Tell(log);
            });

            Receive<GetLog>(_ =>
            {
                Sender.Tell(_timeline.Select(pairs => pairs.Value).SelectMany(msg => msg).Select(m => m.ToString()).ToArray());
            });
            
            Receive<DumpToFile>(dump =>
            {
                // Verify that directory exists
                var dir = new DirectoryInfo(Path.GetDirectoryName(dump.FilePath));
                if (!dir.Exists)
                    dir.Create();

                var lines = 
                    _timeline.Select(pairs => pairs.Value).SelectMany(msg => msg).Select(m => m.ToString()).ToArray();
                bool dumpSuccess;
                do
                {
                    try
                    {
                        if(!_appendLogOutput && File.Exists(dump.FilePath))
                            File.Delete(dump.FilePath);
                        
                        File.AppendAllLines(dump.FilePath, lines);
                        dumpSuccess = true;
                    }
                    catch
                    {
                        dumpSuccess = false;
                    }
                } while (!dumpSuccess);
                
                Sender.Tell(Done.Instance);
            });
            
            Receive<PrintToConsole>(_ =>
            {
                var logsPerTest = _timeline
                    .Select(pairs => pairs.Value)
                    .SelectMany(msg => msg)
                    .GroupBy(m => m.Node.TestName);

                var sb = new StringBuilder();
                foreach (var testLogs in logsPerTest)
                {
                    Console.WriteLine($"Detailed logs for {testLogs.Key}\n");
                    foreach (var log in testLogs)
                    {
                        var logLine = log.ToString();
                        Console.WriteLine(logLine);
                        sb.AppendLine(logLine);
                    }
                    Console.WriteLine($"\nEnd logs for {testLogs.Key}\n");
                }
                
                Sender.Tell(sb.ToString());
            });
        }

        public class LogMessageInfo
        {
            public NodeInfo Node { get; }
            public string OriginalMessage { get; }
            public DateTime When { get; }
            public LogLevel LogLevel { get; }
            public string Message { get; }

            public LogMessageInfo(LogMessage msg)
            {
                OriginalMessage = msg.Message;
                Node = msg.Node;
                When = DateTime.UtcNow;
                LogLevel = LogLevel.InfoLevel; // In case if we could not find log level, assume that it is Info
                Message = OriginalMessage;
                
                var pieces = Regex.Matches(msg.Message, @"\[([^\]]+)\]");
                foreach (Match piece in pieces)
                {
                    Message = Message.Replace(piece.Value, "");
                    
                    if (DateTime.TryParse(piece.Value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var when))
                        When = when;

                    if (TryParseLogLevel(piece.Value, out var logLevel))
                        LogLevel = logLevel;
                }
            }

            public override string ToString()
            {
                return $"[Node #{Node.Index}({Node.Role})]{OriginalMessage}";
            }

            private bool TryParseLogLevel(string str, out LogLevel logLevel)
            {
                var enumValues = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>().ToList();
                foreach (var logLevelInfo in Enum.GetNames(typeof(LogLevel)).Select((name, i) => (Name: name, Index: i)))
                {
                    if (string.Equals(str + "Level", logLevelInfo.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        logLevel = enumValues[logLevelInfo.Index];
                        return true;
                    }
                }

                logLevel = default(LogLevel);
                return false;
            }
        }
        
        public class NodeInfo : IEquatable<NodeInfo>
        {
            public NodeInfo(int index, string role, string platform, string testName)
            {
                Index = index;
                Role = role;
                Platform = platform;
                TestName = testName;
            }

            public int Index { get; }
            public string Role { get; }
            public string Platform { get; }
            public string TestName { get; set; }

            /// <inheritdoc />
            public bool Equals(NodeInfo other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Index == other.Index;
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != GetType()) return false;
                return Equals((NodeInfo)obj);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return Index;
            }
        }
        
        public class LogMessage
        {
            public LogMessage(NodeInfo node, string message)
            {
                Node = node;
                Message = message;
            }

            public NodeInfo Node { get; }
            public string Message { get; }
        }

        public class SendMeAll { }
        
        public class PrintToConsole { }

        public class GetSpecLog { }

        public class GetLog { }
        
        public class DumpToFile
        {
            public DumpToFile(string filePath)
            {
                FilePath = filePath;
            }

            public string FilePath { get; }
        }
    }
}