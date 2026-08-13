using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LaptopQaUsbBuilder;

public partial class MainWindow : Window
{
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ObservableCollection<string> _winFolders = [];
    private readonly ObservableCollection<string> _itFolders = [];
    private bool _isBuilding;
    private string? _logPath;
    private List<PartitionConfig> _partitions = [];
    private static readonly string VersionLabel = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.3.8"}";

    public MainWindow()
    {
        InitializeComponent();
        _partitions = LoadPartitionConfig();
        ApplyPartitionConfig();
        WinFoldersList.ItemsSource = _winFolders;
        ItFoldersList.ItemsSource = _itFolders;
        Loaded += async (_, _) =>
        {
            AddActivity("USB Drive Builder started in administrator mode.");
            await RefreshDisksAsync();
        };
        Closing += (_, e) =>
        {
            if (!_isBuilding) return;
            e.Cancel = true;
            MessageBox.Show("Wait for the active USB build to finish before closing.", "Build in progress", MessageBoxButton.OK, MessageBoxImage.Information);
        };
    }

    private async Task RefreshDisksAsync()
    {
        try
        {
            RefreshButton.IsEnabled = false;
            FooterText.Text = $"Administrator mode  |  {VersionLabel}  |  Scanning for USB disks";
            var script = "$ProgressPreference='SilentlyContinue'; ConvertTo-Json -InputObject @(Get-Disk | Where-Object BusType -eq 'USB' | Where-Object OperationalStatus -ne 'Offline' | Sort-Object Number | Select-Object Number,FriendlyName,SerialNumber,UniqueId,Size,IsBoot,IsSystem) -Compress";
            var json = await RunPowerShellAsync(script);
            var disks = DeserializeUsbDisks(json);
            DiskPicker.ItemsSource = disks;
            DiskPicker.UnselectAll();
            FooterText.Text = disks.Count == 0
                ? $"Administrator mode  |  {VersionLabel}  |  No USB disks detected"
                : $"Administrator mode  |  {VersionLabel}  |  {disks.Count} USB disk(s) detected";
            if (disks.Count == 0)
            {
                SummaryTarget.Text = "No USB selected";
                UpdatePartitionPreview([]);
                AddActivity("No USB disks detected. Insert a drive and select Refresh.");
            }
        }
        catch (Exception ex)
        {
            AddActivity($"Disk scan failed: {ex.Message}");
            MessageBox.Show($"Unable to scan USB disks.\n\n{ex.Message}", "USB scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            UpdateBuildButton();
        }
    }

    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        var queuedDisks = SelectedDisks();
        if (queuedDisks.Count == 0) return;
        var requiredSize = _partitions.Take(_partitions.Count - 1).Sum(p => PartitionConfig.TryParseSize(p.SizeText, out var bytes) ? bytes : 0) + 64L * 1024 * 1024;
        var tooSmall = queuedDisks.Where(d => d.Size < requiredSize).ToList();
        if (tooSmall.Count > 0)
        {
            MessageBox.Show($"These drives are too small for the configured layout (minimum {FormatBytes(requiredSize)}):\n\n{string.Join("\n", tooSmall.Select(d => $"Disk {d.Number} - {d.FriendlyName} ({FormatBytes(d.Size)})"))}",
                "Drive too small", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var fixedSize = _partitions.Take(_partitions.Count - 1).Sum(p => PartitionConfig.TryParseSize(p.SizeText, out var bytes) ? bytes : 0);
        var fat32TooLarge = queuedDisks.Where(d => _partitions[^1].FileSystem == "FAT32" && d.Size - fixedSize > 32L * 1024 * 1024 * 1024).ToList();
        if (fat32TooLarge.Count > 0)
        {
            MessageBox.Show("The remaining partition would exceed Windows' 32 GB FAT32 formatting limit. Choose NTFS or exFAT for the final partition, or increase the fixed partitions.",
                "FAT32 partition too large", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_partitions.Count < 2 && (_winFolders.Count > 0 || !string.IsNullOrWhiteSpace(UnattendSource.Text)))
        {
            MessageBox.Show("Win11 content requires partition 2. Add a second partition or remove the Win11 sources.", "Partition 2 not configured", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_partitions.Count < 3 && _itFolders.Count > 0)
        {
            MessageBox.Show("IT Support content requires partition 3. Add a third partition or remove the IT Support sources.", "Partition 3 not configured", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folderSources = _winFolders.Concat(_itFolders).ToArray();
        if (!string.IsNullOrWhiteSpace(UnattendSource.Text) && !File.Exists(UnattendSource.Text.Trim()))
        {
            MessageBox.Show("The selected Autounattend.xml path must be an existing file.", "Invalid answer file", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        foreach (var source in folderSources)
        {
            if (!Directory.Exists(source))
            {
                MessageBox.Show($"Source folder not found:\n{source}", "Missing source", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            foreach (var disk in queuedDisks)
            {
                if (await SourceIsOnDiskAsync(source, disk.Number))
                {
                    MessageBox.Show($"A copy source is stored on queued Disk {disk.Number} and would be erased before it could be copied:\n{source}",
                        "Source is on target disk", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(UnattendSource.Text))
        {
            foreach (var disk in queuedDisks)
                if (await SourceIsOnDiskAsync(UnattendSource.Text.Trim(), disk.Number))
                {
                    MessageBox.Show($"The Autounattend.xml file is stored on queued Disk {disk.Number} and would be erased before it could be copied:\n{UnattendSource.Text.Trim()}",
                        "Source is on target disk", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
        }

        var queueSummary = string.Join("\n", queuedDisks.Select(d => $"Disk {d.Number} - {d.FriendlyName} - {FormatBytes(d.Size)}"));
        var answer = MessageBox.Show($"Permanently erase and build these {queuedDisks.Count} USB drive(s) sequentially?\n\n{queueSummary}\n\nThis cannot be undone.",
            "Final confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopQAUsbBuilder", "Logs");
        Directory.CreateDirectory(logFolder);
        _logPath = Path.Combine(logFolder, $"Build-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        ActivityList.Items.Clear();
        SetBuildingState(true);
        SetStatus("Building", "#B36A13");
        BuildProgress.IsIndeterminate = true;

        var succeeded = 0;
        var failures = new List<string>();
        for (var queueIndex = 0; queueIndex < queuedDisks.Count; queueIndex++)
        {
            var disk = queuedDisks[queueIndex];
            SetStatus($"Building {queueIndex + 1} of {queuedDisks.Count}", "#B36A13");
            BuildProgress.IsIndeterminate = true;
            try
            {
                AddActivity($"QUEUE {queueIndex + 1}/{queuedDisks.Count}: Locked target to Disk {disk.Number}: {disk.FriendlyName}.");
                AddActivity("Clearing existing partitions and creating the requested layout.");
                var result = await CreatePartitionsAsync(disk);
                BuildProgress.IsIndeterminate = false;
                BuildProgress.Value = 35;
                foreach (var partition in _partitions) AddActivity($"Created {partition.Name} ({partition.SizeText}, {partition.FileSystem}).");
                if (_partitions.Count >= 2)
                {
                    await CopyFoldersAsync(_winFolders, $"{result.WinLetter}:\\", _partitions[1].Name, 35, 65);
                    await CopyUnattendAsync(UnattendSource.Text.Trim(), $"{result.WinLetter}:\\");
                }
                if (_partitions.Count >= 3)
                    await CopyFoldersAsync(_itFolders, $"{result.SupportLetter}:\\", _partitions[2].Name, 65, 95);
                AddActivity("Verifying partition labels and file systems.");
                await VerifyPartitionsAsync(disk.Number, disk.UniqueId);
                BuildProgress.Value = 100;
                succeeded++;
                AddActivity($"Disk {disk.Number} completed and verified.");
                Log($"Disk {disk.Number} completed and verified.");
            }
            catch (Exception ex)
            {
                failures.Add($"Disk {disk.Number}: {ex.Message}");
                AddActivity($"Disk {disk.Number} FAILED: {ex.Message}. Continuing queue.");
                Log($"Disk {disk.Number} ERROR: {ex}");
            }
        }
        BuildProgress.IsIndeterminate = false;
        SetBuildingState(false);
        ConfirmText.Clear();
        SetStatus(failures.Count == 0 ? "Complete" : "Queue finished", failures.Count == 0 ? "#147A4B" : "#AE3338");
        var failureText = failures.Count == 0 ? "" : $"\n\nFailures:\n{string.Join("\n", failures)}";
        MessageBox.Show($"Queue finished.\n\nSucceeded: {succeeded}\nFailed: {failures.Count}{failureText}\n\nLog: {_logPath}",
            "USB queue complete", MessageBoxButton.OK, failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task<PartitionResult> CreatePartitionsAsync(UsbDisk disk)
    {
        var expectedId = PsQuote(disk.UniqueId ?? "");
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference='Stop'");
        script.AppendLine("$ProgressPreference='SilentlyContinue'");
        script.AppendLine($"$d=Get-Disk -Number {disk.Number}");
        script.AppendLine("if($d.BusType -ne 'USB'){throw 'Selected disk is no longer a USB disk.'}");
        script.AppendLine("if($d.IsBoot -or $d.IsSystem){throw 'Refusing to modify a boot or system disk.'}");
        script.AppendLine($"if('{expectedId}' -and [string]$d.UniqueId -ne '{expectedId}'){{throw 'The USB device changed after selection. Refresh and select it again.'}}");
        script.AppendLine($"if($d.IsReadOnly){{Set-Disk -Number {disk.Number} -IsReadOnly $false}}");
        script.AppendLine($"if($d.IsOffline){{Set-Disk -Number {disk.Number} -IsOffline $false}}");
        script.AppendLine($"if($d.PartitionStyle -ne 'RAW'){{Clear-Disk -Number {disk.Number} -RemoveData -RemoveOEM -Confirm:$false}}");
        script.AppendLine($"Initialize-Disk -Number {disk.Number} -PartitionStyle GPT | Out-Null");

        for (var index = 0; index < _partitions.Count; index++)
        {
            var item = _partitions[index];
            var variable = $"p{index + 1}";
            var sizeArgument = item.IsRemaining
                ? "-UseMaximumSize"
                : PartitionConfig.TryParseSize(item.SizeText, out var sizeBytes) ? $"-Size {sizeBytes}" : throw new InvalidOperationException($"Invalid size for partition {index + 1}.");
            script.AppendLine($"${variable}=New-Partition -DiskNumber {disk.Number} {sizeArgument} -AssignDriveLetter");
            var allocation = item.FileSystem == "NTFS" ? " -AllocationUnitSize 4096" : "";
            script.AppendLine($"${variable} | Format-Volume -FileSystem {item.FileSystem} -NewFileSystemLabel '{PsQuote(item.Name)}'{allocation} -Confirm:$false -Force | Out-Null");
        }

        var winExpression = _partitions.Count >= 2 ? "[string](($p2|Get-Volume).DriveLetter)" : "''";
        var supportExpression = _partitions.Count >= 3 ? "[string](($p3|Get-Volume).DriveLetter)" : "''";
        script.AppendLine($"[pscustomobject]@{{WinLetter={winExpression};SupportLetter={supportExpression}}} | ConvertTo-Json -Compress");
        var json = await RunPowerShellAsync(script.ToString());
        return JsonSerializer.Deserialize<PartitionResult>(json, _jsonOptions)
               ?? throw new InvalidOperationException("Windows did not return the new partition drive letters.");
    }

    private async Task VerifyPartitionsAsync(int diskNumber, string? uniqueId)
    {
        var id = PsQuote(uniqueId ?? "");
        var expected = string.Join(",", _partitions.Select(p => $"[pscustomobject]@{{Label='{PsQuote(p.Name)}';Fs='{p.FileSystem}'}}"));
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference='Stop'");
        script.AppendLine($"$d=Get-Disk -Number {diskNumber}");
        script.AppendLine($"if($d.BusType -ne 'USB' -or ('{id}' -and [string]$d.UniqueId -ne '{id}')){{throw 'Target USB identity changed during verification.'}}");
        script.AppendLine($"$v=@(Get-Partition -DiskNumber {diskNumber} | Get-Volume | Where-Object FileSystemLabel)");
        script.AppendLine($"$expected=@({expected})");
        script.AppendLine($"if($v.Count -ne {_partitions.Count}){{throw 'Verification found an unexpected number of formatted partitions.'}}");
        script.AppendLine("foreach($e in $expected){$item=$v|Where-Object FileSystemLabel -eq $e.Label|Select-Object -First 1;if(-not $item -or $item.FileSystem -ine $e.Fs){throw \"Verification failed for $($e.Label).\"}}");
        script.AppendLine("'OK'");
        await RunPowerShellAsync(script.ToString());
    }

    private async Task CopyFoldersAsync(IReadOnlyList<string> folders, string destination, string name, int startProgress, int endProgress)
    {
        if (folders.Count == 0)
        {
            AddActivity($"No folders selected for {name}; leaving it empty.");
            BuildProgress.Value = endProgress;
            return;
        }

        var progressRange = endProgress - startProgress;
        for (var index = 0; index < folders.Count; index++)
        {
            var folderStart = startProgress + progressRange * index / folders.Count;
            var folderEnd = startProgress + progressRange * (index + 1) / folders.Count;
            await CopySourceAsync(folders[index], destination, $"{name} folder {index + 1} of {folders.Count}", folderStart, folderEnd);
        }
    }

    private async Task CopySourceAsync(string source, string destination, string name, int startProgress, int endProgress)
    {
        BuildProgress.Value = startProgress;
        AddActivity($"Copying {name} content from {source}.");
        Log($"Copying {source} to {destination}");
        await Task.Run(() =>
        {
            var directories = new Stack<(string Source, string Target)>();
            directories.Push((source, destination));
            var filesCopied = 0;
            while (directories.Count > 0)
            {
                var current = directories.Pop();
                Directory.CreateDirectory(current.Target);
                string[] files;
                string[] childDirectories;
                try
                {
                    files = Directory.GetFiles(current.Source);
                    childDirectories = Directory.GetDirectories(current.Source);
                }
                catch (UnauthorizedAccessException)
                {
                    Dispatcher.Invoke(() => AddActivity($"Skipped protected folder: {current.Source}"));
                    continue;
                }
                catch (IOException ex)
                {
                    Dispatcher.Invoke(() => AddActivity($"Skipped unreadable folder: {current.Source} ({ex.Message})"));
                    continue;
                }

                foreach (var file in files)
                {
                    File.Copy(file, Path.Combine(current.Target, Path.GetFileName(file)), true);
                    filesCopied++;
                    if (filesCopied % 20 == 0)
                    {
                        Dispatcher.Invoke(() => BuildProgress.Value = Math.Min(endProgress - 1, BuildProgress.Value + 1));
                    }
                }
                foreach (var folder in childDirectories)
                {
                    var folderName = Path.GetFileName(folder);
                    if (folderName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                        folderName.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.Invoke(() => AddActivity($"Skipped Windows metadata folder: {folderName}"));
                        continue;
                    }

                    try
                    {
                        var attributes = File.GetAttributes(folder);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            Dispatcher.Invoke(() => AddActivity($"Skipped linked folder: {folder}"));
                            continue;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Dispatcher.Invoke(() => AddActivity($"Skipped protected folder: {folder}"));
                        continue;
                    }
                    directories.Push((folder, Path.Combine(current.Target, Path.GetFileName(folder))));
                }
            }
            Dispatcher.Invoke(() => BuildProgress.Value = endProgress);
        });
        AddActivity($"{name} content copied successfully.");
    }

    private async Task CopyUnattendAsync(string source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            AddActivity("No Autounattend.xml selected.");
            return;
        }

        AddActivity("Copying Autounattend.xml to the Win11 Boot partition root.");
        Log($"Copying {source} to {Path.Combine(destination, "Autounattend.xml")}");
        await Task.Run(() => File.Copy(source, Path.Combine(destination, "Autounattend.xml"), true));
        AddActivity("Autounattend.xml copied successfully.");
    }

    private async Task<bool> SourceIsOnDiskAsync(string source, int diskNumber)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(source));
        if (string.IsNullOrWhiteSpace(root) || root.StartsWith("\\\\")) return false;
        var letter = root[0];
        var result = await RunPowerShellAsync($"$p=Get-Partition -DriveLetter '{letter}' -ErrorAction SilentlyContinue;if($p){{$p.DiskNumber}}else{{-1}}");
        return int.TryParse(result.Trim(), out var sourceDisk) && sourceDisk == diskNumber;
    }

    private static async Task<string> RunPowerShellAsync(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Windows storage service.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Windows storage operation failed." : CleanPowerShellError(error));
        return output;
    }

    private static string CleanPowerShellError(string error)
    {
        var first = error.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first?.Replace("#< CLIXML", "").Trim() ?? "Windows storage operation failed.";
    }

    private void SetBuildingState(bool building)
    {
        _isBuilding = building;
        ConfigButton.IsEnabled = !building;
        DiskPicker.IsEnabled = !building; RefreshButton.IsEnabled = !building; SelectAllButton.IsEnabled = !building;
        WinFoldersList.IsEnabled = !building; ItFoldersList.IsEnabled = !building;
        UnattendSource.IsEnabled = !building;
        WinAddFolderButton.IsEnabled = !building; WinRemoveFolderButton.IsEnabled = !building;
        UnattendFileButton.IsEnabled = !building;
        ItAddFolderButton.IsEnabled = !building; ItRemoveFolderButton.IsEnabled = !building;
        ConfirmText.IsEnabled = !building;
        UpdateBuildButton();
    }

    private void UpdateBuildButton()
    {
        if (!IsInitialized || BuildButton is null || SummarySources is null) return;
        BuildButton.IsEnabled = !_isBuilding && DiskPicker.SelectedItems.Count > 0 && ConfirmText.Text == "ERASE";
        var folderCount = _winFolders.Count + _itFolders.Count;
        SummarySources.Text = $"{folderCount} folder(s), XML {(string.IsNullOrWhiteSpace(UnattendSource.Text) ? "not set" : "selected")}";
    }

    private void AddActivity(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        ActivityList.Items.Add(line);
        ActivityList.ScrollIntoView(line);
        Log(message);
    }

    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(_logPath)) return;
        File.AppendAllText(_logPath, $"{DateTime.Now:s}  {message}{Environment.NewLine}");
    }

    private void SetStatus(string text, string color)
    {
        HeaderStatus.Text = text;
        HeaderStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopQAUsbBuilder", "partition-settings.json");

    private List<PartitionConfig> LoadPartitionConfig()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return PartitionConfig.CreateDefaults();
            var loaded = JsonSerializer.Deserialize<List<PartitionConfig>>(File.ReadAllText(SettingsPath), _jsonOptions);
            if (loaded is null || loaded.Count is < 1 or > 6 || !loaded[^1].IsRemaining) return PartitionConfig.CreateDefaults();
            if (loaded.Any(p => !PartitionConfig.AllowedFormats.Contains(p.FileSystem))) return PartitionConfig.CreateDefaults();
            for (var i = 0; i < loaded.Count - 1; i++)
                if (loaded[i].IsRemaining || !PartitionConfig.TryParseSize(loaded[i].SizeText, out var bytes) || bytes < 32L * 1024 * 1024)
                    return PartitionConfig.CreateDefaults();
            for (var i = 0; i < loaded.Count; i++) loaded[i].Number = i + 1;
            return loaded;
        }
        catch
        {
            return PartitionConfig.CreateDefaults();
        }
    }

    private void SavePartitionConfig()
    {
        var folder = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(folder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_partitions, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ApplyPartitionConfig()
    {
        var selected = SelectedDisks();
        UpdatePartitionPreview(selected);
        WinContentTitle.Text = _partitions.Count >= 2 ? $"3. {_partitions[1].Name}" : "3. Partition 2 not configured";
        ItContentTitle.Text = _partitions.Count >= 3 ? $"4. {_partitions[2].Name}" : "4. Partition 3 not configured";
        UpdateBuildButton();
    }

    private void UpdatePartitionPreview(IReadOnlyList<UsbDisk> disks)
    {
        PartitionPreview.Children.Clear();
        PartitionPreview.ColumnDefinitions.Clear();
        PartitionPreview.RowDefinitions.Clear();
        if (disks.Count == 0)
        {
            foreach (var partition in _partitions) partition.CalculatedSizeText = null;
            return;
        }

        var fixedSize = _partitions.Take(Math.Max(0, _partitions.Count - 1))
            .Sum(p => PartitionConfig.TryParseSize(p.SizeText, out var bytes) ? bytes : 0);
        var colors = new[] { "#D8F1E5", "#DCEAF4", "#F4E8CF", "#E8DFF2", "#F2DDDC", "#D8ECEB" };
        var borderColors = new[] { "#55C98D", "#6AAED6", "#D5A84E", "#A987C5", "#D3827F", "#62B4AF" };
        var compact = disks.Count > 1;
        var rowHeight = 62d / disks.Count;

        PartitionPreview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 54 : 0) });
        PartitionPreview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var diskIndex = 0; diskIndex < disks.Count; diskIndex++)
        {
            var disk = disks[diskIndex];
            PartitionPreview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            if (compact)
            {
                var diskLabel = new TextBlock
                {
                    Text = $"Disk {disk.Number}", FontWeight = FontWeights.SemiBold,
                    FontSize = rowHeight < 18 ? 9 : 11, VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = $"Disk {disk.Number} - {disk.FriendlyName} - {FormatBytes(disk.Size)}"
                };
                Grid.SetRow(diskLabel, diskIndex);
                Grid.SetColumn(diskLabel, 0);
                PartitionPreview.Children.Add(diskLabel);
            }

            var partitionSizes = new List<long>(_partitions.Count);
            for (var i = 0; i < _partitions.Count; i++)
            {
                long size;
                if (i == _partitions.Count - 1)
                {
                    size = Math.Max(1, disk.Size - fixedSize);
                    _partitions[i].CalculatedSizeText = FormatBytes(size);
                }
                else PartitionConfig.TryParseSize(_partitions[i].SizeText, out size);
                partitionSizes.Add(Math.Max(1, size));
            }
            var totalSize = Math.Max(1d, partitionSizes.Sum(size => (double)size));
            var strip = new Grid();
            Grid.SetRow(strip, diskIndex);
            Grid.SetColumn(strip, 1);
            PartitionPreview.Children.Add(strip);
            for (var i = 0; i < _partitions.Count; i++)
            {
                strip.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(partitionSizes[i] / totalSize, GridUnitType.Star),
                    MinWidth = compact ? 34 : 90
                });
                var detailText = i == _partitions.Count - 1 ? $"{FormatBytes(partitionSizes[i])} | {_partitions[i].FileSystem}" : _partitions[i].PreviewText;
                var label = new TextBlock
                {
                    Text = compact ? _partitions[i].Name : $"{_partitions[i].Name}\n{detailText}",
                    FontWeight = FontWeights.Bold, FontSize = compact ? (rowHeight < 18 ? 8 : 10) : 12,
                    TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                };
                var tooltipContent = new StackPanel();
                tooltipContent.Children.Add(new TextBlock { Text = $"Disk {disk.Number} · {_partitions[i].Name}", FontWeight = FontWeights.Bold, FontSize = 13 });
                tooltipContent.Children.Add(new TextBlock { Text = $"Size: {FormatBytes(partitionSizes[i])}", Margin = new Thickness(0, 4, 0, 0) });
                tooltipContent.Children.Add(new TextBlock { Text = $"Format: {_partitions[i].FileSystem}", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#526970")) });
                var segment = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i % colors.Length])),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderColors[i % borderColors.Length])),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(compact ? 5 : 10),
                    Padding = compact ? new Thickness(5, 0, 5, 0) : new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(2, 1, 2, 1), Child = label,
                    ToolTip = new ToolTip { Content = tooltipContent }
                };
                ToolTipService.SetInitialShowDelay(segment, 180);
                ToolTipService.SetShowDuration(segment, 12000);
                Grid.SetColumn(segment, i);
                strip.Children.Add(segment);
            }
        }
    }

    private List<UsbDisk> DeserializeUsbDisks(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<List<UsbDisk>>(json, _jsonOptions) ?? [];
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            var disk = JsonSerializer.Deserialize<UsbDisk>(json, _jsonOptions);
            return disk is null ? [] : [disk];
        }
        return [];
    }

    private static string PsQuote(string value) => value.Replace("'", "''");
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):N2} GB"
        : $"{bytes / (1024d * 1024):N0} MB";

    private void DiskPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedDisks();
        if (selected.Count > 0)
        {
            SummaryTarget.Text = $"{selected.Count} USB drive(s) queued";
            UpdatePartitionPreview(selected);
            ConfirmText.Clear();
        }
        else
        {
            SummaryTarget.Text = "No USB selected";
            UpdatePartitionPreview([]);
        }
        SelectAllButton.Content = DiskPicker.Items.Count > 0 && selected.Count == DiskPicker.Items.Count ? "Clear All" : "Select All";
        UpdateBuildButton();
    }

