using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Durable latest-state journal for patch transactions.</summary>
public sealed class FilePatchJournal : IPatchJournal
{
    private readonly object gate = new object();
    private readonly Dictionary<PatchId, PatchTransactionRecord> records = new Dictionary<PatchId, PatchTransactionRecord>();
    private readonly string path;

    public FilePatchJournal(string journalPath)
    {
        if (string.IsNullOrWhiteSpace(journalPath))
            throw new ArgumentException("A journal path is required.", nameof(journalPath));

        path = Path.GetFullPath(journalPath);
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("The patch journal directory does not exist: " + directory);
        Load();
    }

    public PatchTransactionRecord? Get(PatchId id)
    {
        lock (gate)
        {
            records.TryGetValue(id, out PatchTransactionRecord? record);
            return record;
        }
    }

    public IReadOnlyList<PatchTransactionRecord> GetAll()
    {
        lock (gate)
            return new List<PatchTransactionRecord>(records.Values);
    }

    public void Record(PatchTransactionRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        lock (gate)
        {
            records[record.Id] = record;
            Persist();
        }
    }

    private void Load()
    {
        if (!File.Exists(path))
            return;

        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            string[] fields = line.Split('|');
            if (fields.Length != 4 || !int.TryParse(fields[2], out int rawState) || !Enum.IsDefined(typeof(PatchTransactionState), rawState))
                throw new InvalidDataException("The patch journal contains an invalid transaction record.");
            var id = new PatchId(Decode(fields[0]));
            var owner = new PluginId(Decode(fields[1]));
            string error = Decode(fields[3]);
            records[id] = new PatchTransactionRecord(id, owner, (PatchTransactionState)rawState, error.Length == 0 ? null : error);
        }
    }

    private void Persist()
    {
        string temporary = path + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, Encoding.UTF8))
            {
                foreach (PatchTransactionRecord record in records.Values)
                {
                    writer.WriteLine(Encode(record.Id.Value) + "|" + Encode(record.Owner.Value) + "|" + (int)record.State + "|" + Encode(record.Error ?? string.Empty));
                }
            }

            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Decode(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The patch journal contains invalid encoded data.", exception);
        }
    }
}
