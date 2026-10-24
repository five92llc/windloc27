using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WinLoc27;

/// <summary>
/// Watches one paired Bluetooth device. Raises <see cref="DeviceConnected"/> when the device goes
/// from disconnected to connected after <see cref="Arm"/> was called, so a phone that is already
/// connected when the lock engages does not unlock it instantly; it has to reconnect.
/// </summary>
sealed class BluetoothWatcher : IDisposable
{
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 1500 };
    BluetoothDevice? _device;
    bool _wasConnected;

    public event Action? DeviceConnected;

    public BluetoothWatcher()
    {
        _poll.Tick += (_, _) => Check();
    }

    public static async Task<List<(string Id, string Name)>> ListPairedAsync()
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var devices = await DeviceInformation.FindAllAsync(selector);
        return devices.Select(d => (d.Id, d.Name)).OrderBy(d => d.Name).ToList();
    }

    public async Task<bool> StartAsync(string deviceId)
    {
        Stop();
        try
        {
            _device = await BluetoothDevice.FromIdAsync(deviceId);
        }
        catch
        {
            _device = null;
        }
        return _device != null;
    }

    /// <summary>Begin watching for a fresh connection.</summary>
    public void Arm()
    {
        if (_device == null) return;
        _wasConnected = IsConnected();
        _poll.Start();
    }

    public void Disarm() => _poll.Stop();

    bool IsConnected() => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;

    void Check()
    {
        var now = IsConnected();
        var rose = now && !_wasConnected;
        _wasConnected = now;
        if (rose) DeviceConnected?.Invoke();
    }

    public void Stop()
    {
        _poll.Stop();
        _device?.Dispose();
        _device = null;
    }

    public void Dispose()
    {
        Stop();
        _poll.Dispose();
    }
}
