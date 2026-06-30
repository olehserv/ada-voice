using AdaVoice.App.ViewModels;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class PhraseEditViewModelTests
{
    private static FakePlaybackHost HostWith(PhraseEntry entry) =>
        new() { Phrases = [entry], Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized" }] };

    [Fact]
    public void Save_applies_title_category_and_parsed_tags()
    {
        var host = HostWith(new PhraseEntry { Id = "p-1", Title = "Old", CategoryId = Category.DefaultId });
        var vm = new PhraseEditViewModel(host, host.Phrases[0])
        {
            Title = "  Hello  ",
            TagsText = "opening, greeting ,, ",
        };

        var saved = vm.Save();

        Assert.Equal("Hello", saved!.Title);                    // trimmed
        Assert.Equal(new[] { "opening", "greeting" }, saved.Tags); // split on commas, blanks dropped
    }

    [Fact]
    public void Save_rejects_a_blank_title()
    {
        var host = HostWith(new PhraseEntry { Id = "p-1", Title = "Old", CategoryId = Category.DefaultId });
        var vm = new PhraseEditViewModel(host, host.Phrases[0]) { Title = "   " };

        Assert.False(vm.HasValidTitle);
        Assert.Null(vm.Save());
        Assert.Equal("Old", host.Phrases[0].Title); // unchanged
    }
}
