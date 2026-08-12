using System.Text;
using ABPLCReaderWriter.Models;
using libplctag;
using libplctag.DataTypes;
using Spectre.Console;

namespace ABPLCReaderWriter.Services;

public class PlcTagService
{
    /// <summary>
    /// Lists controller-scoped tags (and optionally program tags) using the special @tags tag.
    /// Groups results by root name (everything before the first '.').
    /// </summary>
    public async Task<Dictionary<string, List<PlcTagInfo>>> ListTagsGroupedAsync(
        PlcDevice device,
        bool includeProgramTags = true,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        timeout ??= TimeSpan.FromSeconds(8);
        var allTags = new List<PlcTagInfo>();

        // Controller tags
        var controllerTags = await ListTagsAtScopeAsync(device, "@tags", timeout.Value, ct);
        allTags.AddRange(controllerTags);

        if (includeProgramTags)
        {
            // Find Program:xxx entries and list their tags
            var programs = controllerTags
                .Where(t => t.Name.StartsWith("Program:", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .Distinct()
                .ToList();

            foreach (var prog in programs)
            {
                try
                {
                    var progTags = await ListTagsAtScopeAsync(device, $"{prog}.@tags", timeout.Value, ct);
                    allTags.AddRange(progTags);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: could not list tags for {prog}: {ex.Message}[/]");
                }
            }
        }

        // Group by root name
        var grouped = allTags
            .GroupBy(t => t.RootName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Name).ToList(), StringComparer.OrdinalIgnoreCase);

        return grouped;
    }

    private async Task<List<PlcTagInfo>> ListTagsAtScopeAsync(PlcDevice device, string tagName, TimeSpan timeout, CancellationToken ct)
    {
        // Prefer the high-level mapper when available (still present though marked obsolete)
#pragma warning disable CS0618
        using var tag = new Tag<TagInfoPlcMapper, TagInfo[]>
        {
            Name = tagName,
            Gateway = device.IpAddress,
            Path = device.Path,
            PlcType = PlcType.ControlLogix,
            Protocol = Protocol.ab_eip,
            Timeout = timeout
        };
#pragma warning restore CS0618

        await tag.ReadAsync(ct);

        var infos = tag.Value ?? Array.Empty<TagInfo>();
        return infos.Select(i => new PlcTagInfo
        {
            Id = i.Id,
            Type = i.Type,
            Name = i.Name ?? string.Empty,
            Length = i.Length,
            Dimensions = i.Dimensions ?? Array.Empty<uint>()
        }).ToList();
    }

    /// <summary>
    /// Reads the current value(s) of a specific tag (or all attributes of a grouped tag).
    /// For structure members the full path "Root.Attribute" is used.
    /// Returns a human-readable representation.
    /// </summary>
    public async Task<string> ReadTagValueAsync(PlcDevice device, string fullTagName, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        timeout ??= TimeSpan.FromSeconds(5);

        // We use the low-level Tag and try common types. For production you would cache type info from listing.
        using var tag = new Tag
        {
            Name = fullTagName,
            Gateway = device.IpAddress,
            Path = device.Path,
            PlcType = PlcType.ControlLogix,
            Protocol = Protocol.ab_eip,
            Timeout = timeout.Value
        };

        try
        {
            await tag.ReadAsync(ct);
        }
        catch (Exception ex)
        {
            return $"[Error reading {fullTagName}: {ex.Message}]";
        }

        // Try to interpret based on size
        var size = tag.GetSize();
        if (size <= 0) return "(empty)";

        // Common cases
        if (size == 1) return tag.GetInt8(0).ToString();
        if (size == 2) return tag.GetInt16(0).ToString();
        if (size == 4)
        {
            // Could be DINT or REAL – show both interpretations
            var asInt = tag.GetInt32(0);
            var asFloat = tag.GetFloat32(0);
            return $"{asInt}  (REAL≈ {asFloat:G6})";
        }
        if (size == 8)
        {
            var asLong = tag.GetInt64(0);
            var asDouble = tag.GetFloat64(0);
            return $"{asLong}  (LREAL≈ {asDouble:G6})";
        }

        // STRING-like (AB STRING is often 82 or 88 bytes)
        if (size >= 82 && size <= 100)
        {
            try
            {
                // Standard AB STRING: 2-byte LEN + 82 bytes data (or similar)
                var len = tag.GetInt16(0);
                if (len > 0 && len < size)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < len; i++)
                    {
                        var b = tag.GetUInt8(2 + i);
                        if (b == 0) break;
                        sb.Append((char)b);
                    }
                    return $"\"{sb}\"";
                }
            }
            catch { /* fall through */ }
        }

        // Fallback: hex dump of first few bytes
        var hex = new StringBuilder();
        for (int i = 0; i < Math.Min(size, 32); i++)
        {
            hex.Append($"{tag.GetUInt8(i):X2} ");
        }
        if (size > 32) hex.Append("...");
        return $"(raw {size} bytes) {hex}";
    }

    /// <summary>
    /// Reads all members of a grouped tag (root + all .attribute entries).
    /// </summary>
    public async Task<Dictionary<string, string>> ReadTagAttributesAsync(
        PlcDevice device,
        string rootName,
        IEnumerable<PlcTagInfo> members,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in members.OrderBy(m => m.Name))
        {
            ct.ThrowIfCancellationRequested();
            var value = await ReadTagValueAsync(device, member.Name, timeout, ct);
            results[member.Name] = value;
        }

        return results;
    }
}
