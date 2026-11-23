using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OmnaSignDesktop;

/// <summary>
/// Local Apple Account provisioning via the MIT-licensed iPASide Python engine.
/// No Apple credentials are uploaded to OmnaSign or passed on the command line.
/// The bundled engine handles its own Apple authentication, device provisioning,
/// cryptographic signing, and (optionally) USB installation.
/// </summary>
public sealed class AppleAccountPanel : UserControl
{
    private static readonly Color Background = Color.FromArgb(9, 15, 29);
    private static readonly Color Card = Color.FromArgb(17, 27, 47);
    private static readonly Color Accent = Color.FromArgb(76, 116, 238);
    private static readonly Color Muted = Color.FromArgb(169, 184, 210);
    private readonly FlowLayoutPanel layout;
    private readonly TextBox email = Field(), password = Field(true), otp = Field(), ipa = Field(), bundle = Field();
    private readonly TextBox progress = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
        BackColor = Card, ForeColor = Muted, Width = 720, Height = 200
    };
    private readonly Label status;
    private readonly List<Button> operations = new();
    private CancellationTokenSource? activeOperation;
    private string EngineExe => Path.Combine(AppContext.BaseDirectory, "engine", "python", "python.exe");

    public AppleAccountPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Background;
        layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            AutoScroll = true, WrapContents = false,
            Padding = new Padding(26, 20, 15, 50), BackColor = Background
        };
        Controls.Add(layout);
        AddText("Apple Account signing", 25, true);
        AddText("1. Sign in with your Apple Account · 2. Connect and trust your iPhone · 3. Choose an IPA · 4. Sign / install.", 10);
        AddText("Uses the MIT-licensed iPASide engine locally. Apple Account authentication uses Apple's developer services. No Apple password or signing certificate is sent to OmnaSign servers.", 10);
        AddText("Apple Account email", 11, true);
        layout.Controls.Add(email);
        AddText("Password (used only during this sign-in request; not saved by OmnaSign)", 11, true);
        layout.Controls.Add(password);
        var first = Row(
            Action("Check setup", async (_, _) => await Execute("doctor")),
            Action("Sign in", async (_, _) => await SignInAsync()),
            Action("Account status", async (_, _) => await Execute("login", "--status"))
        );
        layout.Controls.Add(first);
        AddText("Apple two-factor code (if prompted)", 11, true);
        otp.Width = 180;
        layout.Controls.Add(otp);
        layout.Controls.Add(Row(Action("Verify two-factor code", async (_, _) => await SignInAsync(true)),
                                Action("Detect connected iPhones", async (_, _) => await Execute("devices"))));
        AddText("App IPA (.ipa)", 11, true);
        layout.Controls.Add(Row(ipa, Action("Browse IPA", (_, _) => ChooseIPA())));
        AddText("Bundle identifier override (optional; e.g. com.myteam.myapp)", 11, true);
        layout.Controls.Add(bundle);
        layout.Controls.Add(Row(Action("Sign & install on iPhone", async (_, _) => await SignInstallAsync(), true),
                                Action("Sign & save IPA", async (_, _) => await SignSaveAsync())));
        AddText("A free Apple developer account generally has a 7-day provisioning limit and other app/device restrictions. Apple two-factor verification is required. This implementation cannot guarantee compatibility with every Apple Account or iOS version.", 10);
        status = AddText("Ready. The bundled signing engine will be checked when you click a command.", 10, true);
        layout.Controls.Add(progress);
        layout.Controls.Add(Row(Action("Cancel active operation", (_, _) => activeOperation?.Cancel()),
                               Action("Open engine documentation", (_, _) => OpenUrl("https://github.com/pwnapplehat/iPASide"))));
        layout.Resize += (_, _) => {
            var width = Math.Max(400, layout.ClientSize.Width - 95);
            progress.Width = width;
            foreach (Control c in layout.Controls)
                if (c is Label label) label.MaximumSize = new Size(width, 0);
        };
    }

    private Label AddText(string text, int size, bool bold = false)
    {
        var l = new Label {
            Text = text, AutoSize = true, ForeColor = bold ? Color.White : Muted,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            MaximumSize = new Size(730, 0), Margin = new Padding(2, 5, 0, 12)
        };
        layout.Controls.Add(l);
        return l;
    }
    private static TextBox Field(bool secret = false) => new() {
        Width = 595, BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(14, 24, 40), ForeColor = Color.White,
        UseSystemPasswordChar = secret, Font = new Font("Segoe UI", 11)
    };
    private Button Action(string text, EventHandler callback, bool primary = false)
    {
        var button = new Button {
            Text = text, AutoSize = true, MinimumSize = new Size(120, 43),
            BackColor = primary ? Accent : Color.FromArgb(31, 46, 73),
            ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Margin = new Padding(1, 5, 10, 5)
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(56, 75, 107);
        button.Click += callback;
        operations.Add(button);
        return button;
    }
    private static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 800 };
        row.Controls.AddRange(children);
        return row;
    }
    private void ChooseIPA()
    {
        using var dlg = new OpenFileDialog { Title = "Choose an iOS IPA", Filter = "iOS app (*.ipa)|*.ipa", CheckFileExists = true };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) ipa.Text = dlg.FileName;
    }
    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
    private static bool IsBundleID(string id) =>
        id.Split('.').Length >= 2 &&
        id.Length is > 2 and < 200 &&
        System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Za-z0-9.-]+$") &&
        !id.Contains("..");
    private void AppendLog(string message)
    {
        if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => {
            progress.AppendText(message.Trim() + Environment.NewLine);
            progress.SelectionStart = progress.TextLength;
            progress.ScrollToCaret();
        }));
    }

    private async Task<EngineResult> InvokeEngineAsync(params string[] args)
    {
        if (!File.Exists(EngineExe))
            throw new FileNotFoundException(
                "Apple Account engine not installed. Download the 'OmnaSign Full Windows Bundle' ZIP, extract the whole folder, and run OmnaSignDesktop.exe with its adjacent engine folder. The standalone EXE cannot provision Apple Accounts.",
                EngineExe);
        if (activeOperation != null) throw new InvalidOperationException("Another operation is already running.");
        activeOperation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        foreach (var button in operations) {
            if (!button.Text.StartsWith("Cancel")) button.Enabled = false;
        }
        var input = new ProcessStartInfo(EngineExe) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        input.ArgumentList.Add("-m"); input.ArgumentList.Add("ipaside_engine");
        foreach (var arg in args) input.ArgumentList.Add(arg);
        input.ArgumentList.Add("--json");
        // Apple ID password is never placed in argv, shell history or a file.
        // iPASide's auth code consumes this env only in the child process.
        if (args.Length > 0 && args[0] == "login" && !string.IsNullOrWhiteSpace(password.Text))
            input.Environment["IPASIDE_APPLE_PASSWORD"] = password.Text;
        progress.Text = "Running local Apple developer engine: " + args[0] + Environment.NewLine;
        try {
            using var proc = new Process { StartInfo = input };
            if (!proc.Start()) throw new Exception("Could not launch local engine.");
            var stdout = proc.StandardOutput.ReadToEndAsync(activeOperation.Token);
            var stderr = proc.StandardError.ReadToEndAsync(activeOperation.Token);
            await proc.WaitForExitAsync(activeOperation.Token);
            string result = await stdout;
            string errors = await stderr;
            string summary = ParseSafeResult(result);
            AppendLog(summary);
            if (proc.ExitCode != 0) {
                var safeError = ExtractSafeError(result, errors);
                throw new Exception("Apple developer engine returned error " + proc.ExitCode + ": " + safeError);
            }
            return new EngineResult(proc.ExitCode, result);
        }
        catch (OperationCanceledException) {
            throw new Exception("Operation cancelled or timed out. Connect and trust your device, then retry.");
        }
        finally {
            activeOperation.Dispose(); activeOperation = null;
            foreach (var button in operations) button.Enabled = true;
        }
    }
    private static string ParseSafeResult(string result)
    {
        try {
            using var json = JsonDocument.Parse(result);
            var data = json.RootElement;
            if (data.ValueKind == JsonValueKind.Object) {
                var keys = new[] { "status", "ok", "name", "bundle_id", "signed", "installed", "authenticated", "method", "reason", "error" };
                var sb = new StringBuilder();
                foreach (var key in keys) if (data.TryGetProperty(key, out var item)) {
                    if (item.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                        sb.AppendLine(key + ": " + item.ToString().Substring(0, Math.Min(400, item.ToString().Length)));
                }
                return sb.Length > 0 ? sb.ToString().Trim() : "Command succeeded. Device or account response received.";
            }
            if (data.ValueKind == JsonValueKind.Array)
                return "Connected devices found: " + data.GetArrayLength() + ". If multiple devices are attached, disconnect all but the intended iPhone before installing.";
        } catch { }
        return "Command finished; no safe summary available.";
    }
    private static string ExtractSafeError(string stdout, string stderr)
    {
        try {
            using var json = JsonDocument.Parse(stdout);
            if (json.RootElement.TryGetProperty("error", out var e)) return e.ToString().Substring(0, Math.Min(700, e.ToString().Length));
        } catch { }
        // Avoid printing provider session tokens, secret Apple responses or passwords.
        return stderr.Contains("No module named", StringComparison.OrdinalIgnoreCase) ?
            "Bundled engine is incomplete (missing Python module)." :
            "Review Apple Developer Mode, USB trust, your account, and network connectivity.";
    }
    private async Task Execute(params string[] args)
    {
        try { status.Text = "Running " + args[0] + "..."; await InvokeEngineAsync(args); status.Text = args[0] + " completed."; }
        catch (Exception e) { status.Text = e.Message; }
    }
    private async Task SignInAsync(bool verify = false)
    {
        if (string.IsNullOrWhiteSpace(email.Text) || string.IsNullOrWhiteSpace(password.Text)) {
            status.Text = "Enter Apple Account email and password to sign in."; return;
        }
        if (verify && !System.Text.RegularExpressions.Regex.IsMatch(otp.Text.Trim(), @"^\d{6}$")) {
            status.Text = "Enter the six-digit Apple verification code."; return;
        }
        try {
            var args = verify ? new[] { "login", "--email", email.Text.Trim(), "--code", otp.Text.Trim() } : new[] { "login", "--email", email.Text.Trim() };
            status.Text = verify ? "Verifying with Apple…" : "Authenticating with Apple…";
            var output = await InvokeEngineAsync(args);
            if (verify) {
                password.Clear(); otp.Clear();
                status.Text = "Verification completed. Check account status to confirm login.";
            } else {
                status.Text = output.Output.Contains("2fa_required", StringComparison.OrdinalIgnoreCase) ?
                    "Apple requested two-factor authentication; enter the code above." :
                    "Sign-in request completed. Check account status.";
            }
        } catch (Exception e) { status.Text = e.Message; }
    }
    private bool EnsureIPA()
    {
        if (File.Exists(ipa.Text) && string.Equals(Path.GetExtension(ipa.Text), ".ipa", StringComparison.OrdinalIgnoreCase)) return true;
        status.Text = "Choose a valid .ipa file first."; return false;
    }
    private async Task SignInstallAsync()
    {
        if (!EnsureIPA()) return;
        if (!string.IsNullOrWhiteSpace(bundle.Text) && !IsBundleID(bundle.Text.Trim())) { status.Text = "Invalid Bundle ID."; return; }
        if (MessageBox.Show(FindForm(), "Provision the app with your signed-in Apple Account and install it on your trusted iPhone? This may register an App ID and device with Apple.",
            "Confirm installation", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try {
            var arguments = new System.Collections.Generic.List<string> { "sideload", ipa.Text, "--keep-signed" };
            if (!string.IsNullOrWhiteSpace(bundle.Text)) { arguments.Add("--bundle-id"); arguments.Add(bundle.Text.Trim()); }
            status.Text = "Provisioning, signing and installing (may require several minutes)…";
            await InvokeEngineAsync(arguments.ToArray());
            status.Text = "Install engine completed. Check the app icon and trust settings on your iPhone.";
        } catch (Exception e) { status.Text = e.Message; }
    }
    private async Task SignSaveAsync()
    {
        if (!EnsureIPA()) return;
        if (!IsBundleID(bundle.Text.Trim())) { status.Text = "Enter an App Bundle ID (required for automatic provisioning)."; return; }
        using var save = new SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(ipa.Text) + "-signed.ipa", Filter = "Signed IPA (*.ipa)|*.ipa" };
        if (save.ShowDialog(FindForm()) != DialogResult.OK) return;
        try {
            status.Text = "Provisioning certificate and profile with Apple…";
            await InvokeEngineAsync("provision", "--bundle-id", bundle.Text.Trim());
            status.Text = "Signing locally…";
            await InvokeEngineAsync("sign", ipa.Text, "--output", save.FileName, "--bundle-id", bundle.Text.Trim());
            if (!File.Exists(save.FileName)) throw new Exception("Engine reported success but signed IPA was not created.");
            status.Text = "Signed IPA saved: " + save.FileName;
        } catch (Exception e) { status.Text = e.Message; }
    }
    private record EngineResult(int Code, string Output);
}
