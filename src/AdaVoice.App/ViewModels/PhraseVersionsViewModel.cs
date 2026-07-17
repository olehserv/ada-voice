using System.Collections.ObjectModel;
using AdaVoice.Core.Domain;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Backs the "Versions" window for one phrase: a board-like tile grid of the primary recording plus
/// every alternate take, opened from the phrase tile's "Versions…" context-menu entry (a separate
/// window from the Edit dialog — see design: docs/superpowers/plans/2026-07-07-phrase-versions.md).
/// Add/rename/delete each commit to the library immediately (unlike <see cref="PhraseEditViewModel"/>'s
/// Title/Category/Tags, which wait for Save) — delete is already an irreversible file rename the
/// instant it runs, and recording a new take is eager the moment it's saved too, so rename follows the
/// same rule for consistency. Pure (no XAML), so it is unit-testable.
/// </summary>
public partial class PhraseVersionsViewModel : ObservableObject
{
    private readonly ILibraryHost _library;
    private readonly IPlaybackHost _playback;
    private readonly string _phraseId;
    private readonly Func<string, Task<PhraseEntry?>> _recordVersion;

    public PhraseVersionsViewModel(
        ILibraryHost library,
        IPlaybackHost playback,
        PhraseEntry entry,
        Func<string, Task<PhraseEntry?>>? recordVersion = null)
    {
        _library = library;
        _playback = playback;
        _phraseId = entry.Id;
        _recordVersion = recordVersion ?? (_ => Task.FromResult<PhraseEntry?>(null)); // default: no-op (unit tests opt in)
        Title = entry.Title;

        Tiles = new ObservableCollection<PhraseVersionRowViewModel>(
            [PhraseVersionRowViewModel.ForPrimary(entry, library.BrokenPhraseIds.Contains(entry.Id)),
             .. entry.Versions.Select(v =>
                 new PhraseVersionRowViewModel(library, entry.Id, v, library.BrokenVersionIds.Contains(v.Id)))]);
    }

    /// <summary>The phrase's title — shown in the window's title bar.</summary>
    public string Title { get; }

    /// <summary>The primary recording followed by every version, as one tile grid. The primary tile
    /// is playable but not renamable/deletable here (that happens on the board tile itself, via
    /// Edit/Delete) — <see cref="PhraseVersionRowViewModel.IsPrimary"/> drives that in the view.</summary>
    public ObservableCollection<PhraseVersionRowViewModel> Tiles { get; }

    /// <summary>Raised when a preview fails; the view shows it as a toast. Reuses
    /// <see cref="BoardViewModel"/>'s notification type rather than inventing a second one.</summary>
    public event EventHandler<BoardNotification>? Notified;

    /// <summary>"Add version" records without closing this window — it awaits the injected
    /// <c>recordVersion</c> callback (<c>BoardViewModel.RecordVersionForPhrase</c>), which drives the
    /// same recorder used by the board's own Record button, then refreshes the tile grid from
    /// whatever entry it returns. A null result (recording failed, or was discarded) leaves the tiles
    /// untouched.</summary>
    [RelayCommand]
    private async Task RecordVersion()
    {
        if (await _recordVersion(_phraseId) is { } updated)
            Refresh(updated);
    }

    /// <summary>Rebuild the tile grid from a freshly re-read entry — used after recording a new
    /// version, so the window shows it without having to be reopened.</summary>
    private void Refresh(PhraseEntry entry)
    {
        Tiles.Clear();
        Tiles.Add(PhraseVersionRowViewModel.ForPrimary(entry, _library.BrokenPhraseIds.Contains(entry.Id)));
        foreach (var version in entry.Versions)
            Tiles.Add(new PhraseVersionRowViewModel(_library, entry.Id, version, _library.BrokenVersionIds.Contains(version.Id)));
    }

