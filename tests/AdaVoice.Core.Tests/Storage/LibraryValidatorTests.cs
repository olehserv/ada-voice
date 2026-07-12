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

    [Fact]
    public void Returns_version_ids_whose_audio_is_missing()
    {
        var library = new Library
        {
            Phrases =
            [
                new PhraseEntry
                {
                    Id = "p-1", FileName = "p-1.wav",
                    Versions =
                    [
                        new PhraseVersion { Id = "pv-a", FileName = "p-1-pv-a.wav" },
                        new PhraseVersion { Id = "pv-b", FileName = "p-1-pv-b.wav" },
                    ],
                },
            ],
        };

        var broken = LibraryValidator.FindBrokenVersionIds(library, name => name != "p-1-pv-b.wav");

        Assert.Equal(["pv-b"], broken);
    }
}