    private List<UsbDisk> SelectedDisks() => DiskPicker.SelectedItems.Cast<UsbDisk>().OrderBy(d => d.Number).ToList();

    private void AddFolder(ObservableCollection<string> collection)
    {
        var dialog = new OpenFolderDialog { Title = "Select a folder whose contents will be copied", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        if (collection.Any(path => path.Equals(dialog.FolderName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("That folder is already in this list.", "Folder already added", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        collection.Add(dialog.FolderName);
        UpdateBuildButton();
    }

    private void RemoveFolder(ObservableCollection<string> collection, ListBox list)
    {
        if (list.SelectedItem is string selected) collection.Remove(selected);
        UpdateBuildButton();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || e.ClickCount != 1) return;

        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null && current != this)
        {
            if (current is Button or TextBox or ComboBox or ListBox or ScrollBar)
                return;
            current = VisualTreeHelper.GetParent(current);
        }

        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) { if (!_isBuilding) Close(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDisksAsync();
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (DiskPicker.Items.Count > 0 && DiskPicker.SelectedItems.Count == DiskPicker.Items.Count)
        {
            DiskPicker.UnselectAll();
            SelectAllButton.Content = "Select All";
        }
        else
        {
            DiskPicker.SelectAll();
            SelectAllButton.Content = "Clear All";
        }
    }
    private void ConfirmText_TextChanged(object sender, TextChangedEventArgs e) => UpdateBuildButton();
    private void Source_TextChanged(object sender, TextChangedEventArgs e) => UpdateBuildButton();
    private void Config_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfigWindow(_partitions) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _partitions = dialog.Result;
        SavePartitionConfig();
        ApplyPartitionConfig();
        ConfirmText.Clear();
        AddActivity($"Partition configuration updated: {_partitions.Count} partition(s).");
    }
    private void WinAddFolder_Click(object sender, RoutedEventArgs e) => AddFolder(_winFolders);
    private void WinRemoveFolder_Click(object sender, RoutedEventArgs e) => RemoveFolder(_winFolders, WinFoldersList);
    private void UnattendFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Autounattend.xml",
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true) UnattendSource.Text = dialog.FileName;
    }
    private void ItAddFolder_Click(object sender, RoutedEventArgs e) => AddFolder(_itFolders);
    private void ItRemoveFolder_Click(object sender, RoutedEventArgs e) => RemoveFolder(_itFolders, ItFoldersList);
}

public sealed class UsbDisk
{
    public int Number { get; set; }
    public string FriendlyName { get; set; } = "USB Disk";
    public string? SerialNumber { get; set; }
    public string? UniqueId { get; set; }
    public long Size { get; set; }
    public bool IsBoot { get; set; }
    public bool IsSystem { get; set; }
    public string DiskTitle => $"Disk {Number}";
    public string SizeDisplay => $"{Size / (1024d * 1024 * 1024):N2} GB";
    public string Display => $"Disk {Number}  |  {FriendlyName}  |  {Size / (1024d * 1024 * 1024):N2} GB";
}

public sealed class PartitionResult
{
    public string WinLetter { get; set; } = "";
    public string SupportLetter { get; set; } = "";
}