    /// <summary>Play a tile (primary or version) on the monitor, to hear it — or, if that tile is
    /// already playing, stop it. Runs off the UI thread like <c>BoardViewModel.TestOnHeadphones</c>;
    /// <see cref="PhraseVersionRowViewModel.IsPlaying"/> turns the tile's ▶ into a ■ for the duration.
    /// <c>AllowConcurrentExecutions</c> is required: without it, <c>[RelayCommand]</c>'s default
    /// disables the button for the whole first call, so the ■ could never be clicked to stop it.
    /// A returned error (missing file, wrong monitor device) or a thrown exception both surface via
    /// <see cref="Notified"/> instead of failing silently (review finding 4).</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Play(PhraseVersionRowViewModel? row)
    {
        if (row is null)
            return;

        if (row.IsPlaying)
        {
            _playback.StopPreview();
            return; // the still-running Play() call below clears IsPlaying when it returns
        }

        if (Tiles.Any(t => t.IsPlaying))
            return; // one preview at a time — stop it first, then play this tile

        row.IsPlaying = true;
        try
        {
            var error = row.Version is { } version
                ? await Task.Run(() => _playback.PreviewVersion(version))
                : await Task.Run(() => _playback.PreviewEntry(row.PrimaryEntry!));
            if (error is not null)
                Notified?.Invoke(this, new BoardNotification(error, NoticeSeverity.Error));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Notified?.Invoke(this, new BoardNotification(
                "Could not play the preview — check the playback device and try again.", NoticeSeverity.Error));
        }
        finally
        {
            row.IsPlaying = false;
        }
    }

    /// <summary>Stop any in-progress preview — called when the Versions window closes (by any means),
    /// so audio never keeps playing after the window is gone.</summary>
    public void StopPreview() => _playback.StopPreview();

    /// <summary>Delete a version by orphaning its WAV (never destroyed), mirroring
    /// <c>BoardViewModel.Delete</c>'s eager phrase delete. A no-op for the primary tile — it has
    /// nothing to delete here.</summary>
    [RelayCommand]
    private void DeleteVersion(PhraseVersionRowViewModel? row)
    {
        if (row?.Version is not { } version)
            return;

        if (_library.DeletePhraseVersion(_phraseId, version.Id) is not null)
            Tiles.Remove(row);
    }
}

/// <summary>One tile in the Versions window: either the primary recording (<see cref="IsPrimary"/>,
/// fixed label, not renamable/deletable) or one alternate take. Renaming a version persists
/// immediately — there is no separate Save step for a version's label.</summary>
public partial class PhraseVersionRowViewModel : ObservableObject
{
    private readonly ILibraryHost? _library; // null for the primary tile — nothing to rename
    private readonly string? _phraseId;

    private PhraseVersionRowViewModel(PhraseEntry primary, bool isBroken)
    {
        PrimaryEntry = primary;
        _label = "Primary";
        IsBroken = isBroken;
    }

    public PhraseVersionRowViewModel(ILibraryHost library, string phraseId, PhraseVersion version, bool isBroken = false)
    {
        _library = library;
        _phraseId = phraseId;
        Version = version;
        _label = version.Label;
        IsBroken = isBroken;
    }

    public static PhraseVersionRowViewModel ForPrimary(PhraseEntry entry, bool isBroken = false) => new(entry, isBroken);

    /// <summary>True when this tile's audio file is missing — the view shows a "missing audio" marker so
    /// a silent no-op ▶ is explained (security scan 2026-07-12 finding 5).</summary>
    public bool IsBroken { get; }

    /// <summary>Set only for the primary tile.</summary>
    public PhraseEntry? PrimaryEntry { get; }

    /// <summary>Set only for a version tile — kept up to date after a rename so Play always reads the
    /// current file/gain.</summary>
    public PhraseVersion? Version { get; private set; }

    public bool IsPrimary => PrimaryEntry is not null;

    /// <summary>False while the library refuses writes (a transiently locked file) — the label textbox
    /// binds its <c>IsEnabled</c> to this for a version tile, so a refused rename shows as "disabled"
    /// instead of throwing inside the binding engine, where WPF would swallow it silently (review
    /// finding 9). Always true for the primary tile, whose label is read-only anyway.</summary>
    public bool IsWritable => _library?.IsWritable ?? true;

    /// <summary>The tile's length, matching the board tile's own duration label.</summary>
    public string DurationLabel => $"{(Version?.DurationMs ?? PrimaryEntry!.DurationMs) / 1000.0:0.0} s";

    /// <summary>True while this tile's preview is sounding — swaps its ▶ button for a ■ (stop).</summary>
    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _label;

    partial void OnLabelChanged(string value)
    {
        if (_library is null || _phraseId is null || Version is null)
            return; // the primary tile's label is fixed and never persisted

        if (_library.SetPhraseVersionLabel(_phraseId, Version.Id, value) is { } updated
            && updated.Versions.FirstOrDefault(v => v.Id == Version.Id) is { } version)
            Version = version;
    }
}
