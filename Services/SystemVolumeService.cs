using System.Runtime.InteropServices;

namespace FControl.Services;

public sealed class SystemVolumeService
{
    private const uint ClsctxInprocServer = 0x1;

    public VolumeControlResult ChangeVolumeByPercent(int deltaPercent)
    {
        try
        {
            using var endpoint = AudioEndpointVolumeHandle.CreateDefaultRenderEndpoint();
            endpoint.Value.GetMasterVolumeLevelScalar(out var currentVolume);

            var nextVolume = Math.Clamp(currentVolume + deltaPercent / 100f, 0f, 1f);
            endpoint.Value.SetMasterVolumeLevelScalar(nextVolume, Guid.Empty);
            endpoint.Value.GetMasterVolumeLevelScalar(out var actualVolume);
            endpoint.Value.GetMute(out var muted);

            return VolumeControlResult.Success(actualVolume, muted);
        }
        catch (Exception ex)
        {
            return VolumeControlResult.Failure(ex.Message);
        }
    }

    public VolumeControlResult ToggleMute()
    {
        try
        {
            using var endpoint = AudioEndpointVolumeHandle.CreateDefaultRenderEndpoint();
            endpoint.Value.GetMute(out var muted);
            endpoint.Value.SetMute(!muted, Guid.Empty);
            endpoint.Value.GetMasterVolumeLevelScalar(out var volume);
            endpoint.Value.GetMute(out var actualMuted);

            return VolumeControlResult.Success(volume, actualMuted);
        }
        catch (Exception ex)
        {
            return VolumeControlResult.Failure(ex.Message);
        }
    }

    private sealed class AudioEndpointVolumeHandle : IDisposable
    {
        private readonly IMMDeviceEnumerator _enumerator;
        private readonly IMMDevice _device;

        private AudioEndpointVolumeHandle(IMMDeviceEnumerator enumerator, IMMDevice device, IAudioEndpointVolume value)
        {
            _enumerator = enumerator;
            _device = device;
            Value = value;
        }

        public IAudioEndpointVolume Value { get; }

        public static AudioEndpointVolumeHandle CreateDefaultRenderEndpoint()
        {
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            try
            {
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
                try
                {
                    var endpointVolumeId = typeof(IAudioEndpointVolume).GUID;
                    device.Activate(ref endpointVolumeId, ClsctxInprocServer, 0, out var endpointVolumeObject);
                    return new AudioEndpointVolumeHandle(enumerator, device, (IAudioEndpointVolume)endpointVolumeObject);
                }
                catch
                {
                    Marshal.ReleaseComObject(device);
                    throw;
                }
            }
            catch
            {
                Marshal.ReleaseComObject(enumerator);
                throw;
            }
        }

        public void Dispose()
        {
            Marshal.ReleaseComObject(Value);
            Marshal.ReleaseComObject(_device);
            Marshal.ReleaseComObject(_enumerator);
        }
    }

    private enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out nint ppDevices);
        void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(nint pClient);
        int UnregisterEndpointNotificationCallback(nint pClient);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, uint dwClsCtx, nint pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(uint stgmAccess, out nint ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out uint pdwState);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(nint pNotify);
        int UnregisterControlChangeNotify(nint pNotify);
        void GetChannelCount(out uint pnChannelCount);
        void SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
        void SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
        void GetMasterVolumeLevel(out float pfLevelDB);
        void GetMasterVolumeLevelScalar(out float pfLevel);
        void SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
        void SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);
        void GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        void GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        void GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        void VolumeStepUp(Guid pguidEventContext);
        void VolumeStepDown(Guid pguidEventContext);
        void QueryHardwareSupport(out uint pdwHardwareSupportMask);
        void GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }
}

public sealed record VolumeControlResult(bool Succeeded, int VolumePercent, bool IsMuted, string? Message)
{
    public static VolumeControlResult Success(float volume, bool isMuted)
    {
        return new VolumeControlResult(true, (int)Math.Round(Math.Clamp(volume, 0f, 1f) * 100), isMuted, null);
    }

    public static VolumeControlResult Failure(string message)
    {
        return new VolumeControlResult(false, 0, false, message);
    }
}
