using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Media;
using PhotoSelector.Core.Scanning;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string aiStatus = "AI queue: 128 / 192";
    private int totalPhotos = 192;
    private int pairedPhotos = 171;
    private int jpgOnlyPhotos = 16;
    private int rawOnlyPhotos = 5;
    private string projectDirectory = @"D:\Photos\2026-Trip\Day-01";
    private bool isScanning;
    private string statusMessage = "Sample project loaded.";
    private PhotoItemViewModel? selectedPhoto;

    public MainWindowViewModel()
    {
        Filters =
        [
            new("All", 192, true),
            new("AI: Keep", 42),
            new("AI: Maybe", 81),
            new("AI: Reject", 69),
            new("Unreviewed", 118),
        ];

        Photos =
        [
            new("IMG_0241", "keep", 4, "IMG_0241.JPG", "IMG_0241.CR3", "JPG+RAW", "6d927d", "Sharp subject, clean composition, natural expression. Good export candidate."),
            new("IMG_0242", "maybe", 3, "IMG_0242.JPG", "IMG_0242.CR3", "JPG+RAW", "a48569", "Pleasant light, but the subject edge is slightly soft. Needs review."),
            new("IMG_0243", "reject", 2, "IMG_0243.JPG", "IMG_0243.CR3", "JPG+RAW", "777f8c", "Underexposed with a weaker expression. Likely reject."),
            new("IMG_0244", "keep", 5, "IMG_0244.JPG", "IMG_0244.CR3", "JPG+RAW", "5b8796", "Best candidate in this burst. Expression and motion are stable."),
            new("IMG_0245", "maybe", 3, "IMG_0245.JPG", null, "Single JPG", "b09574", "JPG only. Useful preview, but there is no RAW pair for export."),
            new("IMG_0246", "reject", 1, "IMG_0246.JPG", "IMG_0246.CR3", "JPG+RAW", "8b7774", "Noticeably out of focus."),
            new("IMG_0247", "keep", 4, "IMG_0247.JPG", "IMG_0247.CR3", "JPG+RAW", "729066", "Good color and depth. Worth keeping for the shortlist."),
            new("IMG_0248", "maybe", 3, "IMG_0248.JPG", "IMG_0248.CR3", "JPG+RAW", "8d718f", "Usable framing, but the background is busy."),
            new("IMG_0249", "reject", 2, null, "IMG_0249.CR3", "Single RAW", "686f64", "RAW only with no usable paired preview."),
            new("IMG_0250", "keep", 4, "IMG_0250.JPG", "IMG_0250.CR3", "JPG+RAW", "9a7d5c", "Clear detail and a strong keeper candidate."),
            new("IMG_0251", "maybe", 3, "IMG_0251.JPG", "IMG_0251.CR3", "JPG+RAW", "5f7c89", "Needs comparison with neighboring frames."),
            new("IMG_0252", "reject", 2, "IMG_0252.JPG", "IMG_0252.CR3", "JPG+RAW", "83796f", "Duplicate frame with weaker quality."),
        ];

        SelectedPhoto = Photos[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProjectDirectory
    {
        get => projectDirectory;
        private set => SetProperty(ref projectDirectory, value);
    }

    public string AiStatus
    {
        get => aiStatus;
        private set => SetProperty(ref aiStatus, value);
    }

    public string AiProvider { get; } = "OpenAI-compatible - gpt-4.1-mini";

    public int TotalPhotos
    {
        get => totalPhotos;
        private set => SetProperty(ref totalPhotos, value);
    }

    public int PairedPhotos
    {
        get => pairedPhotos;
        private set => SetProperty(ref pairedPhotos, value);
    }

    public int JpgOnlyPhotos
    {
        get => jpgOnlyPhotos;
        private set => SetProperty(ref jpgOnlyPhotos, value);
    }

    public int RawOnlyPhotos
    {
        get => rawOnlyPhotos;
        private set => SetProperty(ref rawOnlyPhotos, value);
    }

    public int AiProgressPercent { get; } = 67;

    public bool IsScanning
    {
        get => isScanning;
        private set => SetProperty(ref isScanning, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public ObservableCollection<FilterItemViewModel> Filters { get; }

    public ObservableCollection<PhotoItemViewModel> Photos { get; }

    public PhotoItemViewModel? SelectedPhoto
    {
        get => selectedPhoto;
        set
        {
            if (selectedPhoto == value)
            {
                return;
            }

            selectedPhoto = value;
            OnPropertyChanged();
        }
    }

    public void LoadDirectory(string directory)
    {
        var result = ScanAndPersist(directory);
        LoadScannedPairs(result.SourceDirectory, result.Pairs);
        StatusMessage = $"Scanned {result.Pairs.Count} photo item(s).";
    }

    public async Task LoadDirectoryAsync(string directory)
    {
        IsScanning = true;
        StatusMessage = $"Scanning {directory}...";

        try
        {
            var result = await Task.Run(() => ScanAndPersist(directory));
            LoadScannedPairs(result.SourceDirectory, result.Pairs);
            StatusMessage = $"Scanned {result.Pairs.Count} photo item(s).";
        }
        catch (IOException ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void ReportDirectorySelectionFailed(string? message = null)
    {
        StatusMessage = message ?? "Could not open the selected directory.";
    }

    private void LoadScannedPairs(string directory, IReadOnlyList<PhotoPair> pairs)
    {
        ProjectDirectory = directory;
        TotalPhotos = pairs.Count;
        PairedPhotos = pairs.Count(pair => pair.JpegPath is not null && pair.RawPath is not null);
        JpgOnlyPhotos = pairs.Count(pair => pair.JpegPath is not null && pair.RawPath is null);
        RawOnlyPhotos = pairs.Count(pair => pair.JpegPath is null && pair.RawPath is not null);
        AiStatus = $"AI queue: 0 / {TotalPhotos}";

        Filters.Clear();
        Filters.Add(new("All", TotalPhotos, true));
        Filters.Add(new("AI: Keep", 0));
        Filters.Add(new("AI: Maybe", 0));
        Filters.Add(new("AI: Reject", 0));
        Filters.Add(new("Unreviewed", TotalPhotos));

        Photos.Clear();
        for (var index = 0; index < pairs.Count; index++)
        {
            Photos.Add(CreatePhotoItem(pairs[index], index));
        }

        SelectedPhoto = Photos.Count > 0 ? Photos[0] : null;
    }

    private static ScanResult ScanAndPersist(string directory)
    {
        var sourceDirectory = Path.GetFullPath(directory);
        var pairs = PhotoScanner.ScanDirectory(sourceDirectory);
        var databasePath = Path.Combine(sourceDirectory, ".photo-selector", "photo-selector.db");

        using var database = ProjectDatabase.Open(databasePath);
        database.Migrate();
        var existingProject = database
            .ListProjects()
            .FirstOrDefault(project =>
                string.Equals(project.SourceDirectory, sourceDirectory, StringComparison.OrdinalIgnoreCase));
        var projectId = existingProject?.Id ?? database.CreateProject(sourceDirectory);
        database.ReplacePhotos(projectId, pairs);

        return new ScanResult(sourceDirectory, pairs);
    }

    private static PhotoItemViewModel CreatePhotoItem(PhotoPair pair, int index)
    {
        return new PhotoItemViewModel(
            pair.BaseName,
            "unreviewed",
            0,
            Path.GetFileName(pair.JpegPath),
            Path.GetFileName(pair.RawPath),
            GetPairStatus(pair),
            PickTone(index),
            "Not scored yet.");
    }

    private static string GetPairStatus(PhotoPair pair)
    {
        return (pair.JpegPath, pair.RawPath) switch
        {
            (not null, not null) => "JPG+RAW",
            (not null, null) => "JPG only",
            (null, not null) => "RAW only",
            _ => "Unsupported",
        };
    }

    private static string PickTone(int index)
    {
        string[] tones = ["6d927d", "a48569", "777f8c", "5b8796", "b09574", "729066"];
        return tones[index % tones.Length];
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record ScanResult(string SourceDirectory, IReadOnlyList<PhotoPair> Pairs);
}

public sealed record FilterItemViewModel(string Name, int Count, bool IsActive = false)
{
    public IBrush BackgroundBrush => IsActive ? SolidColorBrush.Parse("#E2ECE8") : Brushes.Transparent;
}

public sealed record PhotoItemViewModel(
    string Name,
    string AiCategory,
    int AiScore,
    string? JpgFileName,
    string? RawFileName,
    string PairStatus,
    string Tone,
    string AiReason)
{
    public bool HasPair => JpgFileName is not null && RawFileName is not null;

    public string JpgFileLabel => JpgFileName ?? "No JPG file";

    public string RawFileLabel => RawFileName ?? "No RAW file";

    public string PairFileSummary => HasPair ? $"{JpgFileName} + {RawFileName}" : PairStatus;

    public string AiScoreSummary => $"{AiScore} / 5 - {AiCategory}";

    public string UserDecision => "Not reviewed";

    public string Stars => new string('*', AiScore).PadRight(5, '-');

    public IBrush ToneBrush => SolidColorBrush.Parse($"#{Tone}");

    public IBrush CategoryBrush => AiCategory switch
    {
        "keep" => SolidColorBrush.Parse("#197a52"),
        "maybe" => SolidColorBrush.Parse("#9b6a18"),
        "reject" => SolidColorBrush.Parse("#9d3636"),
        _ => SolidColorBrush.Parse("#485550"),
    };
}
