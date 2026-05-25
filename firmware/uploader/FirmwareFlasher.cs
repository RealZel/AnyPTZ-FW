using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FirmwareFlasher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public sealed class MainForm : Form
    {
        private static readonly int[] StandardBaudRates = new[] { 9600, 115200, 921600 };

        private readonly ComboBox _portsCombo = new ComboBox();
        private readonly Button _refreshPortsButton = new Button();
        private readonly TextBox _firmwarePathText = new TextBox();
        private readonly Button _browseButton = new Button();
        private readonly ComboBox _baudCombo = new ComboBox();
        private readonly Button _logsToggleButton = new Button();
        private readonly Button _cancelButton = new Button();
        private readonly Button _flashButton = new Button();
        private readonly TextBox _logText = new TextBox();
        private readonly Timer _monitorFlushTimer = new Timer();

        private const int MaxLogLines = 3000;
        private const int FlashTimeoutMs = 600000;

        private Process _currentProcess;
        private SerialPort _monitorPort;
        private bool _isRefreshingPorts;
        private bool _isOpeningLogs;
        private bool _isClosingForm;
        private readonly object _monitorBufferLock = new object();
        private readonly Queue<string> _monitorBuffer = new Queue<string>();

        public MainForm()
        {
            Text = "Biolumos FW Uploader";
            Width = 920;
            Height = 600;
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            BuildUi();
            ConfigureLogBufferTimer();
            LoadDefaultValues();
            UpdateUiState();

            Shown += async delegate
            {
                await RefreshPortsAsync(false);
            };
            FormClosing += OnFormClosing;
        }

        private void ConfigureLogBufferTimer()
        {
            _monitorFlushTimer.Interval = 120;
            _monitorFlushTimer.Tick += delegate
            {
                FlushMonitorBuffer();
            };
            _monitorFlushTimer.Start();
        }

        private void BuildUi()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 4,
                Padding = new Padding(12)
            };

            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var portLabel = BuildLabel("COM port");
            var firmwareLabel = BuildLabel("Firmware (.bin)");
            var baudLabel = BuildLabel("Speed");

            _portsCombo.Dock = DockStyle.Fill;
            _portsCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _portsCombo.Margin = new Padding(4);

            _refreshPortsButton.Text = "Refresh";
            _refreshPortsButton.Dock = DockStyle.Fill;
            _refreshPortsButton.Margin = new Padding(4);
            _refreshPortsButton.Click += async delegate
            {
                await RefreshPortsAsync(true);
            };

            _firmwarePathText.Dock = DockStyle.Fill;
            _firmwarePathText.Margin = new Padding(4);

            _browseButton.Text = "Browse";
            _browseButton.Dock = DockStyle.Fill;
            _browseButton.Margin = new Padding(4);
            _browseButton.Click += delegate
            {
                BrowseFirmware();
            };

            _baudCombo.Dock = DockStyle.Fill;
            _baudCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _baudCombo.Margin = new Padding(4);

            _logsToggleButton.Text = "Logs on";
            _logsToggleButton.Dock = DockStyle.Fill;
            _logsToggleButton.Margin = new Padding(4);
            _logsToggleButton.Click += async delegate
            {
                await ToggleLogsAsync();
            };

            _cancelButton.Text = "Cancel";
            _cancelButton.Dock = DockStyle.Fill;
            _cancelButton.Margin = new Padding(4);
            _cancelButton.Click += delegate
            {
                CancelFlash();
            };

            _flashButton.Text = "Flash";
            _flashButton.Dock = DockStyle.Fill;
            _flashButton.Margin = new Padding(4);
            _flashButton.Click += async delegate
            {
                try
                {
                    await FlashAsync();
                }
                catch (Exception ex)
                {
                    AppendLogLine("Unhandled error: " + ex.Message);
                    MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _currentProcess = null;
                    if (!IsDisposed && !Disposing && IsHandleCreated)
                    {
                        UpdateUiState();
                    }
                }
            };

            _logText.Multiline = true;
            _logText.ReadOnly = true;
            _logText.ScrollBars = ScrollBars.Vertical;
            _logText.Dock = DockStyle.Fill;
            _logText.Margin = new Padding(4);
            _logText.Font = new Font("Consolas", 9.0f, FontStyle.Regular, GraphicsUnit.Point);

            table.Controls.Add(portLabel, 0, 0);
            table.Controls.Add(_portsCombo, 1, 0);
            table.SetColumnSpan(_portsCombo, 3);
            table.Controls.Add(_refreshPortsButton, 4, 0);

            table.Controls.Add(firmwareLabel, 0, 1);
            table.Controls.Add(_firmwarePathText, 1, 1);
            table.SetColumnSpan(_firmwarePathText, 3);
            table.Controls.Add(_browseButton, 4, 1);

            table.Controls.Add(baudLabel, 0, 2);
            table.Controls.Add(_baudCombo, 1, 2);
            table.Controls.Add(_logsToggleButton, 2, 2);
            table.Controls.Add(_cancelButton, 3, 2);
            table.Controls.Add(_flashButton, 4, 2);

            table.Controls.Add(_logText, 0, 3);
            table.SetColumnSpan(_logText, 5);

            Controls.Add(table);
        }

        private static Label BuildLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(4)
            };
        }

        private void LoadDefaultValues()
        {
            _baudCombo.Items.Clear();
            foreach (var baudRate in StandardBaudRates)
            {
                _baudCombo.Items.Add(baudRate.ToString());
            }

            _baudCombo.SelectedItem = "921600";

            var defaultFirmware = ResolveDefaultFirmwarePath();

            if (!string.IsNullOrWhiteSpace(defaultFirmware))
            {
                _firmwarePathText.Text = defaultFirmware;
            }
        }

        private static string ResolveDefaultFirmwarePath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidatePaths = new[]
            {
                Path.Combine(baseDir, "firmware.bin"),
                Path.Combine(baseDir, "AnyPTZ_firmware.bin"),
                Path.Combine(baseDir, "..", "biolumFW_2.1b.bin"),
                Path.Combine(baseDir, "biolumFW_2.1b.bin"),
                Path.Combine(baseDir, "..", "esp32_web_BLE_api_led_V4", "build", "esp32.esp32.esp32", "fw_2.2v.bin"),
                Path.Combine(baseDir, "fw_2.2v.bin")
            };

            foreach (var candidate in candidatePaths)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bootloader.bin",
                "partitions.bin",
                "littlefs.bin",
                "esp32_web_BLE_api_led_V4.ino.bootloader.bin",
                "esp32_web_BLE_api_led_V4.ino.partitions.bin"
            };

            return Directory.GetFiles(baseDir, "*.bin", SearchOption.TopDirectoryOnly)
                .Where(path => !excluded.Contains(Path.GetFileName(path)))
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }

        private async Task RefreshPortsAsync(bool userInitiated)
        {
            if (_isRefreshingPorts || _currentProcess != null || IsMonitoringLogs() || _isOpeningLogs)
            {
                return;
            }

            var current = _portsCombo.SelectedItem as PortOption;
            var selectedPortName = current != null ? current.PortName : null;

            _isRefreshingPorts = true;
            UpdateUiState();

            try
            {
                var ports = await Task.Run(delegate
                {
                    return LoadPortOptions();
                });

                if (IsDisposed || Disposing)
                {
                    return;
                }

                _portsCombo.BeginUpdate();
                _portsCombo.Items.Clear();
                foreach (var port in ports)
                {
                    _portsCombo.Items.Add(port);
                }
                _portsCombo.EndUpdate();

                RestoreSelectedPort(selectedPortName);

                if (userInitiated)
                {
                    AppendLogLine("Ports refreshed.");
                }
            }
            catch (Exception ex)
            {
                AppendLogLine("Port refresh error: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Port Refresh Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isRefreshingPorts = false;
                if (!IsDisposed && !Disposing && IsHandleCreated)
                {
                    UpdateUiState();
                }
            }
        }

        private List<PortOption> LoadPortOptions()
        {
            var captions = GetPortCaptions();
            var ports = new List<PortOption>();
            foreach (var portName in SerialPort.GetPortNames().OrderBy(name => name))
            {
                string caption;
                captions.TryGetValue(portName, out caption);
                ports.Add(new PortOption(portName, caption));
            }

            return ports;
        }

        private void RestoreSelectedPort(string selectedPortName)
        {
            if (!string.IsNullOrWhiteSpace(selectedPortName))
            {
                for (var index = 0; index < _portsCombo.Items.Count; index++)
                {
                    var item = _portsCombo.Items[index] as PortOption;
                    if (item != null && string.Equals(item.PortName, selectedPortName, StringComparison.OrdinalIgnoreCase))
                    {
                        _portsCombo.SelectedIndex = index;
                        return;
                    }
                }
            }

            if (_portsCombo.Items.Count > 0)
            {
                _portsCombo.SelectedIndex = 0;
            }
        }

        private static Dictionary<string, string> GetPortCaptions()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, Caption FROM Win32_SerialPort"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject device in collection)
                    {
                        var deviceId = Convert.ToString(device["DeviceID"]);
                        var caption = Convert.ToString(device["Caption"]);
                        if (string.IsNullOrWhiteSpace(deviceId))
                        {
                            continue;
                        }

                        result[deviceId] = caption;
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private void BrowseFirmware()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Select firmware file";
                dialog.Filter = "Firmware (*.bin)|*.bin|All files (*.*)|*.*";
                dialog.Multiselect = false;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _firmwarePathText.Text = dialog.FileName;
                }
            }
        }

        private async Task ToggleLogsAsync()
        {
            if (_isOpeningLogs)
            {
                return;
            }

            if (IsMonitoringLogs())
            {
                StopLogs(true);
                return;
            }

            var portItem = _portsCombo.SelectedItem as PortOption;
            if (portItem == null)
            {
                MessageBox.Show(this, "Select COM port.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int baud;
            if (!TryGetSelectedBaud(out baud))
            {
                MessageBox.Show(this, "Select log speed.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _isOpeningLogs = true;
            UpdateUiState();

            try
            {
                var monitorPort = await Task.Run(delegate
                {
                    var serialPort = new SerialPort(portItem.PortName, baud);
                    serialPort.DtrEnable = false;
                    serialPort.RtsEnable = false;
                    serialPort.ReadTimeout = 500;
                    serialPort.WriteTimeout = 500;
                    serialPort.Open();
                    return serialPort;
                });

                if (_isClosingForm || IsDisposed || Disposing)
                {
                    monitorPort.Dispose();
                    return;
                }

                monitorPort.DataReceived += OnMonitorPortDataReceived;
                _monitorPort = monitorPort;
                AppendLogLine("=== Logs on: " + portItem.DisplayText + " @ " + baud + " ===");
            }
            catch (Exception ex)
            {
                AppendLogLine("Log open error: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Logs Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isOpeningLogs = false;
                if (!IsDisposed && !Disposing && IsHandleCreated)
                {
                    UpdateUiState();
                }
            }
        }

        private void OnMonitorPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var serialPort = sender as SerialPort;
            if (serialPort == null)
            {
                return;
            }

            try
            {
                var text = serialPort.ReadExisting();
                if (!string.IsNullOrEmpty(text))
                {
                    EnqueueMonitorText(text);
                }
            }
            catch (Exception ex)
            {
                EnqueueMonitorText("Log read error: " + ex.Message + Environment.NewLine);
                if (!IsDisposed && !Disposing && IsHandleCreated)
                {
                    BeginInvoke(new Action(delegate
                    {
                        StopLogs(false);
                    }));
                }
            }
        }

        private async Task FlashAsync()
        {
            if (_currentProcess != null)
            {
                return;
            }

            if (IsMonitoringLogs() || _isOpeningLogs)
            {
                MessageBox.Show(this, "Turn logs off before flashing.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var portItem = _portsCombo.SelectedItem as PortOption;
            if (portItem == null)
            {
                MessageBox.Show(this, "Select COM port.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var firmwarePath = _firmwarePathText.Text.Trim();
            if (!File.Exists(firmwarePath))
            {
                MessageBox.Show(this, "Select a valid firmware .bin file.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            firmwarePath = Path.GetFullPath(firmwarePath);

            var flashPlan = ResolveFlashPlan(firmwarePath);
            if (!flashPlan.IsValid)
            {
                MessageBox.Show(this, flashPlan.ValidationError, "Flash profile error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int baud;
            if (!TryGetSelectedBaud(out baud))
            {
                MessageBox.Show(this, "Select upload speed.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var esptoolPath = ResolveLocalEsptool();
            if (!File.Exists(esptoolPath))
            {
                MessageBox.Show(this, "Local tools\\esptool.exe not found. Put esptool next to the program.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateUiState();
            AppendLogLine("=== Flash start ===");
            AppendLogLine("mode: " + flashPlan.Mode);
            AppendLogLine("esptool: " + esptoolPath);
            AppendLogLine("port: " + portItem.DisplayText);
            AppendLogLine("app: " + firmwarePath);
            AppendLogLine("profile: " + flashPlan.ProfileSummary);
            AppendLogLine("speed: " + baud);

            foreach (var item in flashPlan.Items)
            {
                AppendLogLine("flash: " + item.Offset + " -> " + item.FilePath);
            }

            var argsParts = new List<string>
            {
                "--chip esp32",
                "--port " + Quote(portItem.PortName),
                "--baud " + baud,
                "--before default_reset",
                "--after hard_reset",
                "write_flash",
                "-z"
            };

            foreach (var item in flashPlan.Items)
            {
                argsParts.Add(item.Offset);
                argsParts.Add(Quote(item.FilePath));
            }

            var args = string.Join(" ", argsParts);

            var psi = new ProcessStartInfo
            {
                FileName = esptoolPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(esptoolPath)
            };

            Process process = null;
            try
            {
                process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _currentProcess = process;
                UpdateUiState();

                process.OutputDataReceived += OnProcessOutput;
                process.ErrorDataReceived += OnProcessOutput;

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var deadline = DateTime.UtcNow.AddMilliseconds(FlashTimeoutMs);
                while (!process.HasExited)
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        throw new TimeoutException("Flashing timed out.");
                    }

                    await Task.Delay(150);
                }

                if (process.ExitCode == 0)
                {
                    AppendLogLine("=== Flash completed successfully ===");
                }
                else
                {
                    AppendLogLine("=== Flash failed. Exit code: " + process.ExitCode + " ===");
                }
            }
            catch (Exception ex)
            {
                AppendLogLine("Flash error: " + ex.Message);
                throw;
            }
            finally
            {
                if (process != null)
                {
                    process.OutputDataReceived -= OnProcessOutput;
                    process.ErrorDataReceived -= OnProcessOutput;
                    process.Dispose();
                }

                _currentProcess = null;
                if (!IsDisposed && !Disposing && IsHandleCreated)
                {
                    UpdateUiState();
                }
            }
        }

        private bool TryGetSelectedBaud(out int baud)
        {
            var baudText = Convert.ToString(_baudCombo.SelectedItem);
            baud = 0;
            return !string.IsNullOrWhiteSpace(baudText) && int.TryParse(baudText, out baud);
        }

        private static string ResolveAppOffset(string firmwarePath)
        {
            var fileName = Path.GetFileName(firmwarePath);
            if (string.Equals(fileName, "fw_2.2v.bin", StringComparison.OrdinalIgnoreCase))
            {
                return "0x10000";
            }

            if (string.Equals(fileName, "biolumFW_2.1b.bin", StringComparison.OrdinalIgnoreCase))
            {
                return "0x10000";
            }

            return "0x10000";
        }

        private static FlashPlan ResolveFlashPlan(string firmwarePath)
        {
            var fileName = Path.GetFileName(firmwarePath);
            var appOffset = ResolveAppOffset(firmwarePath);

            var anyPtzPlan = ResolveAnyPtzFullPlan(firmwarePath, appOffset);
            if (anyPtzPlan.IsValid)
            {
                return anyPtzPlan;
            }

            if (string.Equals(fileName, "biolumFW_2.1b.bin", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveBiolum21bPlan(firmwarePath, appOffset);
            }

            return new FlashPlan(
                "app",
                "app-only auto",
                true,
                null,
                new List<FlashItem>
                {
                    new FlashItem(appOffset, firmwarePath)
                });
        }

        private static FlashPlan ResolveAnyPtzFullPlan(string firmwarePath, string appOffset)
        {
            var fileName = Path.GetFileName(firmwarePath);
            var isAnyPtzFirmwareCandidate =
                string.Equals(fileName, "firmware.bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "AnyPTZ_firmware.bin", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("AnyPTZ", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(fileName) ||
                string.Equals(fileName, "bootloader.bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "partitions.bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "littlefs.bin", StringComparison.OrdinalIgnoreCase))
            {
                return new FlashPlan(
                    "app",
                    "app-only auto",
                    false,
                    "",
                    new List<FlashItem>());
            }

            var baseDir = Path.GetDirectoryName(firmwarePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var bootloaderPath = Path.Combine(baseDir, "bootloader.bin");
            var partitionsPath = Path.Combine(baseDir, "partitions.bin");
            var littlefsPath = Path.Combine(baseDir, "littlefs.bin");

            if (!File.Exists(bootloaderPath) || !File.Exists(partitionsPath) || !File.Exists(littlefsPath))
            {
                if (isAnyPtzFirmwareCandidate)
                {
                    return new FlashPlan(
                        "full",
                        "AnyPTZ full flash with LittleFS",
                        false,
                        "Missing companion files рядом с firmware.bin: bootloader.bin, partitions.bin и/or littlefs.bin.",
                        new List<FlashItem>());
                }

                return new FlashPlan(
                    "app",
                    "app-only auto",
                    false,
                    "",
                    new List<FlashItem>());
            }

            return new FlashPlan(
                "full",
                "AnyPTZ full flash with LittleFS",
                true,
                null,
                new List<FlashItem>
                {
                    new FlashItem("0x1000", bootloaderPath),
                    new FlashItem("0x8000", partitionsPath),
                    new FlashItem(appOffset, firmwarePath),
                    new FlashItem("0x310000", littlefsPath)
                });
        }

        private static FlashPlan ResolveBiolum21bPlan(string firmwarePath, string appOffset)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var bootloaderPath = Path.Combine(baseDir, "bootloader.bin");
            var partitionsPath = Path.Combine(baseDir, "partitions.bin");

            if (string.IsNullOrWhiteSpace(bootloaderPath) || string.IsNullOrWhiteSpace(partitionsPath))
            {
                return new FlashPlan(
                    "full",
                    "biolumFW_2.1b profile",
                    false,
                    "For biolumFW_2.1b.bin put companion files in program folder: bootloader.bin and partitions.bin.",
                    new List<FlashItem>());
            }

            if (!File.Exists(bootloaderPath) || !File.Exists(partitionsPath))
            {
                return new FlashPlan(
                    "full",
                    "biolumFW_2.1b profile",
                    false,
                    "Missing fixed files in app folder: bootloader.bin and/or partitions.bin.",
                    new List<FlashItem>());
            }

            return new FlashPlan(
                "full",
                "biolumFW_2.1b fixed companions + auto app offset",
                true,
                null,
                new List<FlashItem>
                {
                    new FlashItem("0x1000", bootloaderPath),
                    new FlashItem("0x8000", partitionsPath),
                    new FlashItem(appOffset, firmwarePath)
                });
        }


        private void OnProcessOutput(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                AppendLogLine(e.Data);
            }
        }

        private void UpdateUiState()
        {
            var isFlashing = _currentProcess != null;
            var isMonitoringLogs = IsMonitoringLogs();
            var portsBusy = _isRefreshingPorts || _isOpeningLogs;

            _portsCombo.Enabled = !isFlashing && !portsBusy && !isMonitoringLogs;
            _refreshPortsButton.Enabled = !isFlashing && !portsBusy && !isMonitoringLogs;
            _refreshPortsButton.Text = _isRefreshingPorts ? "Loading..." : "Refresh";
            _firmwarePathText.Enabled = !isFlashing;
            _browseButton.Enabled = !isFlashing;
            _baudCombo.Enabled = !isFlashing && !isMonitoringLogs;
            _logsToggleButton.Enabled = !isFlashing && !_isRefreshingPorts && !_isOpeningLogs && (_portsCombo.Items.Count > 0 || isMonitoringLogs);
            _logsToggleButton.Text = _isOpeningLogs ? "Opening..." : (isMonitoringLogs ? "Logs off" : "Logs on");
            _cancelButton.Enabled = isFlashing;
            _flashButton.Enabled = !isFlashing && !portsBusy && !isMonitoringLogs;
        }

        private bool IsMonitoringLogs()
        {
            return _monitorPort != null && _monitorPort.IsOpen;
        }

        private void CancelFlash()
        {
            if (_currentProcess == null)
            {
                return;
            }

            try
            {
                if (!_currentProcess.HasExited)
                {
                    _currentProcess.Kill();
                    AppendLogLine("=== Flash cancelled by user ===");
                }
            }
            catch (Exception ex)
            {
                AppendLogLine("Cancel error: " + ex.Message);
            }
        }

        private void StopLogs(bool writeLogLine)
        {
            var monitorPort = _monitorPort;
            _monitorPort = null;

            if (monitorPort == null)
            {
                UpdateUiState();
                return;
            }

            try
            {
                monitorPort.DataReceived -= OnMonitorPortDataReceived;
                if (monitorPort.IsOpen)
                {
                    monitorPort.Close();
                }
            }
            catch (Exception ex)
            {
                AppendLogLine("Log close error: " + ex.Message);
            }
            finally
            {
                monitorPort.Dispose();
            }

            if (writeLogLine)
            {
                AppendLogLine("=== Logs off ===");
            }

            if (!IsDisposed && !Disposing && IsHandleCreated)
            {
                UpdateUiState();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _isClosingForm = true;

            if (_currentProcess != null)
            {
                var result = MessageBox.Show(
                    this,
                    "Flashing is still running. Stop and exit?",
                    "Confirm exit",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                {
                    _isClosingForm = false;
                    e.Cancel = true;
                    return;
                }

                CancelFlash();
            }

            _monitorFlushTimer.Stop();
            StopLogs(false);
        }

        private void AppendLogLine(string line)
        {
            AppendLogText(line + Environment.NewLine);
        }

        private void AppendLogRaw(string text)
        {
            AppendLogText(text);
        }

        private void EnqueueMonitorText(string text)
        {
            lock (_monitorBufferLock)
            {
                _monitorBuffer.Enqueue(text);
            }
        }

        private void FlushMonitorBuffer()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            StringBuilder builder = null;
            lock (_monitorBufferLock)
            {
                if (_monitorBuffer.Count == 0)
                {
                    return;
                }

                builder = new StringBuilder();
                while (_monitorBuffer.Count > 0)
                {
                    builder.Append(_monitorBuffer.Dequeue());
                }
            }

            AppendLogRaw(builder.ToString());
        }

        private void AppendLogText(string text)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (!IsHandleCreated)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action<string>(AppendLogText), text);
                }
                catch
                {
                }
                return;
            }

            _logText.AppendText(text);

            if (_logText.Lines.Length > MaxLogLines)
            {
                _logText.Lines = _logText.Lines.Skip(_logText.Lines.Length - MaxLogLines).ToArray();
            }
        }

        private static string ResolveLocalEsptool()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "esptool.exe");
        }

        private static string Quote(string value)
        {
            if (value.IndexOf(' ') >= 0)
            {
                return "\"" + value + "\"";
            }

            return value;
        }

        private sealed class PortOption
        {
            public PortOption(string portName, string caption)
            {
                PortName = portName;
                Caption = string.IsNullOrWhiteSpace(caption) ? "Unknown device" : caption.Trim();
            }

            public string PortName { get; private set; }

            public string Caption { get; private set; }

            public string DisplayText
            {
                get
                {
                    return PortName + " - " + Caption;
                }
            }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        private sealed class FlashItem
        {
            public FlashItem(string offset, string filePath)
            {
                Offset = offset;
                FilePath = filePath;
            }

            public string Offset { get; private set; }

            public string FilePath { get; private set; }
        }

        private sealed class FlashPlan
        {
            public FlashPlan(string mode, string profileSummary, bool isValid, string validationError, List<FlashItem> items)
            {
                Mode = mode;
                ProfileSummary = profileSummary;
                IsValid = isValid;
                ValidationError = validationError;
                Items = items;
            }

            public string Mode { get; private set; }

            public string ProfileSummary { get; private set; }

            public bool IsValid { get; private set; }

            public string ValidationError { get; private set; }

            public List<FlashItem> Items { get; private set; }
        }

    }
}
