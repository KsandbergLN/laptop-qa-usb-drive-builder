using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LaptopQaUsbBuilder;

public partial class ConfigWindow : Window
{
    private readonly ObservableCollection<PartitionConfig> _items;
    private bool _updatingCount;
    public List<PartitionConfig> Result { get; private set; } = [];

    public ConfigWindow(IEnumerable<PartitionConfig> current)
    {
        InitializeComponent();
        _items = new ObservableCollection<PartitionConfig>(current.Select(p => p.Clone()));
        PartitionGrid.ItemsSource = _items;
        FormatColumn.ItemsSource = PartitionConfig.AllowedFormats;
        CountPicker.ItemsSource = Enumerable.Range(1, 6);
        CountPicker.SelectedItem = _items.Count;
    }

    private void CountPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCount || CountPicker.SelectedItem is not int count || _items is null) return;
        ResizeTo(count);
    }

    private void ResizeTo(int count)
    {
        while (_items.Count > count) _items.RemoveAt(_items.Count - 1);
        while (_items.Count < count)
        {
            if (_items.Count > 0 && _items[^1].IsRemaining) _items[^1].SizeText = "10 GB";
            _items.Add(new PartitionConfig { Number = _items.Count + 1, Name = $"PARTITION {_items.Count + 1}", SizeText = "Remaining", FileSystem = "exFAT" });
        }
        for (var i = 0; i < _items.Count; i++) _items[i].Number = i + 1;
        if (_items.Count > 0) _items[^1].SizeText = "Remaining";
        PartitionGrid.Items.Refresh();
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        foreach (var item in PartitionConfig.CreateDefaults()) _items.Add(item);
        _updatingCount = true;
        CountPicker.SelectedItem = _items.Count;
        _updatingCount = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        PartitionGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PartitionGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!Validate(out var message))
        {
            MessageBox.Show(message, "Invalid partition settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = _items.Select(p => p.Clone()).ToList();
        DialogResult = true;
    }

    private bool Validate(out string message)
    {
        message = "";
        if (_items.Count is < 1 or > 6) { message = "Choose between 1 and 6 partitions."; return false; }
        if (_items.Take(_items.Count - 1).Any(p => p.IsRemaining) || !_items[^1].IsRemaining)
        { message = "Only the final partition can use Remaining space."; return false; }
        if (_items.Select(p => p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _items.Count)
        { message = "Every volume label must be unique."; return false; }

        foreach (var item in _items)
        {
            item.Name = item.Name.Trim();
            if (string.IsNullOrWhiteSpace(item.Name)) { message = $"Partition {item.Number} needs a volume label."; return false; }
            if (item.Name.IndexOfAny(['\\', '/', '?', '*', ':', '|', '"', '<', '>']) >= 0 || item.Name.Any(char.IsControl))
            { message = $"Partition {item.Number} contains a character that is not valid in a volume label."; return false; }
            if (!PartitionConfig.AllowedFormats.Contains(item.FileSystem)) { message = $"Partition {item.Number} has an unsupported format."; return false; }
            var maxLength = item.FileSystem == "FAT32" ? 11 : item.FileSystem == "exFAT" ? 15 : 32;
            if (item.Name.Length > maxLength) { message = $"{item.FileSystem} label '{item.Name}' exceeds {maxLength} characters."; return false; }
            if (item.Number < _items.Count)
            {
                if (!PartitionConfig.TryParseSize(item.SizeText, out var bytes))
                { message = $"Partition {item.Number} needs a size such as 50 MB or 20 GB."; return false; }
                if (bytes < 32L * 1024 * 1024)
                { message = $"Partition {item.Number} must be at least 32 MB."; return false; }
                if (item.FileSystem == "FAT32" && bytes > 32L * 1024 * 1024 * 1024)
                { message = $"Partition {item.Number} exceeds Windows' 32 GB FAT32 formatting limit."; return false; }
            }
        }
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

public sealed class PartitionConfig
{
    public static readonly string[] AllowedFormats = ["FAT32", "NTFS", "exFAT"];
    public int Number { get; set; }
    public string Name { get; set; } = "PARTITION";
    public string SizeText { get; set; } = "Remaining";
    public string FileSystem { get; set; } = "exFAT";
    [JsonIgnore]
    public string? CalculatedSizeText { get; set; }
    public bool IsRemaining => SizeText.Trim().Equals("Remaining", StringComparison.OrdinalIgnoreCase);
    public string PreviewText => $"{CalculatedSizeText ?? SizeText}  |  {FileSystem}";
    public PartitionConfig Clone() => new() { Number = Number, Name = Name, SizeText = SizeText, FileSystem = FileSystem };

    public static List<PartitionConfig> CreateDefaults() =>
    [
        new() { Number = 1, Name = "DELL DIAG", SizeText = "50 MB", FileSystem = "FAT32" },
        new() { Number = 2, Name = "Win11 Boot", SizeText = "20 GB", FileSystem = "NTFS" },
        new() { Number = 3, Name = "IT SUPP", SizeText = "Remaining", FileSystem = "exFAT" }
    ];

    public static bool TryParseSize(string text, out long bytes)
    {
        bytes = 0;
        var match = Regex.Match(text.Trim(), @"^(\d+(?:\.\d+)?)\s*(MB|GB)$", RegexOptions.IgnoreCase);
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, out var value) || value <= 0) return false;
        var multiplier = match.Groups[2].Value.Equals("GB", StringComparison.OrdinalIgnoreCase) ? 1024m * 1024 * 1024 : 1024m * 1024;
        if (value * multiplier > long.MaxValue) return false;
        bytes = (long)(value * multiplier);
        return true;
    }
}
