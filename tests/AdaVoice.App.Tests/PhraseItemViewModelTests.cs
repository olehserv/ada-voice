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
}
