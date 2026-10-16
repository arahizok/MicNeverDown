using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace MicNeverDown
{
    // Field names and XML roots match the original portable version.
    public sealed class Settings
    {
        public string DeviceId;
        public string DeviceName;
        public float Level;

        public Settings Copy()
        {
            return new Settings { DeviceId = DeviceId, DeviceName = DeviceName, Level = Level };
        }
    }

    public sealed class GuardStatus
    {
        public string UpdatedUtc;
        public string State;
        public string DeviceName;
        public string DeviceId;
        public float TargetLevel;
        public float ActualLevel;
        public bool Muted;
        public long Corrections;
        public int ProcessId;
        public string Error;
        // ActualLevel and Muted are meaningful only when this flag is true.
        public bool DeviceAvailable;
    }

    public static class Storage
    {
        private const int MaximumDocumentBytes = 64 * 1024;
        private const int MaximumErrorCharacters = 16 * 1024;

        // Keep portable configuration beside the executable. A read-only install
        // location produces a normal save error rather than a hidden second config.
        public static readonly string DirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        public static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.xml");
        public static readonly string StatusPath = Path.Combine(DirectoryPath, "status.xml");

        public static T Read<T>(string path)
        {
            XmlReaderSettings options = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumDocumentBytes,
                CloseInput = false
            };
            // Delete sharing permits a concurrent atomic settings replacement;
            // this stream still reads one complete version of the file.
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete))
            {
                if (stream.Length > MaximumDocumentBytes)
                    throw new InvalidDataException(Ui.Text("The settings file exceeds the 64 KiB limit.", "Файл настроек превышает допустимый размер 64 КиБ."));
                using (XmlReader reader = XmlReader.Create(stream, options))
                    return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
            }
        }

        public static void Write<T>(string path, T value)
        {
            WriteAtomically(path, delegate(Stream stream)
            {
                XmlWriterSettings options = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = true,
                    CloseOutput = false
                };
                using (XmlWriter writer = XmlWriter.Create(stream, options))
                    new XmlSerializer(typeof(T)).Serialize(writer, value);
            });
        }

        public static void Validate(Settings settings)
        {
            if (settings == null || String.IsNullOrWhiteSpace(settings.DeviceId)
                || String.IsNullOrWhiteSpace(settings.DeviceName)
                || Single.IsNaN(settings.Level) || Single.IsInfinity(settings.Level)
                || settings.Level < 0.0f || settings.Level > 1.0f)
                throw new InvalidDataException(Ui.Text("Invalid microphone settings.", "Некорректные настройки микрофона."));
        }

        // Diagnostics must never replace or hide the original exception when the
        // portable folder itself is unavailable, read-only, or out of disk space.
        public static void WriteError(Exception error)
        {
            if (error == null) return;
            try
            {
                string content = DateTime.UtcNow.ToString("o") + Environment.NewLine + error.ToString();
                if (content.Length > MaximumErrorCharacters)
                    content = content.Substring(0, MaximumErrorCharacters);
                WriteAtomically(Path.Combine(DirectoryPath, "last-error.txt"), delegate(Stream stream)
                {
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                        writer.Write(content);
                });
            }
            catch
            {
                // Logging is best-effort; the caller still reports its error.
            }
        }

        private static void WriteAtomically(string path, Action<Stream> write)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", "path");
            string destination = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            // Unique, same-directory temporary files avoid clashes between CLI
            // commands while allowing the final replacement to remain atomic.
            string temporary = Path.Combine(directory, "." + Path.GetFileName(destination)
                + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    write(stream);
                    stream.Flush(true);
                }
                // Keep content replacement atomic. A portable folder may allow
                // file modification without allowing Windows to merge every
                // optional ACL/metadata field from the old file.
                if (File.Exists(destination)) File.Replace(temporary, destination, null, true);
                else
                {
                    try { File.Move(temporary, destination); }
                    catch (IOException)
                    {
                        // Another writer may have created the destination after
                        // the existence check. Its completed file can be replaced.
                        if (!File.Exists(temporary) || !File.Exists(destination)) throw;
                        File.Replace(temporary, destination, null, true);
                    }
                }
            }
            finally
            {
                // Never remove another writer's temporary file.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }
    }
}
