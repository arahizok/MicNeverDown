using System;
using System.Collections.Generic;
using System.IO;

namespace MicNeverDown
{
    public interface IVolumeEndpoint : IDisposable
    {
        float Level { get; set; }
        bool Muted { get; }
        uint State { get; }
    }

    public interface IAudioProvider
    {
        List<AudioDeviceInfo> ListCapture();
        IVolumeEndpoint Open(string id);
    }

    public sealed class CoreAudioProvider : IAudioProvider
    {
        public List<AudioDeviceInfo> ListCapture() { return AudioDevices.ListCapture(); }
        public IVolumeEndpoint Open(string id) { return AudioDevices.Open(id); }
    }

    // All calls occur on the UI thread. No audio stream is opened or recorded.
    public sealed class GuardEngine : IDisposable
    {
        private readonly IAudioProvider audio;
        private IVolumeEndpoint endpoint;
        private Settings configuration;
        private DateTime nextReconnect = DateTime.MinValue;
        private float effectiveLevel;
        private bool paused;
        private bool disposed;
        public GuardStatus Status { get; private set; }
        public Settings Configuration { get { return configuration == null ? null : configuration.Copy(); } }
        public bool Paused
        {
            get { return paused; }
            set
            {
                if (paused == value) return;
                paused = value;
                CloseEndpoint();
                nextReconnect = DateTime.MinValue;
            }
        }

        public GuardEngine(IAudioProvider provider)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            audio = provider;
            Status = new GuardStatus { State = "Unconfigured" };
        }

        public void Configure(Settings value)
        {
            if (disposed) throw new ObjectDisposedException("GuardEngine");
            Storage.Validate(value);
            configuration = value.Copy();
            CloseEndpoint();
            nextReconnect = DateTime.MinValue;
            paused = false;
        }

        public void Poll(DateTime utcNow)
        {
            if (disposed) throw new ObjectDisposedException("GuardEngine");
            Status.UpdatedUtc = utcNow.ToString("o");
            Status.DeviceName = configuration == null ? null : configuration.DeviceName;
            Status.DeviceId = configuration == null ? null : configuration.DeviceId;
            Status.TargetLevel = configuration == null ? 0 : configuration.Level;
            Status.DeviceAvailable = false;
            Status.ActualLevel = 0;
            Status.Muted = false;
            Status.Error = null;
            Status.State = configuration == null ? "Unconfigured" : paused ? "Paused" : "WaitingForDevice";
            if (configuration == null || paused) return;
            try
            {
                if (endpoint != null && endpoint.State != 1) CloseEndpoint();
                if (endpoint == null && utcNow >= nextReconnect)
                {
                    nextReconnect = utcNow.AddSeconds(2);
                    AudioDeviceInfo selected = audio.ListCapture().Find(delegate(AudioDeviceInfo item)
                    {
                        return String.Equals(item.Id, configuration.DeviceId, StringComparison.Ordinal);
                    });
                    if (selected != null)
                    {
                        endpoint = audio.Open(selected.Id);
                        endpoint.Level = configuration.Level;
                        effectiveLevel = ReadLevel();
                    }
                }
                if (endpoint == null) return;
                float current = ReadLevel();
                if (Math.Abs(current - effectiveLevel) > 0.0005f)
                {
                    endpoint.Level = configuration.Level;
                    effectiveLevel = ReadLevel();
                    Status.Corrections++;
                }
                Status.ActualLevel = ReadLevel();
                Status.Muted = endpoint.Muted;
                Status.DeviceAvailable = true;
                Status.State = "Locked";
            }
            catch (Exception error)
            {
                Status.State = "WaitingForDevice";
                Status.DeviceAvailable = false;
                Status.ActualLevel = 0;
                Status.Muted = false;
                Status.Error = error.Message;
                CloseEndpoint();
                nextReconnect = utcNow.AddSeconds(2);
            }
        }

        private float ReadLevel()
        {
            float level = endpoint.Level;
            if (Single.IsNaN(level) || Single.IsInfinity(level) || level < 0 || level > 1)
                throw new InvalidDataException(Ui.Text("The driver returned an invalid microphone level.", "Драйвер вернул некорректный уровень микрофона."));
            return level;
        }

        private void CloseEndpoint()
        {
            IVolumeEndpoint previous = endpoint;
            endpoint = null;
            if (previous != null) previous.Dispose();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CloseEndpoint();
            Status.State = "Stopped";
            Status.DeviceAvailable = false;
            Status.ActualLevel = 0;
            Status.Muted = false;
            Status.UpdatedUtc = DateTime.UtcNow.ToString("o");
        }
    }
}
