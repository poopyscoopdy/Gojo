using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace OmnaSignDesktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new StudioWindow());
    }
}

public sealed class StudioWindow : Form
{
    private static readonly Color Bg = Color.FromArgb(9, 15, 29);
    private static readonly Color PanelBg = Color.FromArgb(17, 27, 47);
    private static readonly Color Accent = Color.FromArgb(85, 120, 245);
    private static readonly Color Muted = Color.FromArgb(169, 184, 210);
    private static readonly string StudioUrl = "https://omnasign-studio.vercel.app";
    private static readonly string IphoneIPA = "https://github.com/poopyscoopdy/Gojo/releases/download/omnasigner-v1.1-applemaps-preview/OmnaSigner-unsigned.ipa";
    private static readonly string Sideloadly = "https://sideloadly.io/";
    private readonly Panel surface = new() { Dock = DockStyle.Fill, BackColor = Bg, AutoScroll = true };
    private readonly Panel sidebar = new() { Dock = DockStyle.Left, Width = 210, BackColor = Color.FromArgb(12, 22, 40) };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 26, Text = "OmnaSign Desktop 5.2 · No subscription · Local signing", ForeColor = Muted, BackColor = PanelBg, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
    private readonly Label header = new() { Dock = DockStyle.Top, Height = 76, Text = "  OmnaSign Desktop     ·     Sign apps, build apps, and test locations", Font = new Font("Segoe UI", 15, FontStyle.Bold), ForeColor = Color.White, BackColor = PanelBg, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox ipa = new(), p12 = new(), profile = new(), zsign = new(), pass = new(), bundle = new();
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 115 };
    private Button? signButton;
    private WebView2? editorWebView;
    private WebView2? mapsWebView;
    private Process? activeLocationSession;
    private bool locationBusy;

    public StudioWindow()
    {
        Text = "OmnaSign Desktop 5.2";
        Size = new Size(1100, 790);
        MinimumSize = new Size(850, 610);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        Controls.Add(surface);
        Controls.Add(sidebar);
        Controls.Add(header);
        Controls.Add(status);
        AddNav("⌂   Home", ShowHome);
        AddNav("✦   Sign an IPA", ShowSign);
        AddNav("✎   Create / edit", ShowEditor);
        AddNav("◎   Apple Maps", ShowMaps);
        AddNav("↓   Downloads", ShowDownloads);
        AddNav("⚙   About / setup", ShowAbout);
        FormClosed += (_, _) => StopLocationSession();
        ShowHome();
    }

    private static Label Label(string text, int size = 11, bool bold = false)
        => new() { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = Color.White, AutoSize = true, MaximumSize = new Size(830, 0) };

