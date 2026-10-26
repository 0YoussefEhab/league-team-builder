using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeagueTeamBuilder.Desktop
{
    internal sealed class MainForm : Form
    {
        private readonly ServerProcess _server = new();
        private readonly WebView2 _web = new() { Dock = DockStyle.Fill, Visible = false };
        private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12f), Text = "Starting..." };
        private readonly CancellationTokenSource _cts = new();
        private bool _started;

        public MainForm()
        {
            Text = Program.AppTitle;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1366, 820);
            MinimumSize = new Size(900, 600);
            BackColor = Color.FromArgb(14, 16, 19);
            _status.ForeColor = Color.FromArgb(200, 205, 212);
            Controls.Add(_web);
            Controls.Add(_status);
            Shown += async (_, _) => await StartAsync();
            FormClosing += (_, _) => { _cts.Cancel(); _server.Dispose(); };
        }

        private async Task StartAsync()
        {
            if (_started) return;
            _started = true;
            try
            {
                if (!WebViewAvailable())
                {
                    if (MessageBox.Show("This app needs the Microsoft WebView2 Runtime (it is part of Windows 10 and 11 and of Microsoft Edge, but yours is missing).\r\n\r\nOpen the download page?",
                            Program.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
                    Close();
                    return;
                }

                string? email = null, password = null;
                var firstRun = !File.Exists(Paths.SetupMarker);
                if (firstRun)
                {
                    using var setup = new SetupForm();
                    if (setup.ShowDialog(this) != DialogResult.OK) { Close(); return; }
                    email = setup.Email;
                    password = setup.Password;
                }

                _status.Text = "Starting League Team Builder...";
                Paths.LinkUploads();
                _server.Start(email, password);
                // The first start also creates and migrates the database, so give it more time.
                if (!await _server.WaitUntilReadyAsync(TimeSpan.FromSeconds(90), _cts.Token))
                {
                    if (_cts.IsCancellationRequested) return;
                    MessageBox.Show("The app could not start.\r\n\r\n" + ServerProcess.LogTail() + "\r\n\r\nFull log: " + Paths.ServerLog,
                        Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                    return;
                }
                if (firstRun) File.WriteAllText(Paths.SetupMarker, DateTime.UtcNow.ToString("o"));

                Directory.CreateDirectory(Paths.WebViewData);
                var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebViewData);
                await _web.EnsureCoreWebView2Async(env);
                var core = _web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = true; // Ctrl+P, Ctrl+F, F5 keep working

                // Anything that is not this app opens in the normal browser; the window stays on the app.
                core.NewWindowRequested += (_, e) => { e.Handled = true; OpenOutside(e.Uri); };
                core.NavigationStarting += (_, e) =>
                {
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)) return;
                    if (u.Scheme is "data" or "blob" or "about") return;
                    if (u.Host == _server.BaseUri.Host && u.Port == _server.BaseUri.Port) return;
                    e.Cancel = true;
                    OpenOutside(e.Uri);
                };

                // If the server stops while the window is open (crash), say so instead of leaving a dead page.
                _server.Exited += () => { if (_cts.IsCancellationRequested || !IsHandleCreated || IsDisposed) return; try { BeginInvoke(() =>
                {
                    if (IsDisposed || _cts.IsCancellationRequested) return;
                    MessageBox.Show("The app stopped unexpectedly.\r\n\r\n" + ServerProcess.LogTail() + "\r\n\r\nFull log: " + Paths.ServerLog,
                        Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                }); } catch (InvalidOperationException) { } };

                _web.Source = _server.BaseUri;
                _status.Visible = false;
                _web.Visible = true;
            }
            catch (Exception ex)
            {
                File.AppendAllText(Paths.DesktopLog, $"{DateTime.Now:u} {ex}\r\n");
                MessageBox.Show("The app could not start.\r\n\r\n" + ex.Message + "\r\n\r\nDetails: " + Paths.DesktopLog,
                    Program.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private static bool WebViewAvailable()
        {
            try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
            catch { return false; }
        }

        private static void OpenOutside(string uri)
        {
            // Only web links; never run a file or another scheme.
            if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
                try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }
    }
}
