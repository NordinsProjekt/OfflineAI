namespace AgentKit.Api.Models;

/// <summary>
/// This node's current capacity, as reported to a peer deciding whether to forward a job here.
/// </summary>
public sealed record ClusterStatus(int AvailableCapacity, int MaxCapacity);
