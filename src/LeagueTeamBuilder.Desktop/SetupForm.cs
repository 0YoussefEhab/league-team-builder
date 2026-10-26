namespace LeagueTeamBuilder.Desktop
{
    // First start only: asks for the first Admin account (the web edition takes these from user-secrets / environment variables).
    internal sealed class SetupForm : Form
    {
        private readonly TextBox _email = new() { Dock = DockStyle.Top };
        private readonly TextBox _pw = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
        private readonly TextBox _pw2 = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
        private readonly Label _error = new() { Dock = DockStyle.Top, ForeColor = Color.Firebrick, AutoSize = true, MaximumSize = new Size(360, 0) };

        public string Email => _email.Text.Trim();
        public string Password => _pw.Text;

        public SetupForm()
        {
            Text = Program.AppTitle + " - first start";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 330);
            Padding = new Padding(20);
            Font = new Font("Segoe UI", 9.5f);

            var ok = new Button { Text = "Create account and start", Dock = DockStyle.Top, Height = 36 };
            ok.Click += (_, _) => Submit();
            AcceptButton = ok;

            // Docked controls stack bottom-up, so they are added in reverse.
            Controls.Add(ok);
            Controls.Add(_error);
            Controls.Add(Gap());
            Controls.Add(_pw2);
            Controls.Add(Lbl("Repeat the password"));
            Controls.Add(_pw);
            Controls.Add(Lbl("Password (at least 8 characters)"));
            Controls.Add(_email);
            Controls.Add(Lbl("E-mail (this is your sign-in name)"));
            Controls.Add(new Label { Text = "Create the first Admin account. It stays on this computer.", Dock = DockStyle.Top, Height = 40 });
        }

        private static Label Lbl(string t) => new() { Text = t, Dock = DockStyle.Top, Height = 24, Padding = new Padding(0, 8, 0, 0) };
        private static Control Gap() => new Panel { Dock = DockStyle.Top, Height = 8 };

        private void Submit()
        {
            if (!Email.Contains('@') || Email.StartsWith('@') || Email.EndsWith('@')) { _error.Text = "Enter a valid e-mail."; return; }
            if (_pw.Text.Length < 8) { _error.Text = "The password needs at least 8 characters."; return; }
            if (_pw.Text != _pw2.Text) { _error.Text = "The two passwords are not the same."; return; }
            DialogResult = DialogResult.OK;
        }
    }
}
