using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MicNeverDown
{
    public sealed class AudioDeviceInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public float Level { get; set; }
        public bool Muted { get; set; }
        public bool IsDefault { get; set; }
        public bool IsCommunications { get; set; }
    }

    public static class AudioDevices
    {
        private const uint DeviceStateActive = 0x00000001;

        // Lists active capture endpoints only. No audio stream is opened.
        public static List<AudioDeviceInfo> ListCapture()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                string defaultId = ReadDefaultId(enumerator, ERole.Console);
                string communicationsId = ReadDefaultId(enumerator, ERole.Communications);
                HResult.Check(enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceStateActive, out collection));
                uint count;
                HResult.Check(collection.GetCount(out count));
                List<AudioDeviceInfo> result = new List<AudioDeviceInfo>();
                for (uint index = 0; index < count; index++)
                {
                    IMMDevice device = null;
                    AudioEndpoint endpoint = null;
                    try
                    {
                        HResult.Check(collection.Item(index, out device));
                        string id = ReadId(device);
                        string name = ReadName(device);
                        endpoint = new AudioEndpoint(device);
                        device = null; // AudioEndpoint now owns this acquired COM reference.
                        result.Add(new AudioDeviceInfo
                        {
                            Id = id,
                            Name = String.IsNullOrEmpty(name) ? id : name,
                            Level = endpoint.Level,
                            Muted = endpoint.Muted,
                            IsDefault = String.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
                            IsCommunications = String.Equals(id, communicationsId, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                    catch (COMException)
                    {
                        // A USB endpoint can disappear between enumeration and activation.
                        // A later enumeration will see it again after reconnection.
                    }
                    finally
                    {
                        if (endpoint != null) endpoint.Dispose();
                        ComObjects.Release(device);
                    }
                }
                return result;
            }
            finally
            {
                ComObjects.Release(collection);
                ComObjects.Release(enumerator);
            }
        }

        public static AudioEndpoint Open(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("An endpoint ID is required.", "id");
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                HResult.Check(enumerator.GetDevice(id, out device));
                EDataFlow flow;
                HResult.Check(((IMMEndpoint)device).GetDataFlow(out flow));
                if (flow != EDataFlow.Capture)
                    throw new ArgumentException("The selected endpoint is not a microphone/capture device.", "id");
                AudioEndpoint endpoint = new AudioEndpoint(device);
                device = null;
                return endpoint;
            }
            finally
            {
                ComObjects.Release(device);
                ComObjects.Release(enumerator);
            }
        }

        private static string ReadDefaultId(IMMDeviceEnumerator enumerator, ERole role)
        {
            IMMDevice device = null;
            try
            {
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, role, out device);
                // No active default microphone is a normal situation after unplugging it.
                if (hr == unchecked((int)0x80070490)) return null;
                HResult.Check(hr);
                return ReadId(device);
            }
            finally { ComObjects.Release(device); }
        }

        private static string ReadId(IMMDevice device)
        {
            IntPtr value = IntPtr.Zero;
            try
            {
                HResult.Check(device.GetId(out value));
                return Marshal.PtrToStringUni(value);
            }
            finally
            {
                if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value);
            }
        }

        private static string ReadName(IMMDevice device)
        {
            IPropertyStore store = null;
            PropVariant value = new PropVariant();
            try
            {
                HResult.Check(device.OpenPropertyStore(0, out store)); // STGM_READ
                PropertyKey key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
                HResult.Check(store.GetValue(ref key, out value));
                if (value.VarType == 31) return Marshal.PtrToStringUni(value.PointerValue); // VT_LPWSTR
                if (value.VarType == 8) return Marshal.PtrToStringBSTR(value.PointerValue); // VT_BSTR
                return null;
            }
            finally
            {
                NativeMethods.PropVariantClear(ref value);
                ComObjects.Release(store);
            }
        }
    }

    // Keep each instance on its creating thread and dispose it before reconnecting.
    // Only the endpoint level can be written; this API deliberately has no mute setter.
    public sealed class AudioEndpoint : IVolumeEndpoint
    {
        private IMMDevice device;
        private IAudioEndpointVolume volume;
        private readonly Guid eventContext = new Guid("73382806-b768-47ba-9d19-af96c0885b61");

        internal AudioEndpoint(IMMDevice sourceDevice)
        {
            object activated = null;
            try
            {
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                HResult.Check(sourceDevice.Activate(ref iid, 23, IntPtr.Zero, out activated)); // CLSCTX_ALL
                volume = (IAudioEndpointVolume)activated;
                device = sourceDevice;
            }
            catch
            {
                ComObjects.Release(activated);
                throw;
            }
        }

        public float Level
        {
            get
            {
                EnsureOpen();
                float level;
                HResult.Check(volume.GetMasterVolumeLevelScalar(out level));
                return level;
            }
            set
            {
                EnsureOpen();
                if (Single.IsNaN(value) || Single.IsInfinity(value) || value < 0.0f || value > 1.0f)
                    throw new ArgumentOutOfRangeException("value", "Microphone level must be between 0 and 1.");
                Guid context = eventContext;
                HResult.Check(volume.SetMasterVolumeLevelScalar(value, ref context));
            }
        }

        public bool Muted
        {
            get
            {
                EnsureOpen();
                bool muted;
                HResult.Check(volume.GetMute(out muted));
                return muted;
            }
        }

        public uint State
        {
            get
            {
                EnsureOpen();
                uint state;
                HResult.Check(device.GetState(out state));
                return state;
            }
        }

        public void Dispose()
        {
            IAudioEndpointVolume oldVolume = volume;
            IMMDevice oldDevice = device;
            volume = null;
            device = null;
            ComObjects.Release(oldVolume);
            ComObjects.Release(oldDevice);
        }

        private void EnsureOpen()
        {
            if (device == null || volume == null) throw new ObjectDisposedException("AudioEndpoint");
        }
    }

    internal static class HResult
    {
        internal static void Check(int value)
        {
            if (value < 0) Marshal.ThrowExceptionForHR(value);
        }
    }

    internal static class ComObjects
    {
        internal static void Release(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try { Marshal.ReleaseComObject(value); }
            catch (InvalidComObjectException) { }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = true)]
        internal static extern int PropVariantClear(ref PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        internal Guid FormatId;
        internal uint PropertyId;
        internal PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariantBlob
    {
        internal uint Size;
        internal IntPtr Data;
    }

    // The BLOB union member makes the native layout 24 bytes on x64 and
    // 16 bytes on x86. A fixed 16-byte PROPVARIANT would corrupt x64 memory.
    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] internal ushort VarType;
        [FieldOffset(2)] internal ushort Reserved1;
        [FieldOffset(4)] internal ushort Reserved2;
        [FieldOffset(6)] internal ushort Reserved3;
        [FieldOffset(8)] internal IntPtr PointerValue;
        [FieldOffset(8)] internal PropVariantBlob Blob;
    }

    internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint classContext, IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint accessMode, out IPropertyStore properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMEndpoint
    {
        [PreserveSig] int GetDataFlow(out EDataFlow dataFlow);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        // Required vtable slot only. Public wrapper never calls this method.
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint supportMask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float stepDb);
    }
}
