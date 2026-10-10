using MassTransit;

namespace OrderFlow.ServiceDefaults;

public static class FaultExtensions
{
    /// <summary>
    /// Name of the service whose consumer faulted ("payment", "notification", …), derived from the
    /// faulting host's entry assembly (e.g. <c>OrderFlow.Payment.Worker</c>). Every subscriber of a message
    /// publishes its own <see cref="Fault{T}"/>, so handlers must check where a fault came from.
    /// </summary>
    public static string FaultingService(this Fault fault)
    {
        var assembly = fault.Host?.Assembly ?? string.Empty;
        var parts = assembly.Split('.');
        return (parts.Length >= 2 ? parts[1] : assembly).ToLowerInvariant();
    }
}
