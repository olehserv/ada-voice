using AdaVoice.Core;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests;

public class ConversationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-conv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_conversation_round_trips_through_the_repository()
    {
        var repository = new JsonPhraseRepository(_root);
        var library = repository.Load().Library with
        {
            Conversations =
            [
                new Conversation
                {
                    Id = "v-1",
                    Name = "Cold call intro",
                    PhraseIds = ["p-1", "p-2"],
                    SortOrder = 0,
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                },
            ],
        };
        repository.Save(library);

        var reloaded = new JsonPhraseRepository(_root).Load().Library;

        var conversation = Assert.Single(reloaded.Conversations);
        Assert.Equal("v-1", conversation.Id);
        Assert.Equal("Cold call intro", conversation.Name);
        Assert.Equal(["p-1", "p-2"], conversation.PhraseIds);
    }

    [Fact]
    public void An_old_schema_file_with_no_conversations_key_loads_with_an_empty_list()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(AdaVoicePaths.LibraryFile(_root), """
            {
              "version": 1,
              "categories": [{ "id": "c-default", "name": "Uncategorized", "color": "#808080", "sortOrder": 0 }],
              "phrases": []
            }
            """);

        var library = new JsonPhraseRepository(_root).Load().Library;

        Assert.Empty(library.Conversations);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
