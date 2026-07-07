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

    /// <summary>Duration in seconds with one decimal (e.g. "5.7 s") — friendlier than milliseconds.</summary>
    public string DurationLabel => $"{DurationMs / 1000.0:0.0} s";

    public string CategoryId => Entry.CategoryId;
    public IReadOnlyList<string> Tags => Entry.Tags;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>True when the phrase's audio file is missing — the button dims and play is disabled.</summary>
    [ObservableProperty]
    private bool _isBroken;

    /// <summary>True when this phrase is the active conversation's next expected step — the board
    /// gives it a highlight ring. Set by <c>BoardViewModel.UpdateCurrentStepHighlight</c>.</summary>
    [ObservableProperty]
    private bool _isCurrentStep;

    /// <summary>Sort key while a conversation is active: this phrase's position in the conversation's
    /// step order, or <see cref="int.MaxValue"/> if it isn't a member (irrelevant — the board's filter
    /// already hides those). Ignored (left at its default) when no conversation is active.</summary>
    [ObservableProperty]
    private int _conversationStepIndex;

    /// <summary>Hex fill colour of the phrase's category (set by the board from the category list). Empty
    /// means "no colour" — the tile falls back to a neutral fill.</summary>
    [ObservableProperty]
    private string _categoryColor = "";

    /// <summary>The phrase's tags with their registry colours (set by the board). Empty until the board
    /// resolves them against the tag registry.</summary>
    [ObservableProperty]
    private IReadOnlyList<TagChipViewModel> _tagChips = [];

    /// <summary>Swap in the edited entry and refresh every derived, bound property. <see cref="PhraseEntry"/>
    /// is an immutable record, so an edit returns a new instance — without this the UI keeps the old one.</summary>
    public void Update(PhraseEntry updated)
    {
        Entry = updated;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DurationMs));
        OnPropertyChanged(nameof(DurationLabel));
        OnPropertyChanged(nameof(CategoryId));
        OnPropertyChanged(nameof(Tags));
    }
}

/// <summary>One coloured tag chip shown on a phrase tile.</summary>
public sealed record TagChipViewModel(string Name, string Color);
