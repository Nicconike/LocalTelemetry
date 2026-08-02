namespace LocalTelemetry.Core.Models;

/// <summary>Describes a single telemetry metric: its identifier, labels, unit and group.</summary>
public sealed record MetricDescriptor(
    string Id,
    string ShortLabel,
    string FullLabel,
    string Unit,
    string Group
);
