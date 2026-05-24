using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

public class NetworkMonitor
{
    public async IAsyncEnumerable<(long downloadBytesPerSec, long uploadBytesPerSec)> MonitorTotalAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, int intervalMs = 1000)
    {
        var prev = NetworkInterface.GetAllNetworkInterfaces()
                   .Where(n => n.OperationalStatus == OperationalStatus.Up)
                   .ToDictionary(
                       n => n.Id,
                       n => (
                           received: n.GetIPv4Statistics().BytesReceived,
                           sent: n.GetIPv4Statistics().BytesSent));

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(intervalMs, ct);
            long downloadBytes = 0;
            long uploadBytes = 0;

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var stats = ni.GetIPv4Statistics();
                prev.TryGetValue(ni.Id, out var last);

                downloadBytes += Math.Max(0, stats.BytesReceived - last.received);
                uploadBytes += Math.Max(0, stats.BytesSent - last.sent);

                prev[ni.Id] = (stats.BytesReceived, stats.BytesSent);
            }

            yield return (
                downloadBytes * 1000L / Math.Max(1, intervalMs),
                uploadBytes * 1000L / Math.Max(1, intervalMs));
        }
    }
}
