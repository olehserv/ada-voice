using AdaVoice.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>One phrase button on the board. Wraps the stored <see cref="PhraseEntry"/> and adds
/// per-item UI state — for now just <see cref="IsPlaying"/> (the playing glow); broken/decode states
/// will live here too.</summary>
public partial class PhraseItemViewModel(PhraseEntry entry) : ObservableObject
{
    public PhraseEntry Entry { get; } = entry;

    public string Title => Entry.Title;
    public int DurationMs => Entry.DurationMs;

    [ObservableProperty]
    private bool _isPlaying;
}
