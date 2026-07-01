using AdaVoice.App.ViewModels;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class PhraseEditViewModelTests
{
    private static FakePlaybackHost HostWith(PhraseEntry entry) =>
        new() { Phrases = [entry], Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized" }] };

    [Fact]
    public void Save_applies_title_category_and_the_chip_tags()
    {
        var host = HostWith(new PhraseEntry { Id = "p-1", Title = "Old", CategoryId = Category.DefaultId });
        var vm = new PhraseEditViewModel(host, host.Phrases[0]) { Title = "  Hello  " };
        vm.NewTag = "opening";
        vm.AddTagCommand.Execute(null);
        vm.NewTag = "greeting";
        vm.AddTagCommand.Execute(null);

        var saved = vm.Save();

        Assert.Equal("Hello", saved!.Title);                       // trimmed
        Assert.Equal(new[] { "opening", "greeting" }, saved.Tags); // the chip list
    }

    [Fact]
    public void Add_tag_ignores_blanks_and_case_insensitive_duplicates()
    {
        var host = HostWith(new PhraseEntry { Id = "p-1", Title = "T", Tags = ["opening"] });
        var vm = new PhraseEditViewModel(host, host.Phrases[0]);

        vm.NewTag = "  ";           // blank
        vm.AddTagCommand.Execute(null);
        vm.NewTag = "OPENING";      // dup (different case)
        vm.AddTagCommand.Execute(null);
        vm.NewTag = "closing";      // new
        vm.AddTagCommand.Execute(null);

        Assert.Equal(new[] { "opening", "closing" }, vm.Tags);
        Assert.Equal("", vm.NewTag); // input cleared
    }

    [Fact]
    public void Remove_tag_drops_the_chip()
    {
        var host = HostWith(new PhraseEntry { Id = "p-1", Title = "T", Tags = ["opening", "closing"] });
        var vm = new PhraseEditViewModel(host, host.Phrases[0]);

        vm.RemoveTagCommand.Execute("opening");

        Assert.Equal(new[] { "closing" }, vm.Tags);
    }

    [Fact]
    public void Suggestions_are_registry_tags_not_already_on_the_phrase()
    {
        var host = HostWith(new PhraseEntry { Id = "p-1", Title = "T", Tags = ["opening"] });
        host.Tags = [new TagInfo { Name = "opening" }, new TagInfo { Name = "closing" }, new TagInfo { Name = "urgent" }];
        var vm = new PhraseEditViewModel(host, host.Phrases[0]);

        Assert.Equal(new[] { "closing", "urgent" }, vm.Suggestions); // "opening" already on the phrase

        vm.AddSuggestionCommand.Execute("closing");

        Assert.Contains("closing", vm.Tags);
        Assert.DoesNotContain("closing", vm.Suggestions); // moves out of suggestions once added
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
