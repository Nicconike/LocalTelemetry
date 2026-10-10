// <copyright file="OverlayDeploymentState.cs" company="LocalTelemetry">
// Copyright (c) LocalTelemetry. All rights reserved.
// </copyright>

namespace LocalTelemetry.Core.Models;

/// <summary>
/// Represents the deployment lifecycle state of the taskbar telemetry overlay window.
/// </summary>
public enum OverlayDeploymentState
{
    /// <summary>Overlay has not yet attempted to deploy or has been detached/hidden.</summary>
    Uninitialized,

    /// <summary>Overlay is successfully deployed, positioned, and active.</summary>
    Active,

    /// <summary>Overlay failed to deploy, position, or maintain its taskbar connection.</summary>
    Failed,

    /// <summary>Overlay is actively attempting recovery after a failure or Explorer restart.</summary>
    Recovering,
}
