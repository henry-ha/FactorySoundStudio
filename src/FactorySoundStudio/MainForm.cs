using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FactorySoundStudio.Core;

namespace FactorySoundStudio;

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(13, 18, 27);
    private static readonly Color PanelColor = Color.FromArgb(23, 29, 40);
    private static readonly Color InputColor = Color.FromArgb(31, 39, 53);
    private static readonly Color Muted = Color.FromArgb(155, 171, 190);
    private static readonly Color Accent = Color.FromArgb(234, 52, 70);
    private static readonly Color Green = Color.FromArgb(83, 223, 170);

    private readonly ComboBox adapterBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox vehicleBox = new();
    private readonly TextBox hostBox = new() { Text = "" };
    private readonly TextBox testerBox = new() { Text = "0E00" };
    private readonly TextBox targetBox = new() { Text = "" };
    private readonly Button refreshButton = new() { Text = "Refresh adapters" };
    private readonly Button discoverButton = new() { Text = "Discover over ENET" };
    private readonly Button connectButton = new() { Text = "Connect (read-only)" };
    private readonly Button disconnectButton = new() { Text = "Disconnect", Enabled = false };
    private readonly Button readButton = new() { Text = "Read identification DIDs", Enabled = false };
    private readonly Button cancelButton = new() { Text = "Stop operation", Enabled = false };
    private readonly Button exportButton = new() { Text = "Export redacted report" };
    private readonly Label statusLabel = new() { AutoSize = true, ForeColor = Muted, Text = "Not connected" };
    private readonly Label targetHelp = new() { AutoSize = false, Height = 62, ForeColor = Muted };
    private readonly DataGridView ecuGrid = new();
    private readonly RichTextBox logBox = new();
    private readonly Dictionary<ushort, string> results = new();
    private DoIpSession? session;
    private CancellationTokenSource? operation;
    private bool busy;

    public MainForm()
    {
        Text = "Factory Sound Studio | Audi e-tron GT | v0.1 READ-ONLY";
        BackColor = Background;
        ForeColor = Color.White;
        ClientSize = new Size(1180, 770);
        MinimumSize = new Size(1020, 690);
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        BuildInterface();
        RefreshAdapters();
        WriteLog("Factory Sound Studio v0.1 started. No coding, write-data, session-control, security unlock or flashing functions exist.");
        WriteLog("Discovery uses UDP 13400. Routing activation + read-only identification uses TCP 13400.");
    }

    private static Label Title(string text, float size, Color? color = null) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI", size, FontStyle.Bold), ForeColor = color ?? Color.White
    };

    private static Label Caption(string text, int height = 42) => new()
    {
        Text = text, AutoSize = false, Width = 360, Height = height, ForeColor = Muted
    };

    private static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = primary ? Accent : InputColor;
        button.ForeColor = Color.White;
        button.Height = 37;
        button.Width = primary ? 360 : 174;
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(4, 4, 4, 8);
    }

    private static void StyleInput(Control control, int width = 355)
    {
        control.BackColor = InputColor;
        control.ForeColor = Color.White;
        control.Width = width;
        control.Height = 33;
        control.Margin = new Padding(4, 3, 4, 9);
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Background, Padding = new Padding(22, 14, 22, 14), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 79));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
        var headerText = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        headerText.Controls.Add(Title("FACTORY SOUND STUDIO", 22));
        headerText.Controls.Add(new Label { Text = "AUDI E-TRON GT (2024)  •  DOIP / ENET IDENTIFICATION TOOL", AutoSize = true, ForeColor = Muted });
        header.Controls.Add(headerText, 0, 0);
        var safetyTag = Title("●  READ-ONLY V0.1", 11, Green);
        safetyTag.Dock = DockStyle.Right;
        safetyTag.TextAlign = ContentAlignment.MiddleRight;
        safetyTag.Margin = new Padding(0, 6, 0, 35);
        header.Controls.Add(safetyTag, 1, 0);
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = PanelColor, Padding = new Padding(17, 18, 12, 18),
            Margin = new Padding(0, 0, 13, 0)
        };
        body.Controls.Add(left, 0, 0);
        left.Controls.Add(Title("1  CONNECT YOUR CAR", 13));
        left.Controls.Add(Caption("Park securely. Connect the ENET cable to the car and Windows laptop; activate vehicle diagnostics.", 49));
        left.Controls.Add(Title("Local network adapter", 9));
        StyleInput(adapterBox);
        left.Controls.Add(adapterBox);
        StyleButton(refreshButton);
        left.Controls.Add(refreshButton);
        StyleButton(discoverButton, true);
        left.Controls.Add(discoverButton);
        left.Controls.Add(Title("Discovered DoIP entities", 9));
        vehicleBox.Width = 355;
        vehicleBox.Height = 104;
        vehicleBox.BackColor = InputColor;
        vehicleBox.ForeColor = Color.White;
        vehicleBox.BorderStyle = BorderStyle.FixedSingle;
        vehicleBox.Margin = new Padding(4, 4, 4, 12);
        left.Controls.Add(vehicleBox);
        left.Controls.Add(Caption("Select a response to fill the IP and DoIP entity address. You can also enter a known IP manually.", 51));
        left.Controls.Add(Title("Vehicle / DoIP entity IPv4", 9));
        StyleInput(hostBox);
        left.Controls.Add(hostBox);
        left.Controls.Add(Title("Tester logical address (hex)", 9));
        StyleInput(testerBox);
        left.Controls.Add(testerBox);
        var connectRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        StyleButton(connectButton);
        StyleButton(disconnectButton);
        connectRow.Controls.Add(connectButton);
        connectRow.Controls.Add(disconnectButton);
        left.Controls.Add(connectRow);
        left.Controls.Add(new Label { Text = "Note: a successful gateway connection does not prove J1167 is accessible.", ForeColor = Muted, Width = 360, Height = 50 });

        var right = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 8), Margin = new Padding(0) };
        body.Controls.Add(right, 1, 0);
        var idTab = new TabPage("ECU identification") { BackColor = PanelColor, ForeColor = Color.White, Padding = new Padding(22) };
        var logTab = new TabPage("Session log") { BackColor = PanelColor, ForeColor = Color.White, Padding = new Padding(16) };
        var guideTab = new TabPage("Connection guide") { BackColor = PanelColor, ForeColor = Color.White, Padding = new Padding(20) };
        right.TabPages.Add(idTab);
        right.TabPages.Add(logTab);
        right.TabPages.Add(guideTab);

        var idLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1 };
        idLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        idLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        idLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        idLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        idLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 77));
        idLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        idLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        idTab.Controls.Add(idLayout);
        idLayout.Controls.Add(Title("2  READ IDENTIFICATION", 14), 0, 0);
        idLayout.Controls.Add(Caption("J1167 is Audi's standard exterior sound controller. Its DoIP target address is NOT assumed. Read the gateway first, then use an address verified by an authorized diagnostic scan.", 53), 0, 1);
        idLayout.Controls.Add(Title("ECU target logical address (hex)", 9), 0, 2);
        StyleInput(targetBox, 240);
        targetBox.TextChanged += (_, _) => targetHelp.Text = "Destination: " + (targetBox.Text.Trim().Length == 0 ? "not selected" : "0x" + targetBox.Text.Trim()) + "   •   Never guess a sound-controller address.";
        idLayout.Controls.Add(targetBox, 0, 3);
        targetHelp.Text = "Destination: not selected  •  Never guess a sound-controller address.";
        idLayout.Controls.Add(targetHelp, 0, 4);

        ecuGrid.Dock = DockStyle.Fill;
        ecuGrid.BackgroundColor = PanelColor;
        ecuGrid.BorderStyle = BorderStyle.None;
        ecuGrid.GridColor = Color.FromArgb(55, 67, 82);
        ecuGrid.EnableHeadersVisualStyles = false;
        ecuGrid.ColumnHeadersDefaultCellStyle.BackColor = InputColor;
        ecuGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        ecuGrid.DefaultCellStyle.BackColor = PanelColor;
        ecuGrid.DefaultCellStyle.ForeColor = Color.White;
        ecuGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 65, 82);
        ecuGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        ecuGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(30, 38, 51);
        ecuGrid.RowHeadersVisible = false;
        ecuGrid.ReadOnly = true;
        ecuGrid.AllowUserToAddRows = false;
        ecuGrid.AllowUserToDeleteRows = false;
        ecuGrid.AllowUserToResizeRows = false;
        ecuGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        ecuGrid.ColumnHeadersHeight = 38;
        ecuGrid.RowTemplate.Height = 43;
        ecuGrid.Columns.Add("did", "DID");
        ecuGrid.Columns.Add("name", "Identification item");
        ecuGrid.Columns.Add("result", "Result");
        ecuGrid.Columns[0].FillWeight = 16;
        ecuGrid.Columns[1].FillWeight = 44;
        ecuGrid.Columns[2].FillWeight = 65;
        foreach (var did in SafeDid.Identifiers) ecuGrid.Rows.Add($"{did.Key:X4}", did.Value, "Not read");
        idLayout.Controls.Add(ecuGrid, 0, 5);
        StyleButton(readButton, true);
        idLayout.Controls.Add(readButton, 0, 6);

        logBox.Dock = DockStyle.Fill;
        logBox.BackColor = InputColor;
        logBox.ForeColor = Color.FromArgb(211, 229, 241);
        logBox.Font = new Font("Consolas", 10);
        logBox.ReadOnly = true;
        logBox.BorderStyle = BorderStyle.None;
        logBox.WordWrap = true;
        logTab.Controls.Add(logBox);

        var help = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = PanelColor, ForeColor = Color.White, Font = new Font("Segoe UI", 11),
            Text = "FIRST CONNECTION — 2024 AUDI E-TRON GT\n\n" +
                   "1. Park, secure the car and connect the ENET cable to the OBD port and laptop Ethernet port. Avoid public roads during testing.\n\n" +
                   "2. Enable the vehicle's normal diagnostic/ignition-ready state without driving. Keep the vehicle adequately charged.\n\n" +
                   "3. Choose the actual ENET Ethernet adapter (not your Wi-Fi). If Windows assigns 169.254.x.x with a 255.255.0.0 mask, that can be normal for link-local ENET.\n\n" +
                   "4. Click Discover. An ISO 13400 DoIP entity may answer over UDP 13400. If discovery is blocked, obtain the vehicle IP from an approved diagnostic tool and type it manually.\n\n" +
                   "5. Select the entity and Connect. Default routing activation (not ECU programming) is attempted. Authentication or TLS restrictions are respected.\n\n" +
                   "6. The discovered address belongs to the responding DoIP entity, usually the gateway. It is NOT automatically J1167. Obtain J1167's validated logical address from vehicle-specific documentation/scan before using it.\n\n" +
                   "7. Read identification DIDs. Unsupported and access-restricted results are expected on some modules. No attempts are made to unlock protected services.\n\n" +
                   "READ-ONLY SCOPE: UDP discovery (0x0001), TCP routing activation (0x0005), alive check response (0x0008), UDS ReadDataByIdentifier (0x22) with six allowlisted IDs. NO 0x2E / 0x31 / 0x27 / 0x34 / 0x36 / flash or gain modification.\n\n" +
                   "PRIVACY: VIN values are masked. The app writes no diagnostic data automatically. Export creates a redacted JSON file only when you explicitly choose to save it.\n\n" +
                   "If discovery or routing fails, stop here and share the non-sensitive error message for troubleshooting."
        };
        guideTab.Controls.Add(help);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        StyleButton(cancelButton);
        StyleButton(exportButton);
        footer.Controls.Add(statusLabel);
        footer.Controls.Add(cancelButton);
        footer.Controls.Add(exportButton);
        root.Controls.Add(footer, 0, 2);

        refreshButton.Click += (_, _) => RefreshAdapters();
        discoverButton.Click += async (_, _) => await DiscoverAsync();
        connectButton.Click += async (_, _) => await ConnectAsync();
        disconnectButton.Click += async (_, _) => await DisconnectAsync();
        readButton.Click += async (_, _) => await ReadIdentifiersAsync();
        cancelButton.Click += (_, _) => operation?.Cancel();
        exportButton.Click += (_, _) => ExportReport();
        vehicleBox.SelectedIndexChanged += (_, _) =>
        {
            if (vehicleBox.SelectedItem is VehicleNode node)
            {
                hostBox.Text = node.Ip;
                targetBox.Text = node.LogicalAddress.ToString("X4");
                WriteLog($"Selected DoIP entity {node.Ip}, logical 0x{node.LogicalAddress:X4}, VIN {node.MaskedVin} (not necessarily J1167).");
            }
        };
    }

    private void RefreshAdapters()
    {
        adapterBox.Items.Clear();
        foreach (var adapter in NetworkDiscovery.AvailableAdapters()) adapterBox.Items.Add(adapter);
        if (adapterBox.Items.Count > 0) adapterBox.SelectedIndex = 0;
        WriteLog($"Detected {adapterBox.Items.Count} active IPv4 adapter entries. Choose the cable-connected interface.");
    }

    private LocalAdapter SelectedAdapter() => adapterBox.SelectedItem as LocalAdapter
        ?? throw new InvalidOperationException("Select a local IPv4 adapter first.");

    private static ushort HexAddress(string text)
    {
        string clean = text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (!ushort.TryParse(clean, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number))
            throw new FormatException("Logical address must be 4 hex digits (for example 0E00).");
        return number;
    }

    private void SetBusy(bool active, string? message = null)
    {
        busy = active;
        refreshButton.Enabled = !active;
        discoverButton.Enabled = !active;
        connectButton.Enabled = !active && session == null;
        disconnectButton.Enabled = !active && session != null;
        readButton.Enabled = !active && session != null;
        cancelButton.Enabled = active;
        if (message != null) statusLabel.Text = message;
    }

    private async Task DiscoverAsync()
    {
        if (busy) return;
        operation = new CancellationTokenSource();
        try
        {
            var adapter = SelectedAdapter();
            SetBusy(true, "Discovering…");
            WriteLog($"Discovery from {adapter.Address} on {adapter.Name}; UDP 13400.");
            var list = await NetworkDiscovery.DiscoverAsync(adapter, TimeSpan.FromSeconds(4), operation.Token);
            vehicleBox.Items.Clear();
            foreach (var node in list) vehicleBox.Items.Add(node);
            if (list.Count > 0)
            {
                vehicleBox.SelectedIndex = 0;
                WriteLog($"Discovery finished: {list.Count} DoIP entity/entities.");
            }
            else WriteLog("No DoIP discovery response. Try a different Ethernet adapter, verify cable wiring and diagnostic mode, or enter a known vehicle IP.");
            statusLabel.Text = list.Count > 0 ? $"Found {list.Count} entity/entities" : "No discovery response";
        }
        catch (OperationCanceledException) { WriteLog("Discovery stopped."); }
        catch (Exception ex) { ShowError(ex); }
        finally { operation.Dispose(); operation = null; SetBusy(false); }
    }

    private async Task ConnectAsync()
    {
        if (busy || session != null) return;
        operation = new CancellationTokenSource();
        try
        {
            var adapter = SelectedAdapter();
            var tester = HexAddress(testerBox.Text);
            SetBusy(true, "Activating route…");
            WriteLog($"Connecting to {hostBox.Text.Trim()}:13400 via {adapter.Address}; tester 0x{tester:X4}.");
            session = await DoIpSession.OpenAsync(hostBox.Text.Trim(), adapter.Address, tester, operation.Token);
            WriteLog("Routing activation accepted. No diagnostic security bypass attempted.");
            statusLabel.Text = "● Connected — read only";
        }
        catch (OperationCanceledException) { WriteLog("Connection stopped."); }
        catch (Exception ex) { ShowError(ex); }
        finally { operation.Dispose(); operation = null; SetBusy(false); }
    }

    private async Task DisconnectAsync()
    {
        if (busy || session == null) return;
        await session.DisposeAsync();
        session = null;
        SetBusy(false, "Not connected");
        WriteLog("Disconnected.");
    }

    private async Task ReadIdentifiersAsync()
    {
        if (session == null || busy) return;
        operation = new CancellationTokenSource();
        try
        {
            ushort target = HexAddress(targetBox.Text);
            SetBusy(true, $"Reading ECU 0x{target:X4}…");
            WriteLog($"Reading allowlisted identification DIDs from target 0x{target:X4}. This target may be the gateway, not J1167.");
            results.Clear();
            foreach (var did in SafeDid.Identifiers)
            {
                operation.Token.ThrowIfCancellationRequested();
                string outcome;
                try { outcome = await session.ReadDidAsync(target, did.Key, operation.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { outcome = $"Error: {ex.Message}"; }
                results[did.Key] = outcome;
                foreach (DataGridViewRow row in ecuGrid.Rows)
                    if (row.Cells[0].Value?.ToString() == did.Key.ToString("X4")) row.Cells[2].Value = outcome;
                WriteLog($"0x{did.Key:X4} {did.Value}: {outcome}");
            }
            statusLabel.Text = $"Read finished — target 0x{target:X4}";
        }
        catch (OperationCanceledException) { WriteLog("Identification read cancelled."); }
        catch (Exception ex) { ShowError(ex); }
        finally { operation.Dispose(); operation = null; SetBusy(false); }
    }

    private void ExportReport()
    {
        // Never export unmasked VIN, and never export a raw diagnostic traffic capture.
        using var save = new SaveFileDialog
        {
            Filter = "JSON report (*.json)|*.json", FileName = "etron-gt-readonly-report.json",
            Title = "Export redacted identification report"
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var report = new
            {
                app = "Factory Sound Studio 0.1",
                readOnly = true,
                dateUtc = DateTime.UtcNow,
                vehicleIp = hostBox.Text.Trim(),
                targetLogicalAddress = targetBox.Text.Trim(),
                note = "DoIP entity address is not proof of J1167 identity. VIN masked; raw traffic not recorded.",
                identification = SafeDid.Identifiers.Select(pair => new
                {
                    did = $"0x{pair.Key:X4}", name = pair.Value,
                    value = pair.Key == 0xF190 ? "[REDACTED]" :
                        (pair.Key == 0xF18A ? "[SUPPLIER ID REDACTED]" : results.GetValueOrDefault(pair.Key, "Not read"))
                }).ToArray()
            };
            System.IO.File.WriteAllText(save.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            WriteLog("Redacted JSON report exported at user request (location not logged).");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex)
    {
        var message = ex is OperationCanceledException ? "Operation cancelled." : ex.Message;
        WriteLog("Error: " + message);
        statusLabel.Text = "Error — inspect Session log";
    }

    private void WriteLog(string message)
    {
        if (logBox.IsDisposed) return;
        if (logBox.InvokeRequired) { logBox.BeginInvoke(new Action(() => WriteLog(message))); return; }
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        logBox.SelectionStart = logBox.TextLength;
        logBox.ScrollToCaret();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        operation?.Cancel();
        if (session != null) session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnFormClosed(e);
    }
}
