using System.Text.Json;
using System.Text.Json.Serialization;
using LumenvilLite.Models;

namespace LumenvilLite.Services;

public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _lock = new();

    public IReadOnlyList<ProjectEntry> List()
    {
        lock (_lock)
        {
            return ReadAll();
        }
    }

    public ProjectEntry? Get(string name)
    {
        lock (_lock)
        {
            return ReadAll().FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public ProjectEntry Add(ProjectEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            throw new ArgumentException("Project name is required.");
        }
        if (string.IsNullOrWhiteSpace(entry.ProjectPath))
        {
            throw new ArgumentException("Project path is required.");
        }
        // ExecuteMethod is optional — when omitted, the server falls back to
        // the built-in LumenvilLiteBuilder shipped with the Unity package.

        lock (_lock)
        {
            var current = ReadAll().ToList();
            if (current.Any(p => string.Equals(p.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Project '{entry.Name}' already exists.");
            }
            current.Add(entry);
            WriteAll(current);
            return entry;
        }
    }

    public bool Remove(string name)
    {
        lock (_lock)
        {
            var current = ReadAll().ToList();
            var removed = current.RemoveAll(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                WriteAll(current);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Replaces the entry whose key is <paramref name="name"/> with
    /// <paramref name="updated"/>. The key is matched case-insensitively
    /// and the new entry's name overwrites it (so renames are allowed,
    /// as long as the new name does not collide with another entry).
    /// </summary>
    public ProjectEntry? Update(string name, ProjectEntry updated)
    {
        if (string.IsNullOrWhiteSpace(updated.Name))
        {
            throw new ArgumentException("Project name is required.");
        }
        if (string.IsNullOrWhiteSpace(updated.ProjectPath))
        {
            throw new ArgumentException("Project path is required.");
        }

        lock (_lock)
        {
            var current = ReadAll().ToList();
            var index = current.FindIndex(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return null;
            }

            var renaming = !string.Equals(current[index].Name, updated.Name, StringComparison.OrdinalIgnoreCase);
            if (renaming && current.Any(p =>
                    string.Equals(p.Name, updated.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Project '{updated.Name}' already exists.");
            }

            current[index] = updated;
            WriteAll(current);
            return updated;
        }
    }

    private static IReadOnlyList<ProjectEntry> ReadAll()
    {
        StoragePaths.EnsureRoot();
        var path = StoragePaths.ProjectsFile;
        if (!File.Exists(path))
        {
            return Array.Empty<ProjectEntry>();
        }
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<ProjectEntry>();
            }
            return JsonSerializer.Deserialize<List<ProjectEntry>>(json, JsonOptions)
                   ?? new List<ProjectEntry>();
        }
        catch
        {
            // Corrupt file — start over rather than 500 every endpoint.
            return Array.Empty<ProjectEntry>();
        }
    }

    private static void WriteAll(IReadOnlyList<ProjectEntry> entries)
    {
        StoragePaths.EnsureRoot();
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(StoragePaths.ProjectsFile, json);
    }
}
