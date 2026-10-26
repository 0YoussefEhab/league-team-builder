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

    // Where things live. Everything the user creates is under %LocalAppData%\LeagueTeamBuilder, never in the install folder,
    // so an update (which replaces the install folder) cannot touch the database.
    internal static class Paths
    {
        public static readonly string Data = Directory.CreateDirectory(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeagueTeamBuilder")).FullName;
        public static string Database => Path.Combine(Data, "leagueteambuilder.db");
        public static string ServerLog => Path.Combine(Data, "server.log");
        public static string DesktopLog => Path.Combine(Data, "desktop.log");
        public static string SetupMarker => Path.Combine(Data, "setup.complete");
        public static string WebViewData => Path.Combine(Data, "webview");
        // The web app is published into the same folder as this exe.
        public static string ServerExe => Path.Combine(AppContext.BaseDirectory, "LeagueTeamBuilder.exe");
    }
}
