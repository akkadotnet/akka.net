//-----------------------------------------------------------------------
// <copyright file="SemanticLoggingBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Benchmarks.Configurations;
using Akka.Event;
using BenchmarkDotNet.Attributes;
using static Akka.Benchmarks.Configurations.BenchmarkCategories;

namespace Akka.Benchmarks.Logging
{
    /// <summary>
    /// Benchmarks for semantic logging implementation in Akka.NET.
    /// Tests template parsing, property extraction, and message formatting performance.
    ///
    /// Performance Targets:
    /// - Template cache hit: &lt;100ns
    /// - Template parse (uncached): &lt;5μs
    /// - Full format operation: &lt;2μs
    /// - Property extraction: &lt;1μs (with caching)
    /// - GC pressure: &lt;200 bytes per log call
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    [MemoryDiagnoser]
    public class SemanticLoggingBenchmarks
    {
        // ============================================================================
        // CATEGORY 1: Template Parsing - Cache Performance
        // ============================================================================

        private const string SimpleTemplate = "User {UserId} logged in";
        private const string ComplexTemplate = "Request {RequestId} from {IpAddress} at {Timestamp:yyyy-MM-dd} returned {StatusCode} in {Duration:N2}ms";
        private const string PositionalTemplate = "Value {0} and {1} and {2}";

        private string[] _varyingTemplates;
        private const int TemplateVariations = 100;

        [GlobalSetup]
        public void Setup()
        {
            // Pre-generate varying templates to test cache effectiveness
            _varyingTemplates = new string[TemplateVariations];
            for (int i = 0; i < TemplateVariations; i++)
            {
                _varyingTemplates[i] = $"User {{UserId}} performed action {{Action{i}}}";
            }

            // Warm up the cache with first template
            MessageTemplateParser.GetPropertyNames(SimpleTemplate);
        }

