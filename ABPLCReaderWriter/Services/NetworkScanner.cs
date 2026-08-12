using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ABPLCReaderWriter.Models;

namespace ABPLCReaderWriter.Services
{
    public class NetworkScanner
    {
        private const int EtherNetIpPort = 44818;
        private const int ConnectTimeoutMs = 400;

        public async Task<List<PlcDevice>> ScanAsync(string rangeOrCidr, IProgress<string> progress = null, CancellationToken ct = default(CancellationToken))
        {
            var ips = ExpandRange(rangeOrCidr).ToList();
            if (progress != null)
                progress.Report(string.Format("Scanning {0} addresses for EtherNet/IP (port {1})...", ips.Count, EtherNetIpPort));

            var results = new ConcurrentBag<PlcDevice>();

            var tasks = new List<Task>();
            var semaphore = new SemaphoreSlim(64);

            foreach (var ip in ips)
            {
                ct.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(ct).ConfigureAwait(false);

                var localIp = ip;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (await IsPortOpenAsync(localIp, EtherNetIpPort, ConnectTimeoutMs, ct).ConfigureAwait(false))
                        {
                            results.Add(new PlcDevice
                            {
                                IpAddress = localIp.ToString(),
                                Name = string.Empty,
                                Path = "1,0"
                            });
                            if (progress != null)
                                progress.Report("Found candidate: " + localIp.ToString());
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return results.OrderBy(d => d.IpAddress).ToList();
        }

        private static IEnumerable<IPAddress> ExpandRange(string input)
        {
            input = input.Trim();

            if (input.Contains("/"))
            {
                var parts = input.Split('/');
                IPAddress network;
                int prefix;
                if (parts.Length == 2 && IPAddress.TryParse(parts[0], out network) && int.TryParse(parts[1], out prefix))
                {
                    foreach (var ip in ExpandCidr(network, prefix))
                        yield return ip;
                    yield break;
                }
            }

            if (input.Contains("-"))
            {
                var parts = input.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    IPAddress start;
                    if (IPAddress.TryParse(parts[0].Trim(), out start))
                    {
                        IPAddress end;
                        if (IPAddress.TryParse(parts[1].Trim(), out end))
                        {
                            foreach (var ip in ExpandIpRange(start, end))
                                yield return ip;
                            yield break;
                        }

                        var startBytes = start.GetAddressBytes();
                        byte lastOctet;
                        if (byte.TryParse(parts[1].Trim(), out lastOctet) && startBytes.Length == 4)
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

            IPAddress single;
            if (IPAddress.TryParse(input, out single))
            {
                yield return single;
                yield break;
            }

            throw new ArgumentException(string.Format("Unable to parse IP range or CIDR: '{0}'. Examples: 192.168.1.0/24  or  192.168.1.10-50", input));
        }

        private static IEnumerable<IPAddress> ExpandCidr(IPAddress network, int prefixLength)
        {
            var bytes = network.GetAddressBytes();
            if (bytes.Length != 4)
                throw new NotSupportedException("Only IPv4 supported.");

            uint ip = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            uint mask = prefixLength == 0 ? 0u : 0xFFFFFFFFu << (32 - prefixLength);
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
            var startBytes = start.GetAddressBytes().Reverse().ToArray();
            var endBytes = end.GetAddressBytes().Reverse().ToArray();
            uint s = BitConverter.ToUInt32(startBytes, 0);
            uint e = BitConverter.ToUInt32(endBytes, 0);
            if (e < s)
            {
                var tmp = s;
                s = e;
                e = tmp;
            }

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
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(ip, port);
                    var timeoutTask = Task.Delay(timeoutMs, ct);

                    var completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                    if (completed == timeoutTask || !client.Connected)
                        return false;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