    private static Button Button(string text, EventHandler handler, bool primary = false, int width = 235)
    {
        var b = new Button { Text = text, Width = width, Height = 44, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = primary ? Accent : Color.FromArgb(33, 47, 74), Cursor = Cursors.Hand, Margin = new Padding(0, 6, 10, 6) };
        b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(61, 78, 108);
        b.Click += handler;
        return b;
    }
    private static TextBox Field(bool secret = false)
    {
        var t = new TextBox { Width = 550, Height = 36, BackColor = Color.FromArgb(12, 20, 35), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        t.UseSystemPasswordChar = secret;
        return t;
    }
    private void AddNav(string title, Action action)
    {
        int index = sidebar.Controls.Count;
        var b = Button(title, (_, _) => action(), false, 184);
        b.Location = new Point(12, 28 + index * 58);
        b.TextAlign = ContentAlignment.MiddleLeft;
        sidebar.Controls.Add(b);
    }
    private void SetPage(Control page)
    {
        surface.Controls.Clear();
        page.Dock = DockStyle.Fill;
        surface.Controls.Add(page);
    }
    private FlowLayoutPanel Page(string heading, string description)
    {
        var f = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Bg, Padding = new Padding(34, 28, 24, 22) };
        f.Controls.Add(Label(heading, 24, true));
        var descr = Label(description, 10);
        descr.ForeColor = Muted;
        descr.Margin = new Padding(0, 7, 0, 24);
        f.Controls.Add(descr);
        f.Resize += (_, _) =>
        {
            var target = Math.Max(450, f.ClientSize.Width - 95);
            foreach (Control c in f.Controls)
                if (c is Label l) l.MaximumSize = new Size(target, 0);
        };
        return f;
    }
    private static FlowLayoutPanel Actions(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 790, Height = 70 };
        f.Controls.AddRange(controls);
        return f;
    }
    private void ShowHome()
    {
        var p = Page("Your iOS tools. Simpler.", "Choose what you want to do. Advanced configuration stays out of the way until you need it.");
        p.Controls.Add(Label("1    Sign an existing IPA", 17, true));
        p.Controls.Add(Label("Choose your IPA, Apple signing certificate and provisioning profile. Export a signed copy to install."));
        p.Controls.Add(Actions(Button("Sign an IPA", (_, _) => ShowSign(), true), Button("Apple Account signing (Sideloadly)", (_, _) => OpenLink(Sideloadly))));
        p.Controls.Add(Label("2    Create or edit an app", 17, true));
        p.Controls.Add(Label("Use the embedded web studio to edit supported app resources and preview web-based iOS app projects."));
        p.Controls.Add(Actions(Button("Open App Studio", (_, _) => ShowEditor(), true), Button("Open website in browser", (_, _) => OpenLink(StudioUrl))));
        p.Controls.Add(Label("3    Location lab", 17, true));
        p.Controls.Add(Label("Explore locations in Apple Maps and export an Xcode GPX test point. This does not change your iPhone’s system GPS or location shown by other apps."));
        p.Controls.Add(Actions(Button("Choose a test location", (_, _) => ShowMaps())));
        SetPage(p);
    }
    private void ChooseFile(TextBox box, string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true, Title = "Choose file" };
        if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.FileName;
    }
    private void AddFileSelector(FlowLayoutPanel parent, string title, TextBox box, string filter)
    {
        parent.Controls.Add(Label(title, 11, true));
        box.Width = 580;
        parent.Controls.Add(Actions(box, Button("Browse…", (_, _) => ChooseFile(box, filter), false, 130)));
    }
    private void ShowSign()
    {
        var p = Page("Sign or re-sign an IPA", "Local signing with your own .p12 certificate and provisioning profile. Nothing is uploaded to OmnaSign.");
        if (string.IsNullOrEmpty(zsign.Text))
        {
            var local = Path.Combine(AppContext.BaseDirectory, "zsign.exe");
            if (File.Exists(local)) zsign.Text = local;
        }
        AddFileSelector(p, "App to sign (.ipa)", ipa, "iOS App (*.ipa)|*.ipa");
        AddFileSelector(p, "Apple signing certificate (.p12)", p12, "Apple Certificate (*.p12;*.pfx)|*.p12;*.pfx");
        AddFileSelector(p, "Provisioning profile (.mobileprovision)", profile, "Apple Provisioning Profile (*.mobileprovision)|*.mobileprovision");
        AddFileSelector(p, "Signing engine (zsign.exe)", zsign, "Executable (*.exe)|*.exe");
        p.Controls.Add(Label("Certificate password"));
        pass.Width = 580; pass.UseSystemPasswordChar = true;
        p.Controls.Add(pass);
        p.Controls.Add(Label("Bundle ID override (optional)"));
        bundle.Width = 580;
        p.Controls.Add(bundle);
        signButton = Button("Sign IPA & save", async (_, _) => await SignAsync(), true, 250);
        p.Controls.Add(Actions(signButton, Button("Use Apple Account (external Sideloadly)", (_, _) => OpenLink(Sideloadly), false, 350)));
        p.Controls.Add(Label("Apple ID automatic provisioning is not yet built into OmnaSign. The external button opens Sideloadly; it does not send your Apple ID to OmnaSign."));
        p.Controls.Add(Label("Signing output"));
        log.Width = 785; log.BackColor = PanelBg; log.ForeColor = Muted;
        p.Controls.Add(log);
        SetPage(p);
    }
    private async Task SignAsync()
    {
        foreach (var path in new[] { ipa.Text, p12.Text, profile.Text, zsign.Text })
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { MessageBox.Show(this, "Please choose the IPA, certificate, provisioning profile and zsign.exe first.", "Missing file"); return; }
        using var save = new SaveFileDialog { Filter = "Signed IPA (*.ipa)|*.ipa", FileName = Path.GetFileNameWithoutExtension(ipa.Text) + "-signed.ipa" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        var cmd = new ProcessStartInfo(zsign.Text) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        cmd.ArgumentList.Add("-k"); cmd.ArgumentList.Add(p12.Text);
        cmd.ArgumentList.Add("-p"); cmd.ArgumentList.Add(pass.Text);
        cmd.ArgumentList.Add("-m"); cmd.ArgumentList.Add(profile.Text);
        cmd.ArgumentList.Add("-o"); cmd.ArgumentList.Add(save.FileName);
        if (!string.IsNullOrWhiteSpace(bundle.Text)) { cmd.ArgumentList.Add("-b"); cmd.ArgumentList.Add(bundle.Text.Trim()); }
        cmd.ArgumentList.Add(ipa.Text);
        signButton!.Enabled = false;
        status.Text = "  Signing locally…";
        log.Text = "Running zsign. No Apple ID or certificate files are sent to our server.";
        try
        {
            using var process = new Process { StartInfo = cmd };
            if (!process.Start()) throw new Exception("Signing engine failed to start.");
            Task<string> o = process.StandardOutput.ReadToEndAsync(), e = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var lines = (await o) + Environment.NewLine + (await e);
            log.Text = lines.Length > 8000 ? lines[^8000..] : lines;
            if (process.ExitCode != 0 || !File.Exists(save.FileName))
                throw new Exception("Signing failed. Confirm your certificate, password, provisioning, app ID, and entitlements. Details are shown in the output.");
            log.Text = "Signed IPA saved: " + save.FileName + Environment.NewLine + log.Text;
            status.Text = "  Signed IPA exported";
            MessageBox.Show(this, "Signed IPA saved. You can import it into a compatible iPhone installer.", "OmnaSign");
        }
        catch (Exception ex)
        {
            log.Text += Environment.NewLine + ex.Message;
            status.Text = "  Signing failed";
            MessageBox.Show(this, ex.Message, "OmnaSign signing error");
        }
        finally { signButton.Enabled = true; }
    }
    private void ShowEditor()
    {
        var p = new Panel { BackColor = Bg };
        var info = new Label { Dock = DockStyle.Top, Height = 55, Text = "  App Studio — browser-based editor embedded inside the desktop window. Native compiled code is not decompiled.", ForeColor = Color.White, BackColor = PanelBg, TextAlign = ContentAlignment.MiddleLeft };
        p.Controls.Add(info);
        var content = new Panel { Dock = DockStyle.Fill };
        p.Controls.Add(content);
        p.Controls.SetChildIndex(content, 1);
        SetPage(p);
        LaunchWebView(content, StudioUrl + "/#studio", "Can't open embedded studio. Install Microsoft Edge WebView2 Runtime.");
    }
    private void ShowMaps()
    {
        var p = new Panel { BackColor = Bg };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 228, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(15, 9, 10, 0), BackColor = PanelBg };
        var lat = Field(); var lon = Field();
        lat.Width = 112; lon.Width = 112; lat.Text = "40.7128"; lon.Text = "-74.0060";
        bar.Controls.Add(Label("Latitude")); bar.Controls.Add(lat);
        bar.Controls.Add(Label("Longitude")); bar.Controls.Add(lon);
        bar.Controls.Add(Button("Show in Apple Maps", (_, _) =>
        {
            if (TryCoord(lat.Text, lon.Text, out var la, out var lo))
            {
                if (mapsWebView?.CoreWebView2 != null) mapsWebView.CoreWebView2.Navigate("https://maps.apple.com/?ll=" + la + "," + lo);
                else OpenLink("https://maps.apple.com/?ll=" + la + "," + lo);
            }
            else MessageBox.Show(this, "Enter valid latitude (-90 to 90) and longitude (-180 to 180).");
        }, true, 202));
        bar.Controls.Add(Button("Export test GPX", (_, _) => SaveGpx(lat.Text, lon.Text), false, 178));
        bar.SetFlowBreak(bar.Controls[bar.Controls.Count - 1], true);
        bar.Controls.Add(Button("Simulate on USB iPhone (experimental)", async (_, _) => await SimulateLocationAsync(false, lat.Text, lon.Text), true, 295));
        bar.Controls.Add(Button("Clear simulated location", async (_, _) => await SimulateLocationAsync(true, lat.Text, lon.Text), false, 240));
        bar.Controls.Add(Button("Setup instructions", (_, _) => OpenLink("https://doronz88.github.io/pymobiledevice3/latest/cli/developer/"), false, 175));
        bar.SetFlowBreak(bar.Controls[bar.Controls.Count - 1], true);
        var note = Label("Experimental device simulation: requires a trusted USB iPhone, Developer Mode, compatible iOS developer services and the separately installed pymobiledevice3 tool. iOS 27 is not verified. It can affect location reports while active; it does NOT hide IP or cellular location. Use Clear afterward.");
        note.ForeColor = Muted; note.MaximumSize = new Size(870, 0);
        bar.Controls.Add(note);
        p.Controls.Add(bar);
        var content = new Panel { Dock = DockStyle.Fill };
        p.Controls.Add(content);
        SetPage(p);
        mapsWebView = LaunchWebView(content, "https://maps.apple.com/?ll=40.7128,-74.0060", "Apple Maps web view unavailable. Enter coordinates above or use the browser.");
    }

    private void StopLocationSession()
    {
        var process = activeLocationSession;
        activeLocationSession = null;
        if (process == null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { }
        try { process.Dispose(); }
        catch { }
    }

    private static ProcessStartInfo LocationProcessInfo(bool clear, string latitude = "", string longitude = "")
    {
        var start = new ProcessStartInfo("pymobiledevice3") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in new[] { "developer", "dvt", "simulate-location", clear ? "clear" : "set" })
            start.ArgumentList.Add(arg);
        if (!clear) {
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(latitude);
            start.ArgumentList.Add(longitude);
        }
        return start;
    }

    private async Task SimulateLocationAsync(bool clear, string latitude, string longitude)
    {
        if (locationBusy) { MessageBox.Show(this, "A location operation is already running."); return; }
        if (!clear && !TryCoord(latitude, longitude, out _, out _)) {
            MessageBox.Show(this, "Enter valid latitude and longitude.", "OmnaLocation"); return;
        }
        var warning = clear ?
            "Stop developer location simulation and request the phone's real GPS readings?" :
            "Experimental developer location simulation can affect iPhone Core Location readings while this PC session is running. It is NOT privacy protection, and iOS 27 compatibility is unverified. Use only on your own/test devices. Continue?";
        if (MessageBox.Show(this, warning, "OmnaLocation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        locationBusy = true;
        try {
            StopLocationSession();
            TryCoord(latitude, longitude, out var lat, out var lon);
            var info = LocationProcessInfo(clear, lat, lon);
            status.Text = clear ? "  Clearing simulated location…" : "  Establishing iPhone developer location session…";
            if (clear) {
                using var process = new Process { StartInfo = info };
                if (!process.Start()) throw new Exception("Could not start the developer tool.");
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errorOutput = process.StandardError.ReadToEndAsync();
                using var limit = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(45));
                try { await process.WaitForExitAsync(limit.Token); }
                catch (OperationCanceledException) {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    throw new Exception("Clearing location timed out. Try reconnecting the iPhone or using the developer CLI directly.");
                }
                string details = ((await output) + Environment.NewLine + (await errorOutput)).Trim();
                if (process.ExitCode != 0)
                    throw new Exception("The developer tool returned exit code " + process.ExitCode + ".\n" + (details.Length>1800 ? details[^1800..] : details));
                status.Text = "  Location clear command completed";
                MessageBox.Show(this, "The developer service reported that simulation was cleared. Verify your real location on the iPhone.", "OmnaLocation");
            } else {
                // IMPORTANT: iOS 17+ 'dvt simulate-location set' remains running until interrupted.
                // Retain the process while active rather than waiting for it to exit.
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                var logs = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (logs) { logs.AppendLine(e.Data); } };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (logs) { logs.AppendLine(e.Data); } };
                if (!process.Start()) throw new Exception("Could not start the developer tool.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Delay(2700);
                if (process.HasExited) {
                    string details;
                    lock (logs) details = logs.ToString();
                    int exit = process.ExitCode;
                    process.Dispose();
                    throw new Exception("Developer simulation exited early (code " + exit + "). Check your device connection and Developer Mode.\n" + (details.Length>1800 ? details[^1800..] : details));
                }
                activeLocationSession = process;
                process.Exited += (_, _) => {
                    if (ReferenceEquals(activeLocationSession, process) && !IsDisposed && IsHandleCreated) {
                        try { BeginInvoke((Action)(() => status.Text = "  Developer location session ended; verify your actual GPS.")); }
                        catch { }
                    }
                };
                status.Text = "  Developer location session active (verify on iPhone)";
                MessageBox.Show(this, "Location simulation session is running. Verify the iPhone's location in a compatible app. Keep OmnaSign open while testing, and press 'Clear simulated location' afterward. This does not hide cellular, Wi-Fi or IP location.", "OmnaLocation");
            }
        }
        catch (System.ComponentModel.Win32Exception) {
            status.Text = "  pymobiledevice3 not installed";
            MessageBox.Show(this, "The developer tool was not found.\n\nInstall Python and Apple's Windows device support, then run:\npy -m pip install -U pymobiledevice3\n\nConnect a trusted iPhone over USB and enable Developer Mode. Compatibility varies by iOS release.", "OmnaLocation setup");
        }
        catch (Exception e) {
            status.Text = "  Developer location command did not succeed";
            MessageBox.Show(this, e.Message, "OmnaLocation error");
        }
        finally { locationBusy = false; }
    }

    private static bool TryCoord(string a, string b, out string lat, out string lon)
    {
        lat = lon = "";
        if (!double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double la) ||
            !double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lo) ||
            !double.IsFinite(la) || !double.IsFinite(lo) || la < -90 || la > 90 || lo < -180 || lo > 180) return false;
        lat = la.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        lon = lo.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
    private void SaveGpx(string a, string b)
    {
        if (!TryCoord(a, b, out var la, out var lo)) { MessageBox.Show(this, "Invalid coordinates."); return; }
        using var save = new SaveFileDialog { Filter = "GPX test location (*.gpx)|*.gpx", FileName = "OmnaLocation-Test.gpx" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(save.FileName, "<?xml version=\"1.0\" encoding=\"UTF-8\"?><gpx version=\"1.1\" creator=\"OmnaSign\" xmlns=\"http://www.topografix.com/GPX/1/1\"><wpt lat=\"" + la + "\" lon=\"" + lo + "\"><name>OmnaLocation test</name></wpt></gpx>", Encoding.UTF8);
        status.Text = "  GPX test-location file exported";
    }
    private WebView2? LaunchWebView(Panel host, string url, string fallback)
    {
        var browser = new WebView2 { Dock = DockStyle.Fill };
        var err = new Label { Dock = DockStyle.Fill, Text = fallback + Environment.NewLine + "You can also open " + url, ForeColor = Muted, TextAlign = ContentAlignment.MiddleCenter };
        host.Controls.Add(err);
        host.Controls.Add(browser);
        _ = InitializeWebView(browser, err, url);
        return browser;
    }
    private async Task InitializeWebView(WebView2 browser, Label err, string url)
    {
        try { await browser.EnsureCoreWebView2Async(); browser.Source = new Uri(url); err.Visible = false; }
        catch (Exception ex) { browser.Visible = false; err.Text += Environment.NewLine + ex.Message; }
    }
    private void ShowDownloads()
    {
        var p = Page("Downloads", "No freemium. Always download from verified official links.");
        p.Controls.Add(Label("OmnaSigner for iPhone", 17, true));
        p.Controls.Add(Label("A real compiled unsigned IPA. Sign with SideStore, Sideloadly, or another compatible sideloader before installation."));
        p.Controls.Add(Actions(Button("Download iPhone IPA", (_, _) => OpenLink(IphoneIPA), true)));
        p.Controls.Add(Label("OmnaSign Web", 17, true));
        p.Controls.Add(Actions(Button("Open OmnaSign website", (_, _) => OpenLink(StudioUrl), true)));
        p.Controls.Add(Label("Windows signing engine", 17, true));
        p.Controls.Add(Label("Local signing requires zsign.exe beside this EXE, or select its path on the Sign screen. This app itself does not provision Apple Account certificates."));
        p.Controls.Add(Actions(Button("Get zsign source", (_, _) => OpenLink("https://github.com/zhlynn/zsign"))));
        SetPage(p);
    }
    private void ShowAbout()
    {
        var p = Page("Setup & privacy", "OmnaSign Desktop 5.2 — native Windows UI with embedded Microsoft WebView2.");
        p.Controls.Add(Label("Signing uses your local certificate and profile. Apple credentials are never collected here."));
        p.Controls.Add(Label("To sign with only an Apple Account, use the external Sideloadly route until OmnaSign implements verified provisioning. A free Apple account typically has seven-day expiration."));
        p.Controls.Add(Label("OmnaAi API keys should never be embedded in the public installer. A hosted backend with server-held credentials, authentication, and usage limits is needed for bundled AI access."));
        p.Controls.Add(Label("Location tests are not systemwide iPhone location spoofing."));
        p.Controls.Add(Actions(Button("Open OmnaSign website", (_, _) => OpenLink(StudioUrl), true)));
        SetPage(p);
    }
    private void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, "Couldn't open link: " + ex.Message); }
    }
}
