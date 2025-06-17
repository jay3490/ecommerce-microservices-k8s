using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BuildingBlocks.Observability;

/// <summary>
/// The activity source and meter shared by the building blocks. Services register both
/// names with OpenTelemetry through <see cref="ServiceDefaultsExtensions"/>.
/// </summary>
public static class Telemetry
{
    public const string Name = "Ecommerce";

    public static readonly ActivitySource ActivitySource = new(Name);
    public static readonly Meter Meter = new(Name);
}
