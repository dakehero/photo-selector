using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace PhotoSelector.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
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
            new("IMG_0241", "keep", 4, true, "JPG+RAW", "6d927d", "Sharp subject, clean composition, natural expression. Good export candidate."),
            new("IMG_0242", "maybe", 3, true, "JPG+RAW", "a48569", "Pleasant light, but the subject edge is slightly soft. Needs review."),
            new("IMG_0243", "reject", 2, true, "JPG+RAW", "777f8c", "Underexposed with a weaker expression. Likely reject."),
            new("IMG_0244", "keep", 5, true, "JPG+RAW", "5b8796", "Best candidate in this burst. Expression and motion are stable."),
            new("IMG_0245", "maybe", 3, false, "Single JPG", "b09574", "JPG only. Useful preview, but there is no RAW pair for export."),
            new("IMG_0246", "reject", 1, true, "JPG+RAW", "8b7774", "Noticeably out of focus."),
            new("IMG_0247", "keep", 4, true, "JPG+RAW", "729066", "Good color and depth. Worth keeping for the shortlist."),
            new("IMG_0248", "maybe", 3, true, "JPG+RAW", "8d718f", "Usable framing, but the background is busy."),
            new("IMG_0249", "reject", 2, false, "Single RAW", "686f64", "RAW only with no usable paired preview."),
            new("IMG_0250", "keep", 4, true, "JPG+RAW", "9a7d5c", "Clear detail and a strong keeper candidate."),
            new("IMG_0251", "maybe", 3, true, "JPG+RAW", "5f7c89", "Needs comparison with neighboring frames."),
            new("IMG_0252", "reject", 2, true, "JPG+RAW", "83796f", "Duplicate frame with weaker quality."),
        ];

        SelectedPhoto = Photos[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProjectDirectory { get; } = @"D:\Photos\2026-Trip\Day-01";

    public string AiStatus { get; } = "AI queue: 128 / 192";

    public string AiProvider { get; } = "OpenAI-compatible - gpt-4.1-mini";

    public int TotalPhotos { get; } = 192;

    public int PairedPhotos { get; } = 171;

    public int JpgOnlyPhotos { get; } = 16;

    public int RawOnlyPhotos { get; } = 5;

    public int AiProgressPercent { get; } = 67;

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record FilterItemViewModel(string Name, int Count, bool IsActive = false)
{
    public IBrush BackgroundBrush => IsActive ? SolidColorBrush.Parse("#E2ECE8") : Brushes.Transparent;
}

public sealed record PhotoItemViewModel(
    string Name,
    string AiCategory,
    int AiScore,
    bool HasPair,
    string PairStatus,
    string Tone,
    string AiReason)
{
    public string JpgFileName => $"{Name}.JPG";

    public string RawFileName => HasPair ? $"{Name}.CR3" : "No matching RAW found";

    public string PairFileSummary => HasPair ? $"{Name}.JPG + {Name}.CR3" : PairStatus;

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
