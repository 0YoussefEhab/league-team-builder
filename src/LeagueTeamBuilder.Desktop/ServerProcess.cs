using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LeagueTeamBuilder.Desktop
{
    // Runs the unchanged web app (LeagueTeamBuilder.exe) hidden, on 127.0.0.1 only, and stops it with the window.
    internal sealed class ServerProcess : IDisposable
    {
        private const int PreferredPort = 47821; // stable on purpose: the Clash board keeps state in localStorage, which is per address+port
        private Process? _proc;
        private IntPtr _job;

        public int Port { get; private set; }
        public Uri BaseUri => new($"http://127.0.0.1:{Port}/");
        public event Action? Exited;
        public bool HasExited => _proc is { HasExited: true };

        public void Start(string? adminEmail, string? adminPassword)
        {
            if (!File.Exists(Paths.ServerExe)) throw new FileNotFoundException("The server part of the app is missing: " + Paths.ServerExe);
            Port = PortFree(PreferredPort) ? PreferredPort : FreePort();

            var psi = new ProcessStartInfo(Paths.ServerExe)
            {
                WorkingDirectory = AppContext.BaseDirectory, // the web app uses the current folder as its content root (views, wwwroot)
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var env = psi.Environment;
            env["ASPNETCORE_URLS"] = $"http://127.0.0.1:{Port}";
            env["ASPNETCORE_ENVIRONMENT"] = "Production";
            env["Database__Path"] = Paths.Database;
            env["Demo__Seed"] = "false";
            // Lighter on a normal PC: no background GC thread, no profile-guided tiering work.
            env["DOTNET_gcConcurrent"] = "0";
            env["DOTNET_TieredPGO"] = "0";
            // Only on the very first start. The seeder acts only when the site has no active Admin, and never changes a password.
            if (!string.IsNullOrEmpty(adminEmail) && !string.IsNullOrEmpty(adminPassword))
            {
                env["Seed__AdminEmail"] = adminEmail;
                env["Seed__AdminPassword"] = adminPassword;
            }

            var log = new StreamWriter(new FileStream(Paths.ServerLog, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
            _proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
            _proc.Exited += (_, _) => Exited?.Invoke();
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
            KillWithMe(_proc);
        }

        // Waits until /healthz answers. Returns false if the server died or took too long.
        public async Task<bool> WaitUntilReadyAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                if (HasExited) return false;
                try { if ((await http.GetAsync(new Uri(BaseUri, "healthz"), ct)).IsSuccessStatusCode) return true; }
                catch { /* not up yet */ }
                await Task.Delay(250, ct).ContinueWith(_ => { });
            }
            return false;
        }

        public static string LogTail(int lines = 12)
        {
            try
            {
                using var fs = new FileStream(Paths.ServerLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                return string.Join(Environment.NewLine, sr.ReadToEnd().Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).TakeLast(lines));
            }
            catch { return "(no log)"; }
        }

        public void Dispose()
        {
            try { if (_proc is { HasExited: false }) { _proc.Kill(entireProcessTree: true); _proc.WaitForExit(3000); } } catch { }
            _proc?.Dispose();
            if (_job != IntPtr.Zero) { CloseHandle(_job); _job = IntPtr.Zero; }
        }

        private static bool PortFree(int port)
        {
            try { var l = new TcpListener(IPAddress.Loopback, port); l.Start(); l.Stop(); return true; } catch { return false; }
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        // A Windows job object with "kill on close": if this window process is killed or crashes, Windows ends the server too,
        // so no hidden server is ever left running.
        private void KillWithMe(Process p)
        {
            _job = CreateJobObject(IntPtr.Zero, null);
            if (_job == IntPtr.Zero) return;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (SetInformationJobObject(_job, 9 /* ExtendedLimitInformation */, ptr, (uint)size))
                    AssignProcessToJobObject(_job, p.Handle);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attrs, string? name);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int cls, IntPtr info, uint len);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    }
}
