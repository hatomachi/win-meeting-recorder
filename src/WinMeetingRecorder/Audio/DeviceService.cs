using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WinMeetingRecorder.Audio;

public class AudioDeviceInfo
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public bool IsDefault { get; set; }
    public string Type { get; set; } = "Unknown"; // "Playback" or "Capture"
}

public class DeviceService
{
    public static List<AudioDeviceInfo> GetPlaybackDevices()
    {
        var list = new List<AudioDeviceInfo>();
        using var enumerator = new MMDeviceEnumerator();
        
        MMDevice? defaultDevice = null;
        try
        {
            defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
            // デフォルト取得不可時は無視
        }

        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in devices)
        {
            list.Add(new AudioDeviceInfo
            {
                Id = device.ID,
                Name = device.FriendlyName,
                IsDefault = defaultDevice != null && device.ID == defaultDevice.ID,
                Type = "Playback"
            });
        }

        return list;
    }

    public static List<AudioDeviceInfo> GetCaptureDevices()
    {
        var list = new List<AudioDeviceInfo>();
        using var enumerator = new MMDeviceEnumerator();
        
        MMDevice? defaultDevice = null;
        try
        {
            defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            try
            {
                defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            }
            catch
            {
                // デフォルト取得不可時は無視
            }
        }

        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        foreach (var device in devices)
        {
            list.Add(new AudioDeviceInfo
            {
                Id = device.ID,
                Name = device.FriendlyName,
                IsDefault = defaultDevice != null && device.ID == defaultDevice.ID,
                Type = "Capture"
            });
        }

        return list;
    }
}
