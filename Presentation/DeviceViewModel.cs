using IslandDesk.Infrastructure;
using IslandDesk.Monitoring;
namespace IslandDesk.Presentation;

internal sealed class DeviceViewModel : ObservableObject
{
    private Task? _load;
    public string ComputerName { get; private set; } = "This PC";
    public string Motherboard { get; private set; } = "Reading hardware…";
    public Task EnsureLoadedAsync() => _load ??= LoadAsync();
    private async Task LoadAsync()
    {
        try
        {
            var information = await DeviceInformationReader.GetAsync();
            ComputerName = information.ComputerName;
            Motherboard = information.Motherboard;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Device information unavailable: {0}", error.Message);
            Motherboard = "Motherboard unavailable";
        }
        Changed(null);
    }
}
