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
    private bool _isBuilding;
    private bool _updatingPartitionGrid;
    private PartitionConfig? _draggedPartition;
    private Point _partitionDragStart;
    private double _partitionDragStartDistance = 12;
    private DropIndicatorAdorner? _mainDropIndicator;
    private int _mainDropDestinationIndex = -1;
    private string? _logPath;
    private List<PartitionConfig> _partitions = [];
    private List<PartitionConfig> _defaultPartitions = [];
    private AppPreferences _preferences = new();
    private static readonly string VersionLabel = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.4.35"}";
    private const string MainPartitionDragFormat = "LaptopQaUsbBuilder.MainPartition";

    public MainWindow()
    {
        InitializeComponent();
        _preferences = LoadPreferences();
        Localization.ApplyCulture(_preferences.Language);
        _defaultPartitions = LoadPartitionConfig();
        _partitions = _defaultPartitions.Select(p => p.Clone()).ToList();
        MainPartitionList.ItemsSource = _partitions;
        ApplyPartitionConfig();
        ApplyLanguage();
        ThemeService.Apply(this, _preferences.Theme);
        Loaded += (_, _) => ThemeService.Apply(this, _preferences.Theme);
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
        if (!ValidatePartitionLayout(out var layoutError))
        {
            MessageBox.Show(layoutError, "Invalid partition settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var requiredSize = _partitions.Where(p => !p.IsRemaining).Sum(p => PartitionConfig.TryParseSize(p.SizeText, out var bytes) ? bytes : 0) + 64L * 1024 * 1024;
        var tooSmall = queuedDisks.Where(d => d.Size < requiredSize).ToList();
        if (tooSmall.Count > 0)
        {
            MessageBox.Show($"These drives are too small for the configured layout (minimum {FormatBytes(requiredSize)}):\n\n{string.Join("\n", tooSmall.Select(d => $"Disk {d.Number} - {d.FriendlyName} ({FormatBytes(d.Size)})"))}",
                "Drive too small", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var fixedSize = _partitions.Where(p => !p.IsRemaining).Sum(p => PartitionConfig.TryParseSize(p.SizeText, out var bytes) ? bytes : 0);
        var remainingPartition = _partitions.Single(p => p.IsRemaining);
        var fat32TooLarge = queuedDisks.Where(d => remainingPartition.FileSystem == "FAT32" && d.Size - fixedSize > 32L * 1024 * 1024 * 1024).ToList();
        if (fat32TooLarge.Count > 0)
        {
            MessageBox.Show("The remaining-space partition would exceed Windows' 32 GB FAT32 formatting limit. Choose NTFS or exFAT for that partition, or increase the fixed partitions.",
                "FAT32 partition too large", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folderSources = _partitions.SelectMany(p => p.SourceFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var fileSources = _partitions.SelectMany(p => p.SourceFiles.Concat(string.IsNullOrWhiteSpace(p.AutounattendSource) ? [] : [p.AutounattendSource]))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
        foreach (var source in fileSources)
        {
            if (!File.Exists(source))
            {
                MessageBox.Show($"Source file not found:\n{source}", "Missing source", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            foreach (var disk in queuedDisks)
                if (await SourceIsOnDiskAsync(source, disk.Number))
                {
                    MessageBox.Show($"A copy source is stored on queued Disk {disk.Number} and would be erased before it could be copied:\n{source}",
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
                var copyPartitions = _partitions.Select((partition, index) => (partition, index))
                    .Where(item => item.partition.SourceFolders.Count + item.partition.SourceFiles.Count > 0).ToList();
                for (var copyIndex = 0; copyIndex < copyPartitions.Count; copyIndex++)
                {
                    var (partition, partitionIndex) = copyPartitions[copyIndex];
                    if (partitionIndex >= result.Letters.Count || string.IsNullOrWhiteSpace(result.Letters[partitionIndex]))
                        throw new InvalidOperationException($"Windows did not assign a drive letter to {partition.Name}.");
                    var start = 35 + 60 * copyIndex / Math.Max(1, copyPartitions.Count);
                    var end = 35 + 60 * (copyIndex + 1) / Math.Max(1, copyPartitions.Count);
                    await CopyPartitionSourcesAsync(partition, $"{result.Letters[partitionIndex]}:\\", start, end);
                }
                if (copyPartitions.Count == 0) BuildProgress.Value = 95;
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
            string sizeArgument;
            if (item.IsRemaining && index == _partitions.Count - 1)
            {
                sizeArgument = "-UseMaximumSize";
            }
            else if (item.IsRemaining)
            {
                var reservedAfter = _partitions.Skip(index + 1).Sum(p => PartitionConfig.TryParseSize(p.SizeText, out var bytes) ? bytes : 0);
                script.AppendLine($"$remainingSize=[math]::Floor(((Get-Disk -Number {disk.Number}).LargestFreeExtent-{reservedAfter})/1MB)*1MB");
                script.AppendLine("if($remainingSize -lt 32MB){throw 'The remaining-space partition would be smaller than 32 MB.'}");
                sizeArgument = "-Size $remainingSize";
            }
            else
            {
                sizeArgument = PartitionConfig.TryParseSize(item.SizeText, out var sizeBytes)
                    ? $"-Size {sizeBytes}"
                    : throw new InvalidOperationException($"Invalid size for partition {index + 1}.");
            }
            script.AppendLine($"${variable}=New-Partition -DiskNumber {disk.Number} {sizeArgument} -AssignDriveLetter");
            var allocation = item.FileSystem == "NTFS" ? " -AllocationUnitSize 4096" : "";
            script.AppendLine($"${variable} | Format-Volume -FileSystem {item.FileSystem} -NewFileSystemLabel '{PsQuote(item.Name)}'{allocation} -Confirm:$false -Force | Out-Null");
        }

        var letterExpressions = string.Join(",", Enumerable.Range(1, _partitions.Count).Select(number => $"[string](($p{number}|Get-Volume).DriveLetter)"));
        script.AppendLine($"[pscustomobject]@{{Letters=@({letterExpressions})}} | ConvertTo-Json -Compress");
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

    private async Task CopyPartitionSourcesAsync(PartitionConfig partition, string destination, int startProgress, int endProgress)
    {
        var sources = partition.SourceFolders.Select(path => (Path: path, IsFolder: true, TargetName: (string?)null))
            .Concat(partition.SourceFiles.Select(path => (Path: path, IsFolder: false, TargetName: (string?)Path.GetFileName(path))))
            .Concat(partition.FileSystem == "NTFS" && !string.IsNullOrWhiteSpace(partition.AutounattendSource)
                ? [(Path: partition.AutounattendSource!, IsFolder: false, TargetName: (string?)"Autounattend.xml")]
                : []).ToList();
        if (sources.Count == 0) { BuildProgress.Value = endProgress; return; }

        for (var index = 0; index < sources.Count; index++)
        {
            var sourceStart = startProgress + (endProgress - startProgress) * index / sources.Count;
            var sourceEnd = startProgress + (endProgress - startProgress) * (index + 1) / sources.Count;
            var source = sources[index];
            if (source.IsFolder)
            {
                await CopySourceAsync(source.Path, destination, $"{partition.Name} folder {index + 1} of {sources.Count}", sourceStart, sourceEnd);
            }
            else
            {
                BuildProgress.Value = sourceStart;
                var target = Path.Combine(destination, source.TargetName ?? Path.GetFileName(source.Path));
                AddActivity($"Copying file {source.TargetName ?? Path.GetFileName(source.Path)} to {partition.Name}.");
                Log($"Copying {source.Path} to {target}");
                await Task.Run(() => File.Copy(source.Path, target, true));
                BuildProgress.Value = sourceEnd;
            }
        }
        AddActivity($"Selected content copied to {partition.Name}.");
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
        DiskPicker.IsEnabled = !building; RefreshButton.IsEnabled = !building;
        MainPartitionList.IsEnabled = !building; AddPartitionButton.IsEnabled = !building; MainDefaultsButton.IsEnabled = !building;
        ConfirmText.IsEnabled = !building;
        UpdateBuildButton();
    }

    private void UpdateBuildButton()
    {
        if (!IsInitialized || BuildButton is null) return;
        BuildButton.IsEnabled = !_isBuilding && DiskPicker.SelectedItems.Count > 0 && ConfirmText.Text == "ERASE";
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

    private void ApplyLanguage()
    {
        string T(string key) => Localization.Text(_preferences.Language, key);
        SubtitleText.Text = T("Subtitle"); SelectDriveTitle.Text = $"1. {T("Select USB Drive")}";
        RefreshButton.Content = T("Refresh");
        PartitionEditorTitle.Text = T("Partition Settings"); MainDefaultsButton.Content = "Defaults"; PartitionLayoutTitle.Text = T("Partition Layout"); PartitionLayoutNote.Text = T("GPT Note");
        WarningText.Text = T("Warning"); ActivityTitle.Text = T("Activity"); ConfirmLabel.Text = T("Confirm ERASE"); BuildButton.Content = T("Build USB Queue");
        if (!_isBuilding) HeaderStatus.Text = T("Ready");
    }

    private static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopQAUsbBuilder", "default-partition-settings.json");
    private static string PreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopQAUsbBuilder", "preferences.json");

    private AppPreferences LoadPreferences()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return new AppPreferences();
            var result = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(PreferencesPath), _jsonOptions) ?? new AppPreferences();
            result.Language = Localization.Resolve(result.Language).Code; result.Theme = ThemeService.Normalize(result.Theme);
            return result;
        }
        catch { return new AppPreferences(); }
    }

    private void SavePreferences()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(_preferences, new JsonSerializerOptions { WriteIndented = true }));
    }

    private List<PartitionConfig> LoadPartitionConfig()
    {
        try
        {
            if (!File.Exists(DefaultSettingsPath)) return PartitionConfig.CreateDefaults();
            var loaded = JsonSerializer.Deserialize<List<PartitionConfig>>(File.ReadAllText(DefaultSettingsPath), _jsonOptions);
            if (loaded is not null)
                foreach (var partition in loaded)
                    if (partition.SizeText.Trim().Equals("Remaining", StringComparison.OrdinalIgnoreCase)) partition.SizeText = "*";
            if (loaded is null || loaded.Count is < 1 or > 6 || loaded.Count(p => p.IsRemaining) != 1) return PartitionConfig.CreateDefaults();
            if (loaded.Any(p => !PartitionConfig.AllowedFormats.Contains(p.FileSystem))) return PartitionConfig.CreateDefaults();
            for (var i = 0; i < loaded.Count; i++)
                if (!loaded[i].IsRemaining && (!PartitionConfig.TryParseSize(loaded[i].SizeText, out var bytes) || bytes < 32L * 1024 * 1024))
                    return PartitionConfig.CreateDefaults();
            for (var i = 0; i < loaded.Count; i++) loaded[i].Number = i + 1;
            return loaded;
        }
        catch
        {
            return PartitionConfig.CreateDefaults();
        }
    }

    private void SaveDefaultPartitionConfig()
    {
        var folder = Path.GetDirectoryName(DefaultSettingsPath)!;
        Directory.CreateDirectory(folder);
        File.WriteAllText(DefaultSettingsPath, JsonSerializer.Serialize(_defaultPartitions, new JsonSerializerOptions { WriteIndented = true }));
    }

    private bool ValidatePartitionLayout(out string message)
    {
        message = "";
        if (_partitions.Count is < 1 or > 6) { message = "Choose between 1 and 6 partitions."; return false; }
        if (_partitions.Count(p => p.IsRemaining) != 1) { message = "Exactly one partition must use * for remaining space."; return false; }
        if (_partitions.Select(p => p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _partitions.Count)
        { message = "Every volume label must be unique."; return false; }
        foreach (var item in _partitions)
        {
            item.Name = item.Name.Trim();
            if (string.IsNullOrWhiteSpace(item.Name)) { message = $"Partition {item.Number} needs a volume label."; return false; }
            if (item.Name.IndexOfAny(['\\', '/', '?', '*', ':', '|', '"', '<', '>']) >= 0 || item.Name.Any(char.IsControl))
            { message = $"Partition {item.Number} contains a character that is not valid in a volume label."; return false; }
            if (!PartitionConfig.AllowedFormats.Contains(item.FileSystem)) { message = $"Partition {item.Number} has an unsupported format."; return false; }
            var maxLength = item.FileSystem == "FAT32" ? 11 : item.FileSystem == "exFAT" ? 15 : 32;
            if (item.Name.Length > maxLength) { message = $"{item.FileSystem} label '{item.Name}' exceeds {maxLength} characters."; return false; }
            if (item.IsRemaining) continue;
            if (!PartitionConfig.TryParseSize(item.SizeText, out var bytes)) { message = $"Partition {item.Number} needs a size such as 50 MB or 20 GB, or * for remaining space."; return false; }
            if (bytes < 32L * 1024 * 1024) { message = $"Partition {item.Number} must be at least 32 MB."; return false; }
            if (item.FileSystem == "FAT32" && bytes > 32L * 1024 * 1024 * 1024) { message = $"Partition {item.Number} exceeds Windows' 32 GB FAT32 formatting limit."; return false; }
        }
        return true;
    }

    private void PartitionConfigurationChanged(bool refreshList = true)
    {
        for (var index = 0; index < _partitions.Count; index++) _partitions[index].Number = index + 1;
        if (refreshList) MainPartitionList.Items.Refresh();
        UpdatePartitionPreview(SelectedDisks());
        UpdateBuildButton();
        ConfirmText.Clear();
    }

    private void QueuePartitionConfigurationChanged()
    {
        if (_updatingPartitionGrid || _isBuilding) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_updatingPartitionGrid) return;
            _updatingPartitionGrid = true;
            try { PartitionConfigurationChanged(false); }
            finally { _updatingPartitionGrid = false; }
        }));
    }

    private void PartitionField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) QueuePartitionConfigurationChanged();
    }

    private void PartitionFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PartitionConfig partition && partition.FileSystem != "NTFS")
            partition.AutounattendSource = null;
        if (IsLoaded) QueuePartitionConfigurationChanged();
    }

    private void PartitionDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isBuilding || (sender as FrameworkElement)?.DataContext is not PartitionConfig partition) return;
        _draggedPartition = partition;
        _mainDropDestinationIndex = _partitions.IndexOf(partition);
        _partitionDragStart = e.GetPosition(this);
        var row = FindVisualAncestor<ListBoxItem>(sender as DependencyObject);
        _partitionDragStartDistance = Math.Max(12, row is null
            ? 12
            : ((FrameworkElement?)FindVisualDescendant<Border>(row, "PartitionRowCard") ?? row).ActualHeight);
        e.Handled = true;
    }

    private void MainPartitionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedPartition = null;
        _mainDropDestinationIndex = -1;
        ClearMainDropIndicator();
    }

    private void MainPartitionList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedPartition is null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _draggedPartition = null;
            return;
        }
        var position = e.GetPosition(this);
        var deltaX = position.X - _partitionDragStart.X;
        var deltaY = position.Y - _partitionDragStart.Y;
        if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < _partitionDragStartDistance) return;
        var data = new DataObject(MainPartitionDragFormat, _draggedPartition);
        DragDrop.DoDragDrop(MainPartitionList, data, DragDropEffects.Move);
        ClearMainDropIndicator();
        _draggedPartition = null;
        _mainDropDestinationIndex = -1;
        e.Handled = true;
    }

    private void MainPartitionList_DragOver(object sender, DragEventArgs e)
    {
        ListBoxItem? row = null;
        var showAfter = false;
        var dragged = e.Data.GetData(MainPartitionDragFormat) as PartitionConfig;
        var valid = !_isBuilding && dragged is not null;
        if (valid) valid = TryGetMainDropTarget(e, dragged!, out row, out showAfter, out _);
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        if (valid) ShowMainDropIndicator(row!, showAfter); else ClearMainDropIndicator();
        e.Handled = true;
    }

    private void MainPartitionList_DragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(MainPartitionList);
        if (point.X < 0 || point.Y < 0 || point.X > MainPartitionList.ActualWidth || point.Y > MainPartitionList.ActualHeight)
            ClearMainDropIndicator();
    }

    private void MainPartitionList_Drop(object sender, DragEventArgs e)
    {
        ClearMainDropIndicator();
        if (_isBuilding || e.Data.GetData(MainPartitionDragFormat) is not PartitionConfig dragged) return;
        var oldIndex = _partitions.IndexOf(dragged);
        if (oldIndex < 0 || !TryGetMainDropTarget(e, dragged, out _, out _, out var destinationIndex)) return;
        if (destinationIndex == oldIndex)
        {
            MainPartitionList.SelectedItem = dragged;
            _draggedPartition = null;
            e.Handled = true;
            return;
        }
        _partitions.RemoveAt(oldIndex);
        _partitions.Insert(Math.Clamp(destinationIndex, 0, _partitions.Count), dragged);
        PartitionConfigurationChanged();
        MainPartitionList.SelectedItem = dragged;
        _draggedPartition = null;
        e.Handled = true;
    }

    private bool TryGetMainDropTarget(DragEventArgs e, PartitionConfig dragged, out ListBoxItem? row, out bool showAfter, out int destinationIndex)
    {
        destinationIndex = GetMainDestinationIndex(e.GetPosition(MainPartitionList).Y);
        showAfter = false;
        row = MainPartitionList.ItemContainerGenerator.ContainerFromIndex(destinationIndex) as ListBoxItem;
        return row is not null;
    }

    private int GetMainDestinationIndex(double pointerY)
    {
        var destination = Math.Max(0, MainPartitionList.Items.Count - 1);
        ListBoxItem? destinationRow = null;
        for (var index = 0; index < MainPartitionList.Items.Count; index++)
        {
            if (MainPartitionList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem row) continue;
            var top = row.TranslatePoint(new Point(0, 0), MainPartitionList).Y;
            if (pointerY >= top + row.ActualHeight) continue;
            destination = index;
            destinationRow = row;
            break;
        }
        if (_mainDropDestinationIndex >= 0 && destination != _mainDropDestinationIndex && destinationRow is not null)
        {
            var top = destinationRow.TranslatePoint(new Point(0, 0), MainPartitionList).Y;
            var depth = pointerY - top;
            if (depth < destinationRow.ActualHeight * 0.25 || depth > destinationRow.ActualHeight * 0.75)
                return _mainDropDestinationIndex;
        }
        _mainDropDestinationIndex = destination;
        return destination;
    }

    private void ShowMainDropIndicator(ListBoxItem row, bool showAfter)
    {
        UIElement targetBox = (UIElement?)FindVisualDescendant<Border>(row, "PartitionRowCard") ?? row;
        if (_mainDropIndicator?.AdornedElement == targetBox && _mainDropIndicator.IsAfter == showAfter) return;
        _mainDropIndicator?.Detach();
        _mainDropIndicator = DropIndicatorAdorner.Attach(targetBox, showAfter);
    }

    private void ClearMainDropIndicator()
    {
        _mainDropIndicator?.Detach();
        _mainDropIndicator = null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && match.Name == name) return match;
            var nested = FindVisualDescendant<T>(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void ApplyPartitionConfig()
    {
        var selected = SelectedDisks();
        MainPartitionList.ItemsSource = _partitions;
        MainPartitionList.Items.Refresh();
        UpdatePartitionPreview(selected);
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

        var fixedSize = _partitions.Where(p => !p.IsRemaining)
            .Sum(p => PartitionConfig.TryParseSize(p.SizeText, out var bytes) ? bytes : 0);
        var rowHeight = Math.Max(1, PartitionPreview.ActualHeight) / disks.Count;
        var showDiskLabel = disks.Count > 1;
        var compact = rowHeight < 48;

        PartitionPreview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(showDiskLabel ? 54 : 0) });
        PartitionPreview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var diskIndex = 0; diskIndex < disks.Count; diskIndex++)
        {
            var disk = disks[diskIndex];
            PartitionPreview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            if (showDiskLabel)
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
                if (_partitions[i].IsRemaining)
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
                var detailText = _partitions[i].IsRemaining ? $"{FormatBytes(partitionSizes[i])} | {_partitions[i].FileSystem}" : _partitions[i].PreviewText;
                var label = new TextBlock
                {
                    Text = compact ? _partitions[i].Name : $"{_partitions[i].Name}\n{detailText}",
                    FontWeight = FontWeights.Bold, FontSize = compact ? (rowHeight < 18 ? 8 : 10) : 12,
                    TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "PartitionText");
                var tooltipContent = new StackPanel();
                tooltipContent.Children.Add(new TextBlock { Text = $"Disk {disk.Number} · {_partitions[i].Name}", FontWeight = FontWeights.Bold, FontSize = 13 });
                tooltipContent.Children.Add(new TextBlock { Text = $"Size: {FormatBytes(partitionSizes[i])}", Margin = new Thickness(0, 4, 0, 0) });
                tooltipContent.Children.Add(new TextBlock { Text = $"Format: {_partitions[i].FileSystem}", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#526970")) });
                var segment = new Border
                {
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(compact ? 5 : 10),
                    Padding = compact ? new Thickness(5, 0, 5, 0) : new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(2, 1, 2, 1), Child = label,
                    ToolTip = new ToolTip { Content = tooltipContent }
                };
                segment.SetResourceReference(Border.BackgroundProperty, $"PartitionBackground{i % 6}");
                segment.SetResourceReference(Border.BorderBrushProperty, $"PartitionBorder{i % 6}");
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
            UpdatePartitionPreview(selected);
            ConfirmText.Clear();
        }
        else
        {
            UpdatePartitionPreview([]);
        }
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
    private void ConfirmText_TextChanged(object sender, TextChangedEventArgs e) => UpdateBuildButton();
    private void Source_TextChanged(object sender, TextChangedEventArgs e) => UpdateBuildButton();
    private void Config_Click(object sender, RoutedEventArgs e)
    {
        var originalLanguage = _preferences.Language;
        var dialog = new ConfigWindow(_defaultPartitions, _preferences.Language, _preferences.Theme) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            Localization.ApplyCulture(originalLanguage);
            return;
        }
        _defaultPartitions = dialog.Result.Select(p => p.Clone()).ToList();
        _preferences.Language = dialog.SelectedLanguage;
        _preferences.Theme = dialog.SelectedTheme;
        SaveDefaultPartitionConfig();
        SavePreferences();
        Localization.ApplyCulture(_preferences.Language);
        ApplyLanguage();
        ThemeService.Apply(this, _preferences.Theme);
        AddActivity($"Default partition layout updated: {_defaultPartitions.Count} partition(s).");
    }
    private void AddPartition_Click(object sender, RoutedEventArgs e)
    {
        if (_partitions.Count >= 6) { MessageBox.Show("A maximum of six partitions is supported.", "Partition limit", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        _partitions.Add(new PartitionConfig
        {
            Number = _partitions.Count + 1,
            Name = $"PARTITION {_partitions.Count + 1}",
            SizeText = _partitions.Any(p => p.IsRemaining) ? "10 GB" : "*",
            FileSystem = "exFAT"
        });
        PartitionConfigurationChanged();
    }

    private void MainDefaults_Click(object sender, RoutedEventArgs e)
    {
        var hasSources = _partitions.Any(p => p.SourceFiles.Count + p.SourceFolders.Count > 0 || !string.IsNullOrWhiteSpace(p.AutounattendSource));
        if (hasSources && MessageBox.Show("Restore the configured default partitions and clear the current content selections?", "Restore defaults", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _partitions = _defaultPartitions.Select(p => p.Clone()).ToList();
        MainPartitionList.ItemsSource = _partitions;
        PartitionConfigurationChanged();
        AddActivity("Default partition layout restored.");
    }

    private void RemovePartition_Click(object sender, RoutedEventArgs e)
    {
        if (_partitions.Count <= 1) { MessageBox.Show("At least one partition is required.", "Partition required", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var item = (sender as FrameworkElement)?.DataContext as PartitionConfig ?? MainPartitionList.SelectedItem as PartitionConfig ?? _partitions[^1];
        if ((item.SourceFiles.Count + item.SourceFolders.Count > 0 || !string.IsNullOrWhiteSpace(item.AutounattendSource)) && MessageBox.Show($"Remove {item.Name} and its selected content list?", "Remove partition", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _partitions.Remove(item);
        if (!_partitions.Any(p => p.IsRemaining)) _partitions[^1].SizeText = "*";
        PartitionConfigurationChanged();
    }

    private void PartitionFiles_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PartitionConfig partition) return;
        var dialog = new OpenFileDialog { Title = $"Select files for {partition.Name}", Filter = "All files (*.*)|*.*", CheckFileExists = true, Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var path in dialog.FileNames)
            if (!partition.SourceFiles.Any(existing => existing.Equals(path, StringComparison.OrdinalIgnoreCase))) partition.SourceFiles.Add(path);
        MainPartitionList.Items.Refresh(); UpdateBuildButton();
    }

    private void PartitionFolders_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PartitionConfig partition) return;
        var dialog = new OpenFolderDialog { Title = $"Select a folder for {partition.Name}", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        if (!partition.SourceFolders.Any(existing => existing.Equals(dialog.FolderName, StringComparison.OrdinalIgnoreCase))) partition.SourceFolders.Add(dialog.FolderName);
        MainPartitionList.Items.Refresh(); UpdateBuildButton();
    }

    private void PartitionAutounattend_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PartitionConfig partition || partition.FileSystem != "NTFS") return;
        var dialog = new OpenFileDialog { Title = $"Select Autounattend.xml for {partition.Name}", Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        partition.AutounattendSource = dialog.FileName;
        MainPartitionList.Items.Refresh(); UpdateBuildButton();
    }

    private void PartitionSourcesClear_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PartitionConfig partition || (partition.SourceFiles.Count + partition.SourceFolders.Count == 0 && string.IsNullOrWhiteSpace(partition.AutounattendSource))) return;
        if (MessageBox.Show($"Clear all selected files and folders for {partition.Name}?", "Clear partition content", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        partition.SourceFiles.Clear(); partition.SourceFolders.Clear(); partition.AutounattendSource = null; MainPartitionList.Items.Refresh(); UpdateBuildButton();
    }
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
    public List<string> Letters { get; set; } = [];
}
