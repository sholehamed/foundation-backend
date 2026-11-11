using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Modules.System.Observability;

/// <summary>
/// Host-neutral names for explicitly instrumented Foundation operations.
/// These are not tied to Identity, SQL Server, or a telemetry vendor.
/// </summary>
public static class FoundationTelemetry
{
    public const string ActivitySourceName = "Foundation.Observability";
    public const string MeterName = "Foundation.Observability";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    // These metrics carry only low-cardinality, code-owned/normalized values.
    public static readonly Counter<long> CompletedRequests =
        Meter.CreateCounter<long>("foundation.http.requests.completed");

    public static readonly Histogram<double> PipelineDuration =
        Meter.CreateHistogram<double>("foundation.http.pipeline.duration", unit: "ms");
}
