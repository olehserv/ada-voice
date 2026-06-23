using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests.Storage;

public class LibraryValidatorTests
{
    private static Library WithPhrases(params string[] fileNames) => new()
    {
        Phrases = fileNames.Select((f, i) => new PhraseEntry { Id = $"p-{i}", FileName = f }).ToList(),
    };

    [Fact]
    public void Returns_ids_of_phrases_whose_audio_is_missing()
    {
        var library = WithPhrases("a.wav", "b.wav", "c.wav");

        var broken = LibraryValidator.FindBrokenPhraseIds(library, name => name != "b.wav");

        Assert.Equal(["p-1"], broken);
    }

    [Fact]
    public void Empty_when_all_audio_exists()
    {
        var library = WithPhrases("a.wav", "b.wav");

        var broken = LibraryValidator.FindBrokenPhraseIds(library, _ => true);

        Assert.Empty(broken);
    }
}
