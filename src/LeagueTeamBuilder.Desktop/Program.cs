using System.Runtime.InteropServices;
using Velopack;

namespace LeagueTeamBuilder.Desktop
{
    internal static class Program
    {
        public const string AppTitle = "League Team Builder";

        [STAThread]
        private static void Main()
        {
            // Must be the first thing: the installer starts the app with special arguments (install, uninstall, update) that this handles.
            VelopackApp.Build().Run();

            // One copy at a time: a second start just brings the first window to the front.
            using var mutex = new Mutex(true, @"Local\LeagueTeamBuilder.Desktop", out var first);
            if (!first)
            {
                var hwnd = FindWindow(null, AppTitle);
                if (hwnd != IntPtr.Zero) { ShowWindow(hwnd, 9 /* SW_RESTORE */); SetForegroundWindow(hwnd); }
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) => Fail(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Fail(e.ExceptionObject as Exception);

            try { Application.Run(new MainForm()); }
            catch (Exception ex) { Fail(ex); }
        }

        private static void Fail(Exception? ex)
        {
            try { File.AppendAllText(Paths.DesktopLog, $"{DateTime.Now:u} {ex}\r\n"); } catch { }
            MessageBox.Show(AppTitle + " hit an error and has to close.\r\n\r\n" + ex?.Message + "\r\n\r\nDetails: " + Paths.DesktopLog,
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    }

    // Where things live. Everything the user creates is under %LocalAppData%\LeagueTeamBuilderData, NOT %LocalAppData%\LeagueTeamBuilder:
    // the installer (Velopack) owns that second folder, replaces it on every update and deletes it on uninstall.
    internal static class Paths
    {
        public static readonly string Data = Directory.CreateDirectory(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeagueTeamBuilderData")).FullName;
        public static string Database => Path.Combine(Data, "leagueteambuilder.db");
        public static string ServerLog => Path.Combine(Data, "server.log");
        public static string DesktopLog => Path.Combine(Data, "desktop.log");
        public static string SetupMarker => Path.Combine(Data, "setup.complete");
        public static string WebViewData => Path.Combine(Data, "webview");
        // The web app is published into the same folder as this exe.
        public static string ServerExe => Path.Combine(AppContext.BaseDirectory, "LeagueTeamBuilder.exe");

        // The web app saves uploaded pictures (map images, avatars, rank badges) under wwwroot\uploads, which is inside the install
        // folder and is replaced on every update. So wwwroot\uploads is made a junction (a folder link, no admin needed) to
        // <Data>\uploads. Done on every start, because an update brings a fresh empty wwwroot\uploads.
        public static void LinkUploads()
        {
            var store = Directory.CreateDirectory(Path.Combine(Data, "uploads")).FullName;
            var link = Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads");
            var info = new DirectoryInfo(link);

            if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var current = info.LinkTarget?.TrimEnd('\\', '/');
                if (string.Equals(current, store.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
                Directory.Delete(link, false); // removes only the link, never what it points to
            }
            else if (info.Exists)
            {
                // A real folder (first start after an install or update): keep anything in it, then replace it with the link.
                foreach (var entry in Directory.GetFileSystemEntries(link))
                {
                    var dest = Path.Combine(store, Path.GetFileName(entry));
                    if (File.Exists(dest) || Directory.Exists(dest)) continue;
                    if (Directory.Exists(entry)) Directory.Move(entry, dest); else File.Move(entry, dest);
                }
                Directory.Delete(link, true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{store}\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new IOException("Could not link the uploads folder: " + err.Trim());
        }
    }
}
