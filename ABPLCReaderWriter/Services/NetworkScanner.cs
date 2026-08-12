using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ABPLCReaderWriter.Models;
using Spectre.Console;

namespace ABPLCReaderWriter.Services;

public class NetworkScanner
{
    private const int EtherNetIpPort = 44818;
    private const int ConnectTimeoutMs = 400;

    /// <summary>
    /// Scans an IP range or CIDR for hosts with TCP port 44818 open (EtherNet/IP).
    /// Returns candidate PLC devices. Name discovery is best-effort later via tag listing.
    /// </summary>
    public async Task<List<PlcDevice>> ScanAsync(string rangeOrCidr, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var ips = ExpandRange(rangeOrCidr).ToList();
        progress?.Report($"Scanning {ips.Count} addresses for EtherNet/IP (port {EtherNetIpPort})...");

        var results = new ConcurrentBag<PlcDevice>();

        await Parallel.ForEachAsync(ips, new ParallelOptions
        {
            MaxDegreeOfParallelism = 64,
            CancellationToken = ct
        }, async (ip, token) =>
        {
            if (await IsPortOpenAsync(ip, EtherNetIpPort, ConnectTimeoutMs, token))
            {
                results.Add(new PlcDevice
                {
                    IpAddress = ip.ToString(),
                    Name = string.Empty, // filled later if possible
                    Path = "1,0"
                });
                progress?.Report($"Found candidate: {ip}");
            }
        });

        return results.OrderBy(d => d.IpAddress).ToList();
    }

    private static IEnumerable<IPAddress> ExpandRange(string input)
    {
        input = input.Trim();

        // CIDR e.g. 192.168.1.0/24
        if (input.Contains('/'))
        {
            var parts = input.Split('/');
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var network) && int.TryParse(parts[1], out var prefix))
            {
                foreach (var ip in ExpandCidr(network, prefix))
                    yield return ip;
                yield break;
            }
        }

        // Range e.g. 192.168.1.1-192.168.1.50 or 192.168.1.1-50
        if (input.Contains('-'))
        {
            var parts = input.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                if (IPAddress.TryParse(parts[0], out var start))
                {
                    if (IPAddress.TryParse(parts[1], out var end))
                    {
                        foreach (var ip in ExpandIpRange(start, end))
                            yield return ip;
                        yield break;
                    }
                    // Short form 192.168.1.1-50
                    var startBytes = start.GetAddressBytes();
                    if (byte.TryParse(parts[1], out var lastOctet) && startBytes.Length == 4)
                    {
                        var endBytes = (byte[])startBytes.Clone();
                        endBytes[3] = lastOctet;
                        foreach (var ip in ExpandIpRange(start, new IPAddress(endBytes)))
                            yield return ip;
                        yield break;
                    }
                }
            }
        }

        // Single IP
        if (IPAddress.TryParse(input, out var single))
        {
            yield return single;
            yield break;
        }

        throw new ArgumentException($"Unable to parse IP range or CIDR: '{input}'. Examples: 192.168.1.0/24  or  192.168.1.10-50");
    }

    private static IEnumerable<IPAddress> ExpandCidr(IPAddress network, int prefixLength)
    {
        var bytes = network.GetAddressBytes();
        if (bytes.Length != 4) throw new NotSupportedException("Only IPv4 supported.");

        uint ip = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
        uint start = ip & mask;
        uint end = start | ~mask;

        for (uint i = start; i <= end; i++)
        {
            yield return new IPAddress(new byte[]
            {
                (byte)(i >> 24),
                (byte)(i >> 16),
                (byte)(i >> 8),
                (byte)i
            });
        }
    }

    private static IEnumerable<IPAddress> ExpandIpRange(IPAddress start, IPAddress end)
    {
        var s = BitConverter.ToUInt32(start.GetAddressBytes().Reverse().ToArray(), 0);
        var e = BitConverter.ToUInt32(end.GetAddressBytes().Reverse().ToArray(), 0);
        if (e < s) (s, e) = (e, s);

        for (uint i = s; i <= e; i++)
        {
            var bytes = BitConverter.GetBytes(i).Reverse().ToArray();
            yield return new IPAddress(bytes);
        }
    }

    private static async Task<bool> IsPortOpenAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await client.ConnectAsync(ip, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
