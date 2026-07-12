using AdaVoice.App.ViewModels;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class PhraseVersionsViewModelTests
{
    private static FakePlaybackHost HostWith(PhraseEntry entry) =>
        new() { Phrases = [entry], Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized" }] };

    [Fact]
    public void Tiles_lead_with_the_primary_followed_by_every_version()
    {
        var entry = new PhraseEntry
        {
            Id = "p-1",
            Title = "T",
            DurationMs = 2000,
            Versions = [new PhraseVersion { Id = "pv-1", Label = "Friendly" }],
        };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);

        Assert.Equal(2, vm.Tiles.Count);
        Assert.True(vm.Tiles[0].IsPrimary);
        Assert.Equal("Primary", vm.Tiles[0].Label);
        Assert.False(vm.Tiles[1].IsPrimary);
        Assert.Equal("Friendly", vm.Tiles[1].Label);
    }

    [Fact]
    public void A_version_with_a_missing_audio_file_is_flagged_broken_without_flagging_the_primary()
    {
        var entry = new PhraseEntry
        {
            Id = "p-1",
            Title = "T",
            Versions =
            [
                new PhraseVersion { Id = "pv-ok", Label = "Good" },
                new PhraseVersion { Id = "pv-gone", Label = "Missing file" },
            ],
        };
        var host = HostWith(entry);
        host.BrokenVersionIds = ["pv-gone"]; // primary and pv-ok are fine

        var vm = new PhraseVersionsViewModel(host, host, entry);

        Assert.False(vm.Tiles[0].IsBroken); // primary
        Assert.False(vm.Tiles[1].IsBroken); // pv-ok
        Assert.True(vm.Tiles[2].IsBroken);  // pv-gone
    }

    [Fact]
    public async Task Record_version_passes_the_phrase_id_to_the_injected_callback()
    {
        var entry = new PhraseEntry { Id = "p-1", Title = "T" };
        var host = HostWith(entry);
        string? receivedId = null;
        var vm = new PhraseVersionsViewModel(host, host, entry, id =>
        {
            receivedId = id;
            return Task.FromResult<PhraseEntry?>(null);
        });

        await vm.RecordVersionCommand.ExecuteAsync(null);

        Assert.Equal("p-1", receivedId);
    }

    [Fact]
    public async Task Record_version_refreshes_the_tile_grid_from_the_entry_the_callback_returns()
    {
        var entry = new PhraseEntry { Id = "p-1", Title = "T" };
        var host = HostWith(entry);
        var updated = entry with { Versions = [new PhraseVersion { Id = "pv-1", Label = "New take" }] };
        var vm = new PhraseVersionsViewModel(host, host, entry, _ => Task.FromResult<PhraseEntry?>(updated));

        await vm.RecordVersionCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Tiles.Count);
        Assert.True(vm.Tiles[0].IsPrimary);
        Assert.Equal("New take", vm.Tiles[1].Label);
    }

    [Fact]
    public async Task Record_version_leaves_the_tile_grid_untouched_when_the_callback_returns_null()
    {
        var entry = new PhraseEntry
        {
            Id = "p-1",
            Title = "T",
            Versions = [new PhraseVersion { Id = "pv-1", Label = "Existing" }],
        };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry, _ => Task.FromResult<PhraseEntry?>(null));

        await vm.RecordVersionCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Tiles.Count);
        Assert.Equal("Existing", vm.Tiles[1].Label);
    }

    [Fact]
    public async Task Play_previews_the_primary_when_the_tile_is_the_primary()
    {
        var entry = new PhraseEntry { Id = "p-1", Title = "T", FileName = "p-1.wav" };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);

        await vm.PlayCommand.ExecuteAsync(vm.Tiles[0]);

        Assert.Equal("p-1.wav", host.PreviewedEntry?.FileName);
        Assert.Null(host.PreviewedVersion);
    }

    [Fact]
    public async Task Play_previews_the_versions_own_audio_when_the_tile_is_a_version()
    {
        var entry = new PhraseEntry
        {
            Id = "p-1",
            Title = "T",
            Versions = [new PhraseVersion { Id = "pv-1", Label = "Friendly", FileName = "p-1-pv-1.wav" }],
        };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);

        await vm.PlayCommand.ExecuteAsync(vm.Tiles[1]);

        Assert.Equal("p-1-pv-1.wav", host.PreviewedVersion?.FileName);
    }

    [Fact]
    public async Task Play_marks_the_tile_playing_then_clears_it_when_preview_finishes()
    {
        var entry = new PhraseEntry { Id = "p-1", Title = "T", FileName = "p-1.wav" };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);
        var wasPlayingDuringPreview = false;
        host.OnPreviewing = () => wasPlayingDuringPreview = vm.Tiles[0].IsPlaying;

        await vm.PlayCommand.ExecuteAsync(vm.Tiles[0]);

        Assert.True(wasPlayingDuringPreview);
        Assert.False(vm.Tiles[0].IsPlaying);
    }

    [Fact]
    public async Task Playing_the_same_tile_again_stops_it_instead_of_previewing_again()
    {
        var entry = new PhraseEntry { Id = "p-1", Title = "T", FileName = "p-1.wav" };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);
        vm.Tiles[0].IsPlaying = true; // simulate an in-flight preview, as Play() would leave it

        await vm.PlayCommand.ExecuteAsync(vm.Tiles[0]);

        Assert.Equal(1, host.StopPreviewCalls);
        Assert.DoesNotContain("PreviewEntry", host.Calls); // did not start a second preview
    }

    [Fact]
    public async Task Play_button_stays_clickable_while_its_own_preview_is_in_flight()
    {
        // AsyncRelayCommand disables its button for the whole call by default — without
        // AllowConcurrentExecutions on the Play command, the ■ (stop) click below could never
        // reach the view-model, since WPF would never let the click through.
        var entry = new PhraseEntry { Id = "p-1", Title = "T", FileName = "p-1.wav" };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);
        var gate = new TaskCompletionSource();
        host.OnPreviewing = () => gate.Task.Wait();

        var inFlight = vm.PlayCommand.ExecuteAsync(vm.Tiles[0]);
        while (!vm.Tiles[0].IsPlaying)
            await Task.Yield(); // wait for Play() to reach the awaited preview call

        var canStopWhilePlaying = vm.PlayCommand.CanExecute(vm.Tiles[0]);
        gate.SetResult();
        await inFlight;

        Assert.True(canStopWhilePlaying);
    }

    [Fact]
    public async Task Playing_a_different_tile_while_one_previews_is_a_noop()
    {
        var entry = new PhraseEntry
        {
            Id = "p-1",
            Title = "T",
            Versions = [new PhraseVersion { Id = "pv-1", Label = "Alt", FileName = "p-1-pv-1.wav" }],
        };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);
        var gate = new TaskCompletionSource();
        host.OnPreviewing = () => gate.Task.Wait();

        var inFlight = vm.PlayCommand.ExecuteAsync(vm.Tiles[0]);
        while (!vm.Tiles[0].IsPlaying)
            await Task.Yield();

        await vm.PlayCommand.ExecuteAsync(vm.Tiles[1]); // a different tile, while the first still plays
        gate.SetResult();
        await inFlight;

        Assert.False(vm.Tiles[1].IsPlaying);
        Assert.DoesNotContain("PreviewVersion", host.Calls); // never started — one preview at a time
    }

    [Fact]
    public void Stop_preview_delegates_to_the_host()
    {
        var entry = new PhraseEntry { Id = "p-1", Title = "T" };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);

        vm.StopPreview();

        Assert.Equal(1, host.StopPreviewCalls);
    }

    [Fact]
    public void Renaming_the_primary_tile_does_not_crash_or_touch_the_library()
    {
        var entry = new PhraseEntry { Id = "p-1", Title = "T" };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);

        vm.Tiles[0].Label = "Something else"; // the primary tile has no library-backed version to persist to

        Assert.Equal("Something else", vm.Tiles[0].Label); // local edit still shows (read-only in the real UI)
        Assert.Empty(host.Phrases[0].Versions); // nothing was fabricated on the phrase
    }

    [Fact]
    public void Rename_version_persists_immediately()
    {
        var entry = new PhraseEntry
        {
            Id = "p-1",
            Title = "T",
            Versions = [new PhraseVersion { Id = "pv-1", Label = "Old label" }],
        };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);

        vm.Tiles[1].Label = "New label";

        Assert.Equal("New label", host.Phrases[0].Versions[0].Label); // written straight through
    }

    [Fact]
    public void Delete_version_orphans_it_and_removes_the_tile()
    {
        var entry = new PhraseEntry
        {
            Id = "p-1",
            Title = "T",
            Versions = [new PhraseVersion { Id = "pv-1", Label = "A" }, new PhraseVersion { Id = "pv-2", Label = "B" }],
        };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);

        vm.DeleteVersionCommand.Execute(vm.Tiles[1]); // "A"

        Assert.Equal(2, vm.Tiles.Count); // primary + "B"
        Assert.Equal("B", vm.Tiles[1].Label);
        Assert.Single(host.Phrases[0].Versions);
        Assert.Equal("pv-2", host.Phrases[0].Versions[0].Id);
    }

    [Fact]
    public void Delete_version_on_the_primary_tile_is_a_noop()
    {
        var entry = new PhraseEntry { Id = "p-1", Title = "T" };
        var host = HostWith(entry);
        var vm = new PhraseVersionsViewModel(host, host, entry);

        vm.DeleteVersionCommand.Execute(vm.Tiles[0]); // the primary tile

        Assert.Single(vm.Tiles); // still there — nothing to delete
    }
}
