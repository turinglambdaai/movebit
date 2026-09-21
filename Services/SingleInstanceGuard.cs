using System;
using System.Net;
using System.Net.Sockets;

namespace MoveBit.Services;

/// <summary>
/// Cross-process single-instance guard. The named mutex is the source of truth;
/// the loopback TCP channel is best-effort: a second launch pokes the running
/// instance so it surfaces its window, then exits quietly instead of stacking a
/// second tray icon / scheduler / history writer on top of the first.
/// </summary>
internal static class SingleInstanceGuard
{
    public const string MutexName = "MoveBit.SingleInstance";

    public const int ActivationPort = 47391;

    /// <summary>Best-effort "show yourself" poke at the already-running instance.</summary>
    public static void TryActivateRunningInstance()
    {
        try
        {
            using var client = new TcpClient();
            var async = client.BeginConnect(IPAddress.Loopback, ActivationPort, null, null);
            if (!async.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(800)))
            {
                return;
            }

            client.EndConnect(async);
            using var writer = new System.IO.StreamWriter(client.GetStream());
            writer.Write("activate\n");
            writer.Flush();
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // Nothing listening (older version, or activation server failed to bind):
            // the mutex already did its job, just exit.
        }
    }
}
