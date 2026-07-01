using AdaVoice.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>One phrase button on the board. Wraps the stored <see cref="PhraseEntry"/> and adds
/// per-item UI state: <see cref="IsPlaying"/> (the playing glow) and <see cref="IsBroken"/> (audio file
/// missing). The wrapped entry is immutable; after an edit, call <see cref="Update"/> with the new entry
/// so the bound, derived properties refresh.</summary>
public partial class PhraseItemViewModel(PhraseEntry entry) : ObservableObject
{
    /// <summary>The stored phrase. Replaced (not mutated) by <see cref="Update"/> after an edit.</summary>
    public PhraseEntry Entry { get; private set; } = entry;

    public string Title => Entry.Title;
    public int DurationMs => Entry.DurationMs;
    public string CategoryId => Entry.CategoryId;
    public IReadOnlyList<string> Tags => Entry.Tags;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>True when the phrase's audio file is missing — the button dims and play is disabled.</summary>
    [ObservableProperty]
    private bool _isBroken;

    /// <summary>Hex fill colour of the phrase's category (set by the board from the category list). Empty
    /// means "no colour" — the tile falls back to a neutral fill.</summary>
    [ObservableProperty]
    private string _categoryColor = "";

    /// <summary>Swap in the edited entry and refresh every derived, bound property. <see cref="PhraseEntry"/>
    /// is an immutable record, so an edit returns a new instance — without this the UI keeps the old one.</summary>
    public void Update(PhraseEntry updated)
    {
        Entry = updated;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DurationMs));
        OnPropertyChanged(nameof(CategoryId));
        OnPropertyChanged(nameof(Tags));
    }
}
