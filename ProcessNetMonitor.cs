using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;

public class ProcessNetMonitor
{
    public async IAsyncEnumerable<(int pid, string name, long uploadBps, long downloadBps)[]> MonitorTopProcessesAsync(int topN, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var counters = new ConcurrentDictionary<int, (long uploadBytes, long downloadBytes)>();

        using var session = new TraceEventSession("NetworkTray_ETW_Session");
        session.StopOnDispose = true;

        session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

        // Handlers: try to read common size fields (PayloadLength, DataLength, PacketSize, Size)
        session.Source.Kernel.TcpIpSend += data =>
        {
            var sz = GetSizeFromData(data);
            if (sz > 0)
            {
                counters.AddOrUpdate(
                    (int)data.ProcessID,
                    _ => (sz, 0),
                    (_, current) => (current.uploadBytes + sz, current.downloadBytes));
            }
        };
        session.Source.Kernel.TcpIpRecv += data =>
        {
            var sz = GetSizeFromData(data);
            if (sz > 0)
            {
                counters.AddOrUpdate(
                    (int)data.ProcessID,
                    _ => (0, sz),
                    (_, current) => (current.uploadBytes, current.downloadBytes + sz));
            }
        };

        var processing = Task.Run(() => session.Source.Process(), ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);
                var snapshot = counters.ToArray();
                counters.Clear();

                var top = snapshot.OrderByDescending(kv => kv.Value)
                                  .Take(topN)
                                  .Select(kv =>
                                  {
                                      var pid = kv.Key;
                                      var name = GetProcessName(pid);
                                      return (pid, name, uploadBps: kv.Value.uploadBytes, downloadBps: kv.Value.downloadBytes);
                                  }).ToArray();

                yield return top;
            }
        }
        finally
        {
            session.Dispose();
            try { await processing; } catch { }
        }
    }

    private static long GetSizeFromData(object data)
    {
        if (data == null) return 0;
        var t = data.GetType();
        // preferred names in order
        string[] names = { "PayloadLength", "DataLength", "PacketSize", "Length", "Size" };
        foreach (var n in names)
        {
            var pi = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pi != null)
            {
                try
                {
                    var v = pi.GetValue(data);
                    if (v is int i) return Math.Max(0, i);
                    if (v is long l) return Math.Max(0, l);
                }
                catch { }
            }
        }
        // last resort: try EventDataLength property or BodyLength
        var alt = t.GetProperty("EventDataLength") ?? t.GetProperty("BodyLength");
        if (alt != null)
        {
            try
            {
                var v = alt.GetValue(data);
                if (v is int i) return Math.Max(0, i);
                if (v is long l) return Math.Max(0, l);
            }
            catch { }
        }
        return 0;
    }

    private static string GetProcessName(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch { return "unknown"; }
    }
}
