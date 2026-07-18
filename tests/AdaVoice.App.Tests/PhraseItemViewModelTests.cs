using AdaVoice.App.ViewModels;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class PhraseItemViewModelTests
{
    [Theory]
    [InlineData(5700, "5.7 s")]
    [InlineData(1000, "1.0 s")]
    [InlineData(450, "0.5 s")]
    [InlineData(0, "0.0 s")]
    [InlineData(12340, "12.3 s")]
    public void Duration_label_is_seconds_with_one_decimal(int ms, string expected)
    {
        var item = new PhraseItemViewModel(new PhraseEntry { Id = "p-1", DurationMs = ms });

        Assert.Equal(expected, item.DurationLabel);
    }

    [Fact]
    public void Duration_label_refreshes_after_an_edit()
    {
        var item = new PhraseItemViewModel(new PhraseEntry { Id = "p-1", DurationMs = 1000 });
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.Update(new PhraseEntry { Id = "p-1", DurationMs = 2500 });

        Assert.Equal("2.5 s", item.DurationLabel);
        Assert.Contains(nameof(PhraseItemViewModel.DurationLabel), changed);
    }

    // Phase B (brand redesign): the tile is fixed-size, so tag count can never change its layout —
    // anything past the visible cap collapses into a "+N" overflow chip instead.
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(2, 1, true)]
    [InlineData(3, 2, true)]
    [InlineData(5, 4, true)]
    public void Tag_overflow_reflects_tags_beyond_the_visible_cap(int tagCount, int expectedOverflow, bool expectedHasOverflow)
    {
        var item = new PhraseItemViewModel(new PhraseEntry { Id = "p-1" })
        {
            TagChips = Enumerable.Range(0, tagCount)
                .Select(i => new TagChipViewModel($"tag{i}", "#000000"))
                .ToList(),
        };

        Assert.Equal(Math.Min(tagCount, 1), item.VisibleTagChips.Count);
        Assert.Equal(expectedOverflow, item.OverflowTagCount);
        Assert.Equal(expectedHasOverflow, item.HasOverflowTags);
    }

    [Fact]
    public void Tag_overflow_properties_notify_when_TagChips_changes()
    {
        var item = new PhraseItemViewModel(new PhraseEntry { Id = "p-1" });
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.TagChips = [new TagChipViewModel("a", "#000000"), new TagChipViewModel("b", "#000000"), new TagChipViewModel("c", "#000000")];

        Assert.Contains(nameof(PhraseItemViewModel.VisibleTagChips), changed);
        Assert.Contains(nameof(PhraseItemViewModel.OverflowTagCount), changed);
        Assert.Contains(nameof(PhraseItemViewModel.HasOverflowTags), changed);
    }
}