        [Benchmark(Description = "Template parse - COLD (first time)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> TemplateParse_Cold()
        {
            // This simulates a cold cache by using a unique template each time
            // Note: In reality this will pollute the cache, but shows worst-case
            var template = $"Unique template {{Prop{Guid.NewGuid()}}}";
            return MessageTemplateParser.GetPropertyNames(template);
        }

        [Benchmark(Description = "Template parse - WARM (cached)", Baseline = true)]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> TemplateParse_Warm()
        {
            // Should hit ThreadStatic cache - target <100ns
            return MessageTemplateParser.GetPropertyNames(SimpleTemplate);
        }

        [Benchmark(Description = "Template parse - Complex template (cached)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> TemplateParse_ComplexCached()
        {
            return MessageTemplateParser.GetPropertyNames(ComplexTemplate);
        }

        [Benchmark(Description = "Template parse - Positional {0} (cached)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> TemplateParse_PositionalCached()
        {
            return MessageTemplateParser.GetPropertyNames(PositionalTemplate);
        }

        [Benchmark(Description = "Template parse - Cache thrashing (100 templates)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> TemplateParse_CacheThrashing()
        {
            // Tests LRU eviction by cycling through many templates
            var result = default(IReadOnlyList<string>);
            for (int i = 0; i < TemplateVariations; i++)
            {
                result = MessageTemplateParser.GetPropertyNames(_varyingTemplates[i]);
            }
            return result;
        }

        // ============================================================================
        // CATEGORY 2: Property Extraction - LogMessage Performance
        // ============================================================================

        private LogMessage _simpleLogMessage1Param;
        private LogMessage _simpleLogMessage3Params;
        private LogMessage _complexLogMessage5Params;
        private LogMessage _positionalLogMessage;

        [GlobalSetup(Target = nameof(PropertyExtraction_1Param) + "," +
                            nameof(PropertyExtraction_3Params) + "," +
                            nameof(PropertyExtraction_5Params) + "," +
                            nameof(PropertyExtraction_Positional) + "," +
                            nameof(PropertyExtraction_Cached) + "," +
                            nameof(GetProperties_1Param) + "," +
                            nameof(GetProperties_3Params) + "," +
                            nameof(GetProperties_5Params) + "," +
                            nameof(GetProperties_Cached))]
        public void SetupPropertyExtraction()
        {
            _simpleLogMessage1Param = new LogMessage<LogValues<int>>(
                DefaultLogMessageFormatter.Instance,
                "User {UserId} logged in",
                new LogValues<int>(12345)
            );

            _simpleLogMessage3Params = new LogMessage<LogValues<int, string, DateTime>>(
                DefaultLogMessageFormatter.Instance,
                "User {UserId} from {IpAddress} at {Timestamp}",
                new LogValues<int, string, DateTime>(12345, "192.168.1.1", DateTime.UtcNow)
            );

            _complexLogMessage5Params = new LogMessage<LogValues<Guid, string, DateTime, int, double>>(
                DefaultLogMessageFormatter.Instance,
                ComplexTemplate,
                new LogValues<Guid, string, DateTime, int, double>(
                    Guid.NewGuid(), "192.168.1.1", DateTime.UtcNow, 200, 123.45
                )
            );

            _positionalLogMessage = new LogMessage<LogValues<int, string, double>>(
                DefaultLogMessageFormatter.Instance,
                "Value {0} and {1} and {2}",
                new LogValues<int, string, double>(42, "test", 3.14)
            );
        }

        [Benchmark(Description = "PropertyNames - 1 param (lazy init)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> PropertyExtraction_1Param()
        {
            // Tests lazy initialization cost
            var msg = new LogMessage<LogValues<int>>(
                DefaultLogMessageFormatter.Instance,
                SimpleTemplate,
                new LogValues<int>(12345)
            );
            return msg.PropertyNames;
        }

        [Benchmark(Description = "PropertyNames - 3 params (lazy init)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> PropertyExtraction_3Params()
        {
            var msg = new LogMessage<LogValues<int, string, DateTime>>(
                DefaultLogMessageFormatter.Instance,
                "User {UserId} from {IpAddress} at {Timestamp}",
                new LogValues<int, string, DateTime>(12345, "192.168.1.1", DateTime.UtcNow)
            );
            return msg.PropertyNames;
        }

        [Benchmark(Description = "PropertyNames - 5 params (lazy init)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> PropertyExtraction_5Params()
        {
            var msg = new LogMessage<LogValues<Guid, string, DateTime, int, double>>(
                DefaultLogMessageFormatter.Instance,
                ComplexTemplate,
                new LogValues<Guid, string, DateTime, int, double>(
                    Guid.NewGuid(), "192.168.1.1", DateTime.UtcNow, 200, 123.45
                )
            );
            return msg.PropertyNames;
        }

        [Benchmark(Description = "PropertyNames - Positional")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> PropertyExtraction_Positional()
        {
            var msg = new LogMessage<LogValues<int, string, double>>(
                DefaultLogMessageFormatter.Instance,
                PositionalTemplate,
                new LogValues<int, string, double>(42, "test", 3.14)
            );
            return msg.PropertyNames;
        }

        [Benchmark(Description = "PropertyNames - Cached (2nd access)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> PropertyExtraction_Cached()
        {
            // Should be cached after first access - target ~10ns
            return _simpleLogMessage1Param.PropertyNames;
        }

        // ============================================================================
        // CATEGORY 3: GetProperties() - Dictionary Construction
        // ============================================================================

        [Benchmark(Description = "GetProperties - 1 param")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyDictionary<string, object> GetProperties_1Param()
        {
            return _simpleLogMessage1Param.GetProperties();
        }

        [Benchmark(Description = "GetProperties - 3 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyDictionary<string, object> GetProperties_3Params()
        {
            return _simpleLogMessage3Params.GetProperties();
        }

        [Benchmark(Description = "GetProperties - 5 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyDictionary<string, object> GetProperties_5Params()
        {
            return _complexLogMessage5Params.GetProperties();
        }

        [Benchmark(Description = "GetProperties - Cached (2nd access)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyDictionary<string, object> GetProperties_Cached()
        {
            // Should be cached - target ~5ns
            return _simpleLogMessage1Param.GetProperties();
        }

        // ============================================================================
        // CATEGORY 4: Message Formatting - SemanticLogMessageFormatter vs Default
        // ============================================================================

        private object[] _args1 = new object[] { 12345 };
        private object[] _args3 = new object[] { 12345, "192.168.1.1", DateTime.UtcNow };
        private object[] _args5 = new object[] { Guid.NewGuid(), "192.168.1.1", DateTime.UtcNow, 200, 123.45 };

        [Benchmark(Description = "Format - Semantic 1 param")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_Semantic_1Param()
        {
            return SemanticLogMessageFormatter.Instance.Format(SimpleTemplate, _args1);
        }

        [Benchmark(Description = "Format - Semantic 3 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_Semantic_3Params()
        {
            return SemanticLogMessageFormatter.Instance.Format(
                "User {UserId} from {IpAddress} at {Timestamp}",
                _args3
            );
        }

        [Benchmark(Description = "Format - Semantic 5 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_Semantic_5Params()
        {
            return SemanticLogMessageFormatter.Instance.Format(ComplexTemplate, _args5);
        }

        [Benchmark(Description = "Format - Semantic with format spec {Value:N2}")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_Semantic_WithFormatSpec()
        {
            return SemanticLogMessageFormatter.Instance.Format(
                "Duration was {Duration:N2}ms",
                new object[] { 123.456789 }
            );
        }

        [Benchmark(Description = "Format - Default (positional) 3 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_Default_3Params()
        {
            return DefaultLogMessageFormatter.Instance.Format(
                "Value {0} and {1} and {2}",
                _args3
            );
        }

        [Benchmark(Description = "Format - Semantic Positional 3 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_Semantic_Positional_3Params()
        {
            return SemanticLogMessageFormatter.Instance.Format(PositionalTemplate, _args3);
        }

        // ============================================================================
        // CATEGORY 5: End-to-End Logging Pipeline
        // ============================================================================

        private sealed class BenchmarkLogAdapter : LoggingAdapterBase
        {
            public LogEvent LastLog { get; private set; }
            private readonly string _logSource;
            private readonly Type _logClass;

            public BenchmarkLogAdapter(ILogMessageFormatter formatter) : base(formatter)
            {
                _logSource = LogSource.Create(this).Source;
                _logClass = typeof(BenchmarkLogAdapter);
            }

            public override bool IsDebugEnabled => true;
            public override bool IsInfoEnabled => true;
            public override bool IsWarningEnabled => true;
            public override bool IsErrorEnabled => true;

            protected override void NotifyLog(LogLevel logLevel, object message, Exception cause = null)
            {
                LastLog = new Info(cause, _logSource, _logClass, message);
            }
        }

        private BenchmarkLogAdapter _defaultLogger;
        private BenchmarkLogAdapter _semanticLogger;

        [GlobalSetup(Target = nameof(EndToEnd_Default_NoParams) + "," +
                            nameof(EndToEnd_Default_1Param) + "," +
                            nameof(EndToEnd_Default_3Params) + "," +
                            nameof(EndToEnd_Semantic_NoParams) + "," +
                            nameof(EndToEnd_Semantic_1Param) + "," +
                            nameof(EndToEnd_Semantic_3Params) + "," +
                            nameof(EndToEnd_Semantic_WithProperties))]
        public void SetupEndToEnd()
        {
            _defaultLogger = new BenchmarkLogAdapter(DefaultLogMessageFormatter.Instance);
            _semanticLogger = new BenchmarkLogAdapter(SemanticLogMessageFormatter.Instance);
        }

        [Benchmark(Description = "E2E - Default formatter, no params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public LogEvent EndToEnd_Default_NoParams()
        {
            _defaultLogger.Info("User logged in");
            return _defaultLogger.LastLog;
        }

        [Benchmark(Description = "E2E - Default formatter, 1 param")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public LogEvent EndToEnd_Default_1Param()
        {
            _defaultLogger.Info("User {0} logged in", 12345);
            return _defaultLogger.LastLog;
        }

        [Benchmark(Description = "E2E - Default formatter, 3 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public LogEvent EndToEnd_Default_3Params()
        {
            _defaultLogger.Info("User {0} from {1} at {2}", 12345, "192.168.1.1", DateTime.UtcNow);
            return _defaultLogger.LastLog;
        }

        [Benchmark(Description = "E2E - Semantic formatter, no params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public LogEvent EndToEnd_Semantic_NoParams()
        {
            _semanticLogger.Info("User logged in");
            return _semanticLogger.LastLog;
        }

        [Benchmark(Description = "E2E - Semantic formatter, 1 param")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public LogEvent EndToEnd_Semantic_1Param()
        {
            _semanticLogger.Info("User {UserId} logged in", 12345);
            return _semanticLogger.LastLog;
        }

        [Benchmark(Description = "E2E - Semantic formatter, 3 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public LogEvent EndToEnd_Semantic_3Params()
        {
            _semanticLogger.Info("User {UserId} from {IpAddress} at {Timestamp}",
                12345, "192.168.1.1", DateTime.UtcNow);
            return _semanticLogger.LastLog;
        }

        [Benchmark(Description = "E2E - Semantic with GetProperties()")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyDictionary<string, object> EndToEnd_Semantic_WithProperties()
        {
            _semanticLogger.Info("User {UserId} from {IpAddress}", 12345, "192.168.1.1");
            var logEvent = _semanticLogger.LastLog;
            if (logEvent.TryGetProperties(out var props))
                return props;
            return null;
        }

        // ============================================================================
        // CATEGORY 6: Allocation Benchmarks - Memory Pressure Analysis
        // ============================================================================

        [Benchmark(Description = "Allocations - Parse template (cold)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyList<string> Allocations_ParseCold()
        {
            // Unique template to avoid cache
            var template = $"Event {{Id}} at {{Time}} with {{Data}}";
            return MessageTemplateParser.GetPropertyNames(template);
        }

        [Benchmark(Description = "Allocations - Format semantic 3 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Allocations_FormatSemantic()
        {
            return SemanticLogMessageFormatter.Instance.Format(
                "User {UserId} from {IpAddress} at {Timestamp}",
                new object[] { 12345, "192.168.1.1", DateTime.UtcNow }
            );
        }

        [Benchmark(Description = "Allocations - GetProperties 3 params")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyDictionary<string, object> Allocations_GetProperties()
        {
            var msg = new LogMessage<LogValues<int, string, DateTime>>(
                DefaultLogMessageFormatter.Instance,
                "User {UserId} from {IpAddress} at {Timestamp}",
                new LogValues<int, string, DateTime>(12345, "192.168.1.1", DateTime.UtcNow)
            );
            return msg.GetProperties();
        }

        [Benchmark(Description = "Allocations - Full log + properties")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public IReadOnlyDictionary<string, object> Allocations_FullPipeline()
        {
            _semanticLogger.Info("User {UserId} from {IpAddress} performed {Action}",
                12345, "192.168.1.1", "login");
            if (_semanticLogger.LastLog.TryGetProperties(out var props))
                return props;
            return null;
        }

        // ============================================================================
        // CATEGORY 7: Escaped Brace Handling
        // ============================================================================

        private const string EscapedBracesOnly = "Use {{ and }} for literals";
        private const string EscapedBracesWithPlaceholder = "{First}}} text {{more {Second}";
        private const string NestedEscapedBraces = "{{{UserId}}}";
        private const string TrailingEscapedBrace = "{UserId}}";

        [Benchmark(Description = "Format - Escaped braces only (no placeholders)")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_EscapedBracesOnly()
        {
            // Tests UnescapeBraces path - no placeholders, just {{ and }}
            return SemanticLogMessageFormatter.Instance.Format(EscapedBracesOnly, Array.Empty<object>());
        }

        [Benchmark(Description = "Format - Escaped braces with placeholders")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_EscapedBracesWithPlaceholders()
        {
            // Tests FormatNamedTemplate with mixed escaped braces and placeholders
            return SemanticLogMessageFormatter.Instance.Format(
                EscapedBracesWithPlaceholder,
                new object[] { 1, 2 }
            );
        }

        [Benchmark(Description = "Format - Nested escaped braces {{{Value}}}")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_NestedEscapedBraces()
        {
            // Tests {{{ }}} pattern: escaped brace + placeholder + escaped brace
            return SemanticLogMessageFormatter.Instance.Format(NestedEscapedBraces, new object[] { 123 });
        }

        [Benchmark(Description = "Format - Trailing escaped brace {Value}}")]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public string Format_TrailingEscapedBrace()
        {
            // Tests placeholder followed by literal }
            return SemanticLogMessageFormatter.Instance.Format(TrailingEscapedBrace, new object[] { 123 });
        }

        // ============================================================================
        // CATEGORY 8: Stress Tests - Real-world Patterns
        // ============================================================================

        private const int BatchSize = 1000;

        [Benchmark(Description = "Stress - 1K logs (same template)", OperationsPerInvoke = BatchSize)]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public void Stress_1K_SameTemplate()
        {
            for (int i = 0; i < BatchSize; i++)
            {
                _semanticLogger.Info("User {UserId} logged in", i);
            }
        }

        [Benchmark(Description = "Stress - 1K logs (varying templates)", OperationsPerInvoke = BatchSize)]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public void Stress_1K_VaryingTemplates()
        {
            for (int i = 0; i < BatchSize; i++)
            {
                _semanticLogger.Info(_varyingTemplates[i % TemplateVariations], i);
            }
        }

        [Benchmark(Description = "Stress - 1K logs with property extraction", OperationsPerInvoke = BatchSize)]
        [BenchmarkCategory(MicroBenchmark, AkkaEventBenchmark)]
        public int Stress_1K_WithPropertyExtraction()
        {
            int propCount = 0;
            for (int i = 0; i < BatchSize; i++)
            {
                _semanticLogger.Info("User {UserId} performed {Action}", i, $"Action{i}");
                if (_semanticLogger.LastLog.TryGetProperties(out var props))
                    propCount += props.Count;
            }
            return propCount;
        }
    }
}
