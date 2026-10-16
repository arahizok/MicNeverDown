using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MicNeverDown;

// This console harness never constructs CoreAudioProvider or a Windows shortcut.
// All endpoints are fakes; all files stay beneath this executable's work folder.
public static class TestV11Core
{
    private static readonly DateTime Start = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Scratch = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "storage");
    private static int passed;
    private static int failed;

    public static int Main()
    {
        Directory.CreateDirectory(Scratch);
        Run("unconfigured guard does no device work", TestUnconfigured);
        Run("settings snapshots do not alias callers", TestCopies);
        Run("initial lock, correction, and stable polling", TestCorrection);
        Run("hardware quantization does not cause repeated writes", TestQuantization);
        Run("small level jitter is tolerated", TestTolerance);
        Run("zero and maximum target boundaries", TestBoundaryLevels);
        Run("pause clears status and resume immediately reconnects", TestPause);
        Run("only the exact endpoint ID can be selected", TestExactId);
        Run("missing device retries after two seconds", TestMissingDeviceBackoff);
        Run("disconnect disposes and reconnects without stale status", TestReconnect);
        Run("enumeration exception is reported and retried", TestEnumerationFailure);
        Run("activation exception is reported and retried", TestActivationFailure);
        Run("read exception is reported and endpoint disposed", TestReadFailure);
        Run("write exception is reported and endpoint disposed", TestWriteFailure);
        Run("state exception is reported and endpoint disposed", TestStateFailure);
        Run("mute read exception invalidates measurement", TestMuteReadFailure);
        Run("invalid driver levels are rejected", TestInvalidDriverLevel);
        Run("mute state remains unchanged by all volume writes", TestMutePreservation);
        Run("reconfiguration releases old endpoint and applies new target", TestReconfigure);
        Run("guard disposal is idempotent and stops later polling", TestDispose);
        Run("settings XML roundtrip and legacy compatibility", TestRoundtrip);
        Run("status XML preserves availability and readings", TestStatusRoundtrip);
        Run("invalid settings and nonfinite levels are rejected", TestValidation);
        Run("internal and external DTDs are prohibited", TestDtd);
        Run("oversized XML is rejected before parsing", TestOversized);
        Run("malformed XML is rejected without source mutation", TestMalformed);
        Run("atomic writes preserve existing reader snapshot", TestAtomicSnapshot);
        Run("failed writes clean only their temporary file", TestTemporaryCleanup);
        Run("concurrent writes retain complete parseable XML", TestConcurrentWrites);
        Run("diagnostic logging is bounded and best-effort", TestErrorLogging);
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Near(float actual, float expected, string message)
    {
        Check(Math.Abs(actual - expected) < 0.00001f, message + ": " + actual + " != " + expected);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static Settings Config(float level)
    {
        return new Settings { DeviceId = "mic-A", DeviceName = "Headset", Level = level };
    }

    private static FakeProvider Provider(FakeEndpoint endpoint)
    {
        FakeProvider provider = new FakeProvider();
        provider.Devices.Add(new AudioDeviceInfo { Id = "mic-A", Name = "Headset", IsCommunications = true, IsDefault = true });
        provider.Endpoints.Enqueue(endpoint);
        return provider;
    }

    private static void TestUnconfigured()
    {
        Throws<ArgumentNullException>(delegate { new GuardEngine(null); });
        FakeProvider provider = new FakeProvider();
        using (GuardEngine engine = new GuardEngine(provider))
        {
            engine.Poll(Start);
            Check(engine.Status.State == "Unconfigured" && !engine.Status.DeviceAvailable, "Expected unconfigured status");
            Check(provider.ListCalls == 0 && provider.OpenCalls == 0, "Unconfigured poll accessed audio");
        }
    }

    private static void TestCopies()
    {
        Settings original = Config(0.6f);
        using (GuardEngine engine = new GuardEngine(Provider(new FakeEndpoint())))
        {
            engine.Configure(original);
            original.Level = 0.9f;
            original.DeviceId = "changed";
            Settings copy = engine.Configuration;
            Near(copy.Level, 0.6f, "Input settings were not copied");
            Check(copy.DeviceId == "mic-A", "Input device ID alias");
            copy.Level = 0.1f;
            Near(engine.Configuration.Level, 0.6f, "Returned settings were not copied");
        }
    }

    private static void TestCorrection()
    {
        FakeEndpoint endpoint = new FakeEndpoint { RawLevel = 0.3f };
        using (GuardEngine engine = new GuardEngine(Provider(endpoint)))
        {
            engine.Configure(Config(0.8f));
            engine.Poll(Start);
            Check(endpoint.SetCalls == 1 && engine.Status.State == "Locked" && engine.Status.DeviceAvailable, "Initial lock missing");
            Near(engine.Status.ActualLevel, 0.8f, "Initial measurement");
            endpoint.RawLevel = 0.25f;
            engine.Poll(Start.AddMilliseconds(150));
            Check(endpoint.SetCalls == 2 && engine.Status.Corrections == 1, "External change not corrected exactly once");
            Near(endpoint.RawLevel, 0.8f, "Correction did not restore target");
            engine.Poll(Start.AddMilliseconds(300));
            Check(endpoint.SetCalls == 2 && engine.Status.Corrections == 1, "Stable polling wrote again");
        }
    }

    private static void TestQuantization()
    {
        FakeEndpoint endpoint = new FakeEndpoint { Step = 0.05f };
        using (GuardEngine engine = new GuardEngine(Provider(endpoint)))
        {
            engine.Configure(Config(0.333f));
            engine.Poll(Start);
            Near(engine.Status.TargetLevel, 0.333f, "Target was altered");
            Near(engine.Status.ActualLevel, 0.35f, "Driver quantization was not read back");
            for (int index = 1; index <= 8; index++) engine.Poll(Start.AddMilliseconds(index * 150));
            Check(endpoint.SetCalls == 1 && engine.Status.Corrections == 0, "Quantized value caused an endless correction loop");
            endpoint.RawLevel = 0.2f;
            engine.Poll(Start.AddMilliseconds(1500));
            Check(endpoint.SetCalls == 2 && engine.Status.Corrections == 1, "Quantized endpoint was not corrected");
            engine.Poll(Start.AddMilliseconds(1650));
            Check(endpoint.SetCalls == 2, "Correction did not update effective level");
        }
    }

    private static void TestTolerance()
    {
        FakeEndpoint endpoint = new FakeEndpoint();
        using (GuardEngine engine = new GuardEngine(Provider(endpoint)))
        {
            engine.Configure(Config(0.5f));
            engine.Poll(Start);
            endpoint.RawLevel = 0.5004f;
            engine.Poll(Start.AddMilliseconds(150));
            Check(endpoint.SetCalls == 1, "Insignificant jitter caused a write");
            endpoint.RawLevel = 0.501f;
            engine.Poll(Start.AddMilliseconds(300));
            Check(endpoint.SetCalls == 2, "Meaningful level change was ignored");
        }
    }

    private static void TestBoundaryLevels()
    {
        foreach (float target in new float[] { 0, 1 })
        {
            FakeEndpoint endpoint = new FakeEndpoint();
            using (GuardEngine engine = new GuardEngine(Provider(endpoint)))
            {
                engine.Configure(Config(target));
                engine.Poll(Start);
                Check(engine.Status.State == "Locked", "Boundary target could not lock");
                Near(endpoint.RawLevel, target, "Boundary target was changed");
            }
        }
    }

    private static void TestPause()
    {
        FakeEndpoint first = new FakeEndpoint { IsMuted = true };
        FakeEndpoint second = new FakeEndpoint { IsMuted = true };
        FakeProvider provider = Provider(first);
        provider.Endpoints.Enqueue(second);
        using (GuardEngine engine = new GuardEngine(provider))
        {
            engine.Configure(Config(0.7f));
            engine.Poll(Start);
            engine.Paused = true;
            Check(first.DisposeCalls == 1, "Pause retained active endpoint");
            engine.Poll(Start.AddMilliseconds(150));
            Check(engine.Status.State == "Paused" && !engine.Status.DeviceAvailable, "Pause status is wrong");
            Check(engine.Status.ActualLevel == 0 && !engine.Status.Muted, "Paused status retained stale readings");
            Check(provider.ListCalls == 1 && provider.OpenCalls == 1 && first.SetCalls == 1, "Pause accessed device");
            engine.Paused = false;
            engine.Poll(Start.AddMilliseconds(300));
            Check(provider.OpenCalls == 2 && engine.Status.State == "Locked", "Resume did not immediately reconnect");
            Near(second.RawLevel, 0.7f, "Resume target");
            Check(second.IsMuted, "Resume changed mute");
        }
    }

    private static void TestExactId()
    {
        FakeProvider provider = new FakeProvider();
        provider.Devices.Add(new AudioDeviceInfo { Id = "mic-B", Name = "Headset", IsDefault = true, IsCommunications = true });
        provider.Devices.Add(new AudioDeviceInfo { Id = "MIC-A", Name = "Headset" });
        provider.Endpoints.Enqueue(new FakeEndpoint());
        using (GuardEngine engine = new GuardEngine(provider))
        {
            engine.Configure(Config(0.7f));
            engine.Poll(Start);
            Check(provider.OpenCalls == 0 && engine.Status.State == "WaitingForDevice", "Selected a different ID/name/default");
            provider.Devices.Add(new AudioDeviceInfo { Id = "mic-A", Name = "Renamed Headset" });
            engine.Poll(Start.AddSeconds(2));
            Check(provider.OpenCalls == 1 && provider.OpenedIds[0] == "mic-A", "Exact endpoint ID was not selected");
        }
    }

    private static void TestMissingDeviceBackoff()
    {
        FakeProvider provider = new FakeProvider();
        using (GuardEngine engine = new GuardEngine(provider))
        {
            engine.Configure(Config(0.6f));
            engine.Poll(Start);
            engine.Poll(Start.AddMilliseconds(150));
            engine.Poll(Start.AddMilliseconds(1999));
            Check(provider.ListCalls == 1 && provider.OpenCalls == 0, "Missing device did not back off");
            engine.Poll(Start.AddSeconds(2));
            Check(provider.ListCalls == 2 && engine.Status.State == "WaitingForDevice", "Retry timing changed");
        }
    }

    private static void TestReconnect()
    {
        FakeEndpoint first = new FakeEndpoint { IsMuted = true };
        FakeEndpoint second = new FakeEndpoint { IsMuted = false };
        FakeProvider provider = Provider(first);
        provider.Endpoints.Enqueue(second);
        using (GuardEngine engine = new GuardEngine(provider))
        {
            engine.Configure(Config(0.7f));
            engine.Poll(Start);
            first.StateValue = 2;
            engine.Poll(Start.AddMilliseconds(150));
            Check(first.DisposeCalls == 1 && !engine.Status.DeviceAvailable, "Disconnected device not disposed/invalidated");
            Check(engine.Status.State == "WaitingForDevice" && engine.Status.ActualLevel == 0 && !engine.Status.Muted, "Stale status after disconnect");
            engine.Poll(Start.AddMilliseconds(1999));
            Check(provider.OpenCalls == 1, "Reconnect ignored backoff");
            engine.Poll(Start.AddSeconds(2));
            Check(provider.OpenCalls == 2 && engine.Status.State == "Locked", "Reconnect failed");
            Near(second.RawLevel, 0.7f, "Reconnected target");
        }
    }

    private static void TestEnumerationFailure()
    {
        FakeProvider provider = Provider(new FakeEndpoint());
        provider.ListError = new IOException("enumeration failed");
        using (GuardEngine engine = new GuardEngine(provider))
        {
            engine.Configure(Config(0.6f));
            engine.Poll(Start);
            Check(engine.Status.Error == "enumeration failed" && !engine.Status.DeviceAvailable, "Enumeration error not exposed");
            provider.ListError = null;
            engine.Poll(Start.AddMilliseconds(1999));
            Check(provider.ListCalls == 1, "Failure retry had no backoff");
            engine.Poll(Start.AddSeconds(2));
            Check(engine.Status.State == "Locked" && engine.Status.Error == null, "Enumeration recovery failed");
        }
    }

    private static void TestActivationFailure()
    {
        FakeProvider provider = Provider(new FakeEndpoint());
        provider.OpenError = new IOException("activation failed");
        using (GuardEngine engine = new GuardEngine(provider))
        {
            engine.Configure(Config(0.6f));
            engine.Poll(Start);
            Check(engine.Status.Error == "activation failed" && !engine.Status.DeviceAvailable, "Activation error not exposed");
            provider.OpenError = null;
            engine.Poll(Start.AddSeconds(1));
            Check(provider.OpenCalls == 1, "Activation retry ignored backoff");
            engine.Poll(Start.AddSeconds(2));
            Check(engine.Status.State == "Locked" && engine.Status.Error == null, "Activation recovery failed");
        }
    }

    private static void TestReadFailure()
    {
        FakeEndpoint endpoint = new FakeEndpoint();
        FakeProvider provider = Provider(endpoint);
        provider.Endpoints.Enqueue(new FakeEndpoint());
        using (GuardEngine engine = new GuardEngine(provider))
        {
            engine.Configure(Config(0.6f));
            engine.Poll(Start);
            endpoint.ReadError = new IOException("read failed");
            engine.Poll(Start.AddMilliseconds(150));
            Check(engine.Status.Error == "read failed" && endpoint.DisposeCalls == 1, "Read failure not handled/disposed");
            Check(!engine.Status.DeviceAvailable && engine.Status.ActualLevel == 0, "Read failure left stale data");
            engine.Poll(Start.AddMilliseconds(2149));
            Check(provider.OpenCalls == 1, "Read failure retry ignored backoff");
            engine.Poll(Start.AddMilliseconds(2150));
            Check(engine.Status.State == "Locked", "Read failure recovery failed");
        }
    }

    private static void TestWriteFailure()
    {
        FakeEndpoint endpoint = new FakeEndpoint { WriteError = new IOException("write failed") };
        using (GuardEngine engine = new GuardEngine(Provider(endpoint)))
        {
            engine.Configure(Config(0.6f));
            engine.Poll(Start);
            Check(engine.Status.Error == "write failed" && endpoint.DisposeCalls == 1, "Write failure not handled/disposed");
            Check(engine.Status.State == "WaitingForDevice" && !engine.Status.DeviceAvailable, "Write error status wrong");
        }
    }

    private static void TestStateFailure()
    {
        FakeEndpoint endpoint = new FakeEndpoint();
        using (GuardEngine engine = new GuardEngine(Provider(endpoint)))
        {
            engine.Configure(Config(0.6f));
            engine.Poll(Start);
            endpoint.StateError = new IOException("state failed");
            engine.Poll(Start.AddMilliseconds(150));
            Check(engine.Status.Error == "state failed" && endpoint.DisposeCalls == 1, "State failure not handled/disposed");
        }
    }

    private static void TestMuteReadFailure()
    {
        FakeEndpoint endpoint = new FakeEndpoint { MuteError = new IOException("mute read failed") };
        using (GuardEngine engine = new GuardEngine(Provider(endpoint)))
        {
            engine.Configure(Config(0.6f));
            engine.Poll(Start);
            Check(engine.Status.Error == "mute read failed" && !engine.Status.DeviceAvailable, "Mute read error retained valid measurement");
            Check(engine.Status.ActualLevel == 0 && endpoint.DisposeCalls == 1, "Mute read error cleanup failed");
        }
    }

    private static void TestInvalidDriverLevel()
    {
        foreach (float value in new float[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity, -0.1f, 1.1f })
        {
            FakeEndpoint endpoint = new FakeEndpoint { OverrideRead = value };
            using (GuardEngine engine = new GuardEngine(Provider(endpoint)))
            {
                engine.Configure(Config(0.6f));
                engine.Poll(Start);
                Check(engine.Status.State == "WaitingForDevice" && !engine.Status.DeviceAvailable && engine.Status.Error != null, "Invalid driver value accepted");
                Check(endpoint.DisposeCalls == 1, "Invalid driver endpoint retained");
            }
        }
    }

    private static void TestMutePreservation()
    {
        foreach (bool muted in new bool[] { false, true })
        {
            FakeEndpoint endpoint = new FakeEndpoint { IsMuted = muted };
            using (GuardEngine engine = new GuardEngine(Provider(endpoint)))
            {
                engine.Configure(Config(1.0f));
                engine.Poll(Start);
                endpoint.RawLevel = 0.2f;
                engine.Poll(Start.AddMilliseconds(150));
                Check(endpoint.IsMuted == muted && engine.Status.Muted == muted, "Guard changed mute state");
                endpoint.IsMuted = !muted;
                engine.Poll(Start.AddMilliseconds(300));
                Check(endpoint.IsMuted == !muted && engine.Status.Muted == !muted, "External mute change was reversed/ignored");
                Check(endpoint.SetCalls == 2, "Mute change caused a volume write");
            }
        }
    }

    private static void TestReconfigure()
    {
        FakeEndpoint first = new FakeEndpoint();
        FakeEndpoint second = new FakeEndpoint();
        FakeProvider provider = Provider(first);
        provider.Endpoints.Enqueue(second);
        provider.Devices.Add(new AudioDeviceInfo { Id = "mic-B", Name = "Second" });
        using (GuardEngine engine = new GuardEngine(provider))
        {
            engine.Configure(Config(0.6f));
            engine.Poll(Start);
            engine.Paused = true;
            Settings replacement = new Settings { DeviceId = "mic-B", DeviceName = "Second", Level = 0.4f };
            engine.Configure(replacement);
            engine.Poll(Start.AddMilliseconds(150));
            Check(first.DisposeCalls == 1 && !engine.Paused && provider.OpenedIds[1] == "mic-B", "Reconfiguration lifecycle failed");
            Near(second.RawLevel, 0.4f, "Replacement target");
        }
    }

    private static void TestDispose()
    {
        FakeEndpoint endpoint = new FakeEndpoint();
        GuardEngine engine = new GuardEngine(Provider(endpoint));
        engine.Configure(Config(0.5f));
        engine.Poll(Start);
        engine.Dispose();
        engine.Dispose();
        Check(endpoint.DisposeCalls == 1 && engine.Status.State == "Stopped" && !engine.Status.DeviceAvailable, "Dispose was not idempotent");
        Throws<ObjectDisposedException>(delegate { engine.Poll(Start); });
        Throws<ObjectDisposedException>(delegate { engine.Configure(Config(0.4f)); });
    }

    private static string FilePath(string name) { return Path.Combine(Scratch, name); }

    private static void TestRoundtrip()
    {
        string path = FilePath("settings.xml");
        Settings original = new Settings { DeviceId = "{0.0.1}.test<&>", DeviceName = "Гарнитура & headset <test>", Level = 0.375f };
        Storage.Write(path, original);
        Settings read = Storage.Read<Settings>(path);
        Storage.Validate(read);
        Check(read.DeviceId == original.DeviceId && read.DeviceName == original.DeviceName, "Unicode/XML escaping roundtrip failed");
        Near(read.Level, original.Level, "Roundtrip level");
        File.WriteAllText(path, "<?xml version=\"1.0\"?><Settings><DeviceId>mic-A</DeviceId><DeviceName>Legacy</DeviceName><Level>0.5</Level></Settings>");
        read = Storage.Read<Settings>(path);
        Storage.Validate(read);
        Check(read.DeviceName == "Legacy", "Old settings schema was not accepted");
        Near(read.Level, 0.5f, "Legacy level");
    }

    private static void TestStatusRoundtrip()
    {
        string path = FilePath("status.xml");
        GuardStatus status = new GuardStatus { State = "Locked", UpdatedUtc = Start.ToString("o"), DeviceAvailable = true,
            DeviceId = "mic-A", DeviceName = "Headset", TargetLevel = 0.6f, ActualLevel = 0.6f,
            Muted = true, Corrections = 3, ProcessId = 123, Error = null };
        Storage.Write(path, status);
        GuardStatus read = Storage.Read<GuardStatus>(path);
        Check(read.DeviceAvailable && read.Muted && read.Corrections == 3 && read.ProcessId == 123, "Status fields lost");
        Near(read.ActualLevel, 0.6f, "Status reading");
    }

    private static void TestValidation()
    {
        Throws<InvalidDataException>(delegate { Storage.Validate(null); });
        foreach (float value in new float[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity, -0.001f, 1.001f })
        {
            float captured = value;
            Throws<InvalidDataException>(delegate { Storage.Validate(Config(captured)); });
        }
        foreach (string value in new string[] { null, "", "  " })
        {
            Settings noId = Config(0.5f); noId.DeviceId = value;
            Throws<InvalidDataException>(delegate { Storage.Validate(noId); });
            Settings noName = Config(0.5f); noName.DeviceName = value;
            Throws<InvalidDataException>(delegate { Storage.Validate(noName); });
        }
        Storage.Validate(Config(0));
        Storage.Validate(Config(1));
    }

    private static void TestDtd()
    {
        string path = FilePath("dtd.xml");
        File.WriteAllText(path, "<!DOCTYPE Settings [<!ENTITY name 'expanded'>]><Settings><DeviceId>mic-A</DeviceId><DeviceName>&name;</DeviceName><Level>0.5</Level></Settings>");
        Throws<InvalidOperationException>(delegate { Storage.Read<Settings>(path); });
        File.WriteAllText(path, "<!DOCTYPE Settings SYSTEM 'file:///definitely-not-a-real-file'><Settings><DeviceId>mic-A</DeviceId><DeviceName>test</DeviceName><Level>0.5</Level></Settings>");
        Throws<InvalidOperationException>(delegate { Storage.Read<Settings>(path); });
    }

    private static void TestOversized()
    {
        string path = FilePath("oversized.xml");
        File.WriteAllText(path, new string(' ', 65537), new UTF8Encoding(false));
        Throws<InvalidDataException>(delegate { Storage.Read<Settings>(path); });
    }

    private static void TestMalformed()
    {
        string path = FilePath("malformed.xml");
        File.WriteAllText(path, "<Settings><DeviceId>broken");
        Throws<InvalidOperationException>(delegate { Storage.Read<Settings>(path); });
        Check(File.ReadAllText(path) == "<Settings><DeviceId>broken", "Reading malformed XML mutated source");
    }

    private static void TestAtomicSnapshot()
    {
        string path = FilePath("snapshot.xml");
        Storage.Write(path, Config(0.2f));
        using (FileStream reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            Storage.Write(path, Config(0.8f));
            using (StreamReader text = new StreamReader(reader, Encoding.UTF8, true, 1024, true))
                Check(text.ReadToEnd().Contains("<Level>0.2</Level>"), "Existing reader saw mixed/new file");
        }
        Near(Storage.Read<Settings>(path).Level, 0.8f, "New reader did not see replacement");
        Check(Directory.GetFiles(Scratch, ".snapshot.xml.*.tmp").Length == 0, "Successful write left temp files");
    }

    private static void TestTemporaryCleanup()
    {
        string destination = FilePath("blocked-destination");
        Directory.CreateDirectory(destination);
        string sentinel = FilePath(".someone-elses.tmp");
        File.WriteAllText(sentinel, "keep");
        Throws<IOException>(delegate { Storage.Write(destination, Config(0.5f)); });
        Check(Directory.GetFiles(Scratch, ".blocked-destination.*.tmp").Length == 0, "Failed write left owned temp file");
        Check(File.ReadAllText(sentinel) == "keep", "Cleanup affected unrelated temp file");
    }

    private static void TestConcurrentWrites()
    {
        string path = FilePath("concurrent.xml");
        if (File.Exists(path)) File.Delete(path);
        int completed = 0;
        int conflicts = 0;
        Task[] writers = new Task[6];
        for (int index = 0; index < writers.Length; index++)
        {
            int captured = index;
            writers[index] = Task.Run(delegate
            {
                for (int iteration = 0; iteration < 10; iteration++)
                {
                    try
                    {
                        Storage.Write(path, Config((captured + 1) / 10.0f));
                        Interlocked.Increment(ref completed);
                    }
                    catch (IOException)
                    {
                        // ReplaceFile reports competing writers explicitly. The
                        // app serializes normal writes on its UI thread/mutex;
                        // this stress test checks complete-file integrity and
                        // cleanup, rather than promising concurrent save success.
                        Interlocked.Increment(ref conflicts);
                    }
                }
            });
        }
        Task.WaitAll(writers);
        Check(completed > 0 && completed + conflicts == 60, "No concurrent write completed");
        Settings result = Storage.Read<Settings>(path);
        Storage.Validate(result);
        Check(result.Level >= 0.1f && result.Level <= 0.6f, "Concurrent write damaged settings");
        Check(Directory.GetFiles(Scratch, ".concurrent.xml.*.tmp").Length == 0, "Concurrent writes left temp files");
        Console.WriteLine("  stress: " + completed + " complete writes, " + conflicts + " explicit competing-writer IO conflicts");
    }

    private static void TestErrorLogging()
    {
        // This data path belongs to the console harness under work, not the app.
        string log = Path.Combine(Storage.DirectoryPath, "last-error.txt");
        Directory.CreateDirectory(Storage.DirectoryPath);
        if (File.Exists(log)) File.Delete(log);
        Directory.CreateDirectory(log);
        try { Storage.WriteError(new Exception("This log failure must not escape")); }
        finally { Directory.Delete(log); }
        Storage.WriteError(new Exception(new string('x', 50000)));
        Check(File.ReadAllText(log).Length <= 16384, "Diagnostic file was not bounded");
        Storage.WriteError(null);
    }

    private sealed class FakeProvider : IAudioProvider
    {
        public readonly List<AudioDeviceInfo> Devices = new List<AudioDeviceInfo>();
        public readonly Queue<FakeEndpoint> Endpoints = new Queue<FakeEndpoint>();
        public readonly List<string> OpenedIds = new List<string>();
        public int ListCalls;
        public int OpenCalls;
        public Exception ListError;
        public Exception OpenError;

        public List<AudioDeviceInfo> ListCapture()
        {
            ListCalls++;
            if (ListError != null) throw ListError;
            return new List<AudioDeviceInfo>(Devices);
        }

        public IVolumeEndpoint Open(string id)
        {
            OpenCalls++;
            OpenedIds.Add(id);
            if (OpenError != null) throw OpenError;
            if (Endpoints.Count == 0) throw new InvalidOperationException("Unexpected extra fake endpoint activation");
            return Endpoints.Dequeue();
        }
    }

    private sealed class FakeEndpoint : IVolumeEndpoint
    {
        public float RawLevel;
        public float Step;
        public float? OverrideRead;
        public bool IsMuted;
        public uint StateValue = 1;
        public int SetCalls;
        public int DisposeCalls;
        public Exception ReadError;
        public Exception WriteError;
        public Exception StateError;
        public Exception MuteError;

        public float Level
        {
            get
            {
                EnsureOpen();
                if (ReadError != null) throw ReadError;
                return OverrideRead.HasValue ? OverrideRead.Value : RawLevel;
            }
            set
            {
                EnsureOpen();
                SetCalls++;
                if (WriteError != null) throw WriteError;
                RawLevel = Step > 0 ? (float)Math.Round(value / Step) * Step : value;
            }
        }

        public bool Muted
        {
            get { EnsureOpen(); if (MuteError != null) throw MuteError; return IsMuted; }
        }

        public uint State
        {
            get { EnsureOpen(); if (StateError != null) throw StateError; return StateValue; }
        }

        public void Dispose() { DisposeCalls++; }

        private void EnsureOpen()
        {
            if (DisposeCalls > 0) throw new ObjectDisposedException("FakeEndpoint");
        }
    }
}
