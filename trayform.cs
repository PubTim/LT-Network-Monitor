using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public class TrayForm : Form
{
    private const int ResizeGrip = 12;

    private readonly NotifyIcon _tray;
    private readonly NetworkMonitor _netMon = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Label _downloadValue = new();
    private readonly Label _uploadValue = new();
    private readonly Label _bootDownloadValue = new();
    private readonly Label _bootUploadValue = new();
    private readonly Label _uptimeValue = new();
    private readonly Label _statusValue = new();
    private readonly Label _networkNameValue = new();
    private readonly Label _titleLabel = new();
    private readonly Panel _dragBar = new();
    private readonly Panel _resizeFrame = new();
    private readonly Panel _historyPanel = new();
    private readonly Label _historyLabel = new();
    private readonly Button _themeButton = new();
    private readonly TableLayoutPanel _liveTotals = new();
    private readonly TableLayoutPanel _bootTotals = new();
    private readonly ListBox _networkHistory = new();
    private readonly System.Windows.Forms.Timer _summaryTimer = new();
    private readonly Dictionary<string, NetworkUsageState> _networkUsage = new(StringComparer.OrdinalIgnoreCase);

    private string _currentNetworkKey = "Unknown";
    private string _currentNetworkName = "Detecting...";
    private long _lastReceivedBytes = -1;
    private long _lastSentBytes = -1;
    private bool _monitoringStarted;
    private bool _isDarkMode = true;

    public TrayForm()
    {
        Text = "Network Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(720, 580);
        Padding = new Padding(10);
        BackColor = Color.FromArgb(12, 12, 16);
        Opacity = 0.94;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);

        BuildUi();

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Network Monitor"
        };

        var menu = new ContextMenuStrip();
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, __) => ExitApp();
        menu.Items.Add(exitItem);
        _tray.ContextMenuStrip = menu;

        Load += (_, __) => StartMonitors();
        FormClosing += (_, __) => CleanupTray();
    }

    private void BuildUi()
    {
        _resizeFrame.Dock = DockStyle.Fill;
        _resizeFrame.Padding = new Padding(ResizeGrip);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(0)
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _dragBar.Dock = DockStyle.Top;
        _dragBar.Height = 60;
        _dragBar.Padding = new Padding(12, 8, 12, 8);

        _titleLabel.AutoSize = true;
        _titleLabel.Text = "Current Network Usage";
        _titleLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
        _titleLabel.Location = new Point(12, 6);

        _themeButton.Dock = DockStyle.Right;
        _themeButton.Width = 34;
        _themeButton.FlatStyle = FlatStyle.Flat;
        _themeButton.Cursor = Cursors.Hand;
        _themeButton.FlatAppearance.BorderSize = 0;
        _themeButton.Click += (_, __) => ToggleTheme();

        _dragBar.Controls.Add(_themeButton);
        _dragBar.Controls.Add(_titleLabel);

        _liveTotals.Dock = DockStyle.Top;
        _liveTotals.AutoSize = true;
        _liveTotals.ColumnCount = 2;
        _liveTotals.RowCount = 1;
        _liveTotals.Margin = new Padding(0, 0, 0, 12);
        _liveTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _liveTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _liveTotals.Controls.Add(CreateMetricCard("Download", _downloadValue, Color.FromArgb(59, 130, 246)), 0, 0);
        _liveTotals.Controls.Add(CreateMetricCard("Upload", _uploadValue, Color.FromArgb(34, 197, 94)), 1, 0);

        _bootTotals.Dock = DockStyle.Top;
        _bootTotals.AutoSize = true;
        _bootTotals.ColumnCount = 3;
        _bootTotals.RowCount = 1;
        _bootTotals.Margin = new Padding(0, 0, 0, 12);
        _bootTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        _bootTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
        _bootTotals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
        _bootTotals.Controls.Add(CreateMetricCard("Total Downloaded", _bootDownloadValue, Color.FromArgb(168, 85, 247)), 0, 0);
        _bootTotals.Controls.Add(CreateMetricCard("Total Uploaded", _bootUploadValue, Color.FromArgb(245, 158, 11)), 1, 0);
        _bootTotals.Controls.Add(CreateMetricCard("Uptime", _uptimeValue, Color.FromArgb(236, 72, 153)), 2, 0);

        _statusValue.AutoSize = true;
        _statusValue.Margin = new Padding(0, 8, 0, 0);
        _statusValue.Text = "Starting monitors...";

        _networkNameValue.AutoSize = true;
        _networkNameValue.Margin = new Padding(0, 4, 0, 0);
        _networkNameValue.Text = "Current network: Detecting...";

        _historyPanel.Dock = DockStyle.Fill;
        _historyPanel.MinimumSize = new Size(0, 180);
        _historyPanel.Margin = new Padding(0, 8, 0, 0);
        _historyPanel.Padding = new Padding(8);
        _historyPanel.BorderStyle = BorderStyle.FixedSingle;

        _historyLabel.AutoSize = true;
        _historyLabel.Text = "Network History";
        _historyLabel.Dock = DockStyle.Top;

        _networkHistory.Dock = DockStyle.Fill;
        _networkHistory.BorderStyle = BorderStyle.None;
        _networkHistory.HorizontalScrollbar = true;
        _networkHistory.IntegralHeight = false;

        _historyPanel.Controls.Add(_networkHistory);
        _historyPanel.Controls.Add(_historyLabel);

        root.Controls.Add(_dragBar, 0, 0);
        root.Controls.Add(_liveTotals, 0, 1);
        root.Controls.Add(_bootTotals, 0, 2);
        root.Controls.Add(_statusValue, 0, 3);
        root.Controls.Add(_networkNameValue, 0, 4);
        root.Controls.Add(_historyPanel, 0, 5);

        _resizeFrame.Controls.Add(root);
        Controls.Add(_resizeFrame);

        EnableDrag(_dragBar);
        EnableResize(_resizeFrame);
        EnableResize(_dragBar);

        ApplyTheme();
        Shown += (_, __) => BringToFront();
    }

    private static Control CreateMetricCard(string title, Label valueLabel, Color accent)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 86,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 12, 0),
            Tag = "Card"
        };

        var accentBar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 6,
            BackColor = accent,
            Tag = "AccentBar"
        };

        var titleLabel = new Label
        {
            AutoSize = true,
            Text = title,
            Location = new Point(12, 9),
            Tag = "CardTitle"
        };

        valueLabel.AutoSize = true;
        valueLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        valueLabel.Location = new Point(12, 36);
        valueLabel.Text = "0 B/s";

        card.Controls.Add(valueLabel);
        card.Controls.Add(titleLabel);
        card.Controls.Add(accentBar);
        return card;
    }

    private void ToggleTheme()
    {
        _isDarkMode = !_isDarkMode;
        ApplyTheme();
        RefreshHistoryList();
    }

    private void ApplyTheme()
    {
        var background = _isDarkMode ? Color.FromArgb(12, 12, 16) : Color.FromArgb(245, 246, 250);
        var surface = _isDarkMode ? Color.FromArgb(22, 24, 32) : Color.FromArgb(230, 234, 242);
        var card = _isDarkMode ? Color.FromArgb(26, 31, 44) : Color.White;
        var history = _isDarkMode ? Color.FromArgb(20, 23, 33) : Color.FromArgb(250, 251, 255);
        var text = _isDarkMode ? Color.WhiteSmoke : Color.FromArgb(32, 36, 48);
        var muted = _isDarkMode ? Color.Gainsboro : Color.FromArgb(64, 68, 78);
        var accentText = _isDarkMode ? Color.FromArgb(144, 224, 239) : Color.FromArgb(12, 102, 132);
        var buttonBg = _isDarkMode ? Color.FromArgb(35, 38, 48) : Color.FromArgb(220, 224, 232);
        var buttonText = _isDarkMode ? Color.White : Color.FromArgb(32, 36, 48);

        BackColor = background;
        _resizeFrame.BackColor = background;
        _dragBar.BackColor = surface;
        _titleLabel.ForeColor = text;
        _themeButton.BackColor = buttonBg;
        _themeButton.ForeColor = buttonText;
        _themeButton.Text = _isDarkMode ? "☀" : "☾";

        _liveTotals.BackColor = background;
        _bootTotals.BackColor = background;
        _historyPanel.BackColor = history;
        _historyLabel.ForeColor = muted;
        _networkHistory.BackColor = history;
        _networkHistory.ForeColor = text;
        _statusValue.ForeColor = muted;
        _networkNameValue.ForeColor = accentText;

        ApplyThemeToControlTree(this, background, card, history, text, muted);
    }

    private static void ApplyThemeToControlTree(Control control, Color background, Color card, Color history, Color text, Color muted)
    {
        foreach (Control child in control.Controls)
        {
            if (child is Label label)
            {
                if (label.Text == "Network History")
                {
                    label.ForeColor = muted;
                }
                else
                {
                    label.ForeColor = text;
                }
            }
            else if (child is Button button)
            {
                button.ForeColor = text;
            }
            else if (child is Panel panel)
            {
                var tag = panel.Tag as string;
                if (tag == "Card")
                {
                    panel.BackColor = card;
                }
                else if (tag == "AccentBar")
                {
                    // preserve accent color
                }
                else if (panel.BorderStyle == BorderStyle.FixedSingle)
                {
                    panel.BackColor = history;
                }
                else
                {
                    panel.BackColor = background;
                }
            }
            else if (child is TableLayoutPanel table)
            {
                table.BackColor = background;
            }
            else if (child is ListBox listBox)
            {
                listBox.BackColor = history;
                listBox.ForeColor = text;
            }

            ApplyThemeToControlTree(child, background, card, history, text, muted);
        }
    }

    private void EnableDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
            }
        };
    }

    private void EnableResize(Control control)
    {
        control.MouseMove += (_, e) =>
        {
            var hit = GetResizeHitTest(new Point(e.X, e.Y), control.ClientSize);
            control.Cursor = hit switch
            {
                10 or 11 => Cursors.SizeWE,
                12 or 15 => Cursors.SizeNS,
                13 or 17 => Cursors.SizeNWSE,
                14 or 16 => Cursors.SizeNESW,
                _ => Cursors.Default
            };
        };

        control.MouseLeave += (_, __) => control.Cursor = Cursors.Default;

        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var hit = GetResizeHitTest(new Point(e.X, e.Y), control.ClientSize);
            if (hit == 0)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, 0x112, (IntPtr)(0xF000 + hit), IntPtr.Zero);
        };
    }

    private static int GetResizeHitTest(Point cursor, Size size)
    {
        var resizeLeft = cursor.X <= ResizeGrip;
        var resizeRight = cursor.X >= size.Width - ResizeGrip;
        var resizeTop = cursor.Y <= ResizeGrip;
        var resizeBottom = cursor.Y >= size.Height - ResizeGrip;

        if (resizeTop && resizeLeft) return 13;
        if (resizeTop && resizeRight) return 14;
        if (resizeBottom && resizeLeft) return 16;
        if (resizeBottom && resizeRight) return 17;
        if (resizeLeft) return 10;
        if (resizeRight) return 11;
        if (resizeTop) return 12;
        if (resizeBottom) return 15;
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void StartMonitors()
    {
        if (_monitoringStarted)
        {
            return;
        }

        _monitoringStarted = true;
        var token = _cts.Token;

        _summaryTimer.Interval = 1000;
        _summaryTimer.Tick += (_, __) => UpdateNetworkTotalsAndUptime();
        _summaryTimer.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var (downloadBps, uploadBps) in _netMon.MonitorTotalAsync(token))
                {
                    UpdateTotals(downloadBps, uploadBps);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                UpdateStatus($"Network totals unavailable: {ex.Message}");
            }
        }, token);

        UpdateNetworkTotalsAndUptime();
    }

    private void ExitApp()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        Close();
    }

    private void CleanupTray()
    {
        _summaryTimer.Stop();
        _tray.Visible = false;
    }

    private void UpdateTotals(long downloadBps, long uploadBps)
    {
        SafeUi(() =>
        {
            _downloadValue.Text = $"{FormatBytes(downloadBps)}/s";
            _uploadValue.Text = $"{FormatBytes(uploadBps)}/s";
            _tray.Text = $"Down {FormatBytes(downloadBps)}/s | Up {FormatBytes(uploadBps)}/s";
            _statusValue.Text = "Monitoring live network traffic";
        });
    }

    private void UpdateNetworkTotalsAndUptime()
    {
        if (!TryGetActiveNetworkStats(out var key, out var name, out var received, out var sent))
        {
            SafeUi(() =>
            {
                _networkNameValue.Text = "Current network: Not connected";
                _statusValue.Text = "Waiting for a network connection";
            });

            return;
        }

        if (!_networkUsage.TryGetValue(key, out var usage))
        {
            usage = new NetworkUsageState();
            _networkUsage[key] = usage;
        }

        usage.DisplayName = name;

        if (!string.Equals(_currentNetworkKey, key, StringComparison.OrdinalIgnoreCase))
        {
            PauseCurrentNetwork();
            _currentNetworkKey = key;
            _currentNetworkName = name;
            usage.ActiveSinceUtc = DateTime.UtcNow;
            _lastReceivedBytes = received;
            _lastSentBytes = sent;
        }
        else if (!string.Equals(_currentNetworkName, name, StringComparison.Ordinal))
        {
            _currentNetworkName = name;
        }

        if (_lastReceivedBytes >= 0 && _lastSentBytes >= 0)
        {
            usage.TotalDownloaded += Math.Max(0, received - _lastReceivedBytes);
            usage.TotalUploaded += Math.Max(0, sent - _lastSentBytes);
        }

        _lastReceivedBytes = received;
        _lastSentBytes = sent;

        var elapsed = usage.ElapsedBeforeActive + (usage.ActiveSinceUtc.HasValue
            ? DateTime.UtcNow - usage.ActiveSinceUtc.Value
            : TimeSpan.Zero);

        SafeUi(() =>
        {
            _bootDownloadValue.Text = FormatBytes(usage.TotalDownloaded);
            _bootUploadValue.Text = FormatBytes(usage.TotalUploaded);
            _uptimeValue.Text = FormatUptime(elapsed);
            _networkNameValue.Text = $"Current network: {_currentNetworkName}";
            RefreshHistoryList();
        });
    }

    private void PauseCurrentNetwork()
    {
        if (!_networkUsage.TryGetValue(_currentNetworkKey, out var current))
        {
            return;
        }

        if (current.ActiveSinceUtc.HasValue)
        {
            current.ElapsedBeforeActive += DateTime.UtcNow - current.ActiveSinceUtc.Value;
            current.ActiveSinceUtc = null;
        }
    }

    private bool TryGetActiveNetworkStats(out string key, out string name, out long received, out long sent)
    {
        key = "Unknown";
        name = "Unknown";
        received = 0;
        sent = 0;

        NetworkInterface? selected = null;

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var ip = ni.GetIPProperties();
            if (ip.GatewayAddresses.Count == 0)
            {
                continue;
            }

            selected = ni;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            {
                break;
            }
        }

        if (selected is null)
        {
            return false;
        }

        var stats = selected.GetIPv4Statistics();
        received = Math.Max(0, stats.BytesReceived);
        sent = Math.Max(0, stats.BytesSent);

        if (selected.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        {
            var ssid = TryGetConnectedSsid();
            if (!string.IsNullOrWhiteSpace(ssid))
            {
                key = $"wifi:{ssid}";
                name = ssid;
                return true;
            }
        }

        name = selected.Name;
        key = $"{selected.NetworkInterfaceType}:{selected.Id}";
        return true;
    }

    private static string? TryGetConnectedSsid()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.StartsWith("SSID name", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = trimmed.IndexOf(':');
                    if (idx > 0 && idx + 1 < trimmed.Length)
                    {
                        var ssid = trimmed[(idx + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(ssid))
                        {
                            return ssid;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private void RefreshHistoryList()
    {
        var selected = _networkHistory.SelectedIndex;
        _networkHistory.BeginUpdate();
        _networkHistory.Items.Clear();

        foreach (var pair in _networkUsage)
        {
            var entry = pair.Value;
            var totalElapsed = entry.ElapsedBeforeActive + (entry.ActiveSinceUtc.HasValue
                ? DateTime.UtcNow - entry.ActiveSinceUtc.Value
                : TimeSpan.Zero);

            var marker = string.Equals(pair.Key, _currentNetworkKey, StringComparison.OrdinalIgnoreCase)
                ? "*"
                : " ";

            var displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? pair.Key : entry.DisplayName;

            _networkHistory.Items.Add(
                $"{marker} {displayName} | Down {FormatBytes(entry.TotalDownloaded)} | Up {FormatBytes(entry.TotalUploaded)} | {FormatUptime(totalElapsed)}");
        }

        _networkHistory.EndUpdate();
        if (selected >= 0 && selected < _networkHistory.Items.Count)
        {
            _networkHistory.SelectedIndex = selected;
        }
    }

    private void UpdateStatus(string text)
    {
        SafeUi(() =>
        {
            _statusValue.Text = text;
            _tray.Text = text.Length <= 63 ? text : text[..63];
        });
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000)
        {
            return $"{bytes / 1_000_000.0:F1} MB";
        }

        if (bytes >= 1_000)
        {
            return $"{bytes / 1_000.0:F1} KB";
        }

        return $"{bytes} B";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.Days > 0)
        {
            return $"{uptime.Days}d {uptime:hh\\:mm\\:ss}";
        }

        return uptime.ToString(@"hh\:mm\:ss");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            PauseCurrentNetwork();
            _tray.Dispose();
            _cts.Dispose();
        }

        base.Dispose(disposing);
    }
}

public sealed class NetworkUsageState
{
    public string DisplayName = string.Empty;
    public long TotalDownloaded;
    public long TotalUploaded;
    public TimeSpan ElapsedBeforeActive;
    public DateTime? ActiveSinceUtc;
}
