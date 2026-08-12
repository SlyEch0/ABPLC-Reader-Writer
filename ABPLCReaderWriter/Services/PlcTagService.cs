using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ABPLCReaderWriter.Models;
using libplctag;
using libplctag.DataTypes;

namespace ABPLCReaderWriter.Services
{
    public class PlcTagService
    {
        public async Task<Dictionary<string, List<PlcTagInfo>>> ListTagsGroupedAsync(
            PlcDevice device,
            bool includeProgramTags = true,
            TimeSpan? timeout = null,
            CancellationToken ct = default(CancellationToken))
        {
            if (timeout == null)
                timeout = TimeSpan.FromSeconds(8);

            var allTags = new List<PlcTagInfo>();

            var controllerTags = await ListTagsAtScopeAsync(device, "@tags", timeout.Value, ct).ConfigureAwait(false);
            allTags.AddRange(controllerTags);

            if (includeProgramTags)
            {
                var programs = controllerTags
                    .Where(t => t.Name.StartsWith("Program:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Name)
                    .Distinct()
                    .ToList();

                foreach (var prog in programs)
                {
                    try
                    {
                        var progTags = await ListTagsAtScopeAsync(device, prog + ".@tags", timeout.Value, ct).ConfigureAwait(false);
                        allTags.AddRange(progTags);
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            var grouped = allTags
                .GroupBy(t => t.RootName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Name).ToList(), StringComparer.OrdinalIgnoreCase);

            return grouped;
        }

        private async Task<List<PlcTagInfo>> ListTagsAtScopeAsync(PlcDevice device, string tagName, TimeSpan timeout, CancellationToken ct)
        {
#pragma warning disable CS0618
            using (var tag = new Tag<TagInfoPlcMapper, TagInfo[]>
            {
                Name = tagName,
                Gateway = device.IpAddress,
                Path = device.Path,
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Timeout = timeout
            })
            {
#pragma warning restore CS0618
                await tag.ReadAsync(ct).ConfigureAwait(false);

                var infos = tag.Value ?? new TagInfo[0];
                return infos.Select(i => new PlcTagInfo
                {
                    Id = i.Id,
                    Type = i.Type,
                    Name = i.Name ?? string.Empty,
                    Length = i.Length,
                    Dimensions = i.Dimensions ?? new uint[0]
                }).ToList();
            }
        }

        public async Task<string> ReadTagValueAsync(PlcDevice device, string fullTagName, TimeSpan? timeout = null, CancellationToken ct = default(CancellationToken))
        {
            if (timeout == null)
                timeout = TimeSpan.FromSeconds(5);

            using (var tag = new Tag
            {
                Name = fullTagName,
                Gateway = device.IpAddress,
                Path = device.Path,
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Timeout = timeout.Value
            })
            {
                try
                {
                    await tag.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return string.Format("[Error reading {0}: {1}]", fullTagName, ex.Message);
                }

                var size = tag.GetSize();
                if (size <= 0) return "(empty)";

                if (size == 1) return tag.GetInt8(0).ToString();
                if (size == 2) return tag.GetInt16(0).ToString();
                if (size == 4)
                {
                    var asInt = tag.GetInt32(0);
                    var asFloat = tag.GetFloat32(0);
                    return string.Format("{0}  (REAL≈ {1:G6})", asInt, asFloat);
                }
                if (size == 8)
                {
                    var asLong = tag.GetInt64(0);
                    var asDouble = tag.GetFloat64(0);
                    return string.Format("{0}  (LREAL≈ {1:G6})", asLong, asDouble);
                }

                if (size >= 82 && size <= 100)
                {
                    try
                    {
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
                            return "\"" + sb.ToString() + "\"";
                        }
                    }
                    catch { }
                }

                var hex = new StringBuilder();
                for (int i = 0; i < Math.Min(size, 32); i++)
                {
                    hex.AppendFormat("{0:X2} ", tag.GetUInt8(i));
                }
                if (size > 32) hex.Append("...");
                return string.Format("(raw {0} bytes) {1}", size, hex.ToString());
            }
        }

        public async Task<Dictionary<string, string>> ReadTagAttributesAsync(
            PlcDevice device,
            string rootName,
            IEnumerable<PlcTagInfo> members,
            TimeSpan? timeout = null,
            CancellationToken ct = default(CancellationToken))
        {
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in members.OrderBy(m => m.Name))
            {
                ct.ThrowIfCancellationRequested();
                var value = await ReadTagValueAsync(device, member.Name, timeout, ct).ConfigureAwait(false);
                results[member.Name] = value;
            }

            return results;
        }
    }
}
