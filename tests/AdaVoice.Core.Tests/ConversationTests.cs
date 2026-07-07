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

    // A fresh service re-reads from disk, so using one to assert proves the change was persisted.
    private PhraseLibraryService NewService() => new(new JsonPhraseRepository(_root));

    [Fact]
    public void AddConversation_creates_persists_and_appends_sort_order()
    {
        var service = NewService();

        var conversation = service.AddConversation("Cold call intro");

        Assert.StartsWith("v-", conversation.Id);
        Assert.Equal("Cold call intro", conversation.Name);
        Assert.Empty(conversation.PhraseIds);
        Assert.Equal(0, conversation.SortOrder);
        Assert.Contains(NewService().Conversations, c => c.Id == conversation.Id);
    }

    [Fact]
    public void AddConversation_blank_name_throws()
    {
        Assert.Throws<ArgumentException>(() => NewService().AddConversation("   "));
    }

    [Fact]
    public void RenameConversation_renames_and_persists_unknown_returns_null()
    {
        var service = NewService();
        var conversation = service.AddConversation("Old");

        var renamed = service.RenameConversation(conversation.Id, "New");

        Assert.Equal("New", renamed!.Name);
        Assert.Null(service.RenameConversation("v-nope", "X"));
        Assert.Equal("New", Assert.Single(NewService().Conversations).Name);
    }

    [Fact]
    public void DeleteConversation_removes_it_but_leaves_phrases_untouched()
    {
        var service = NewService();
        var phrase = service.Add("p", Category.DefaultId, 100, 0, _ => { });
        var conversation = service.AddConversation("Temp");
        service.SetConversationPhrases(conversation.Id, [phrase.Id]);

        Assert.True(service.DeleteConversation(conversation.Id));

        var reloaded = NewService();
        Assert.Empty(reloaded.Conversations);
        Assert.Single(reloaded.Phrases); // untouched
    }

    [Fact]
    public void DeleteConversation_unknown_returns_false()
    {
        Assert.False(NewService().DeleteConversation("v-nope"));
    }

    [Fact]
    public void SetConversationPhrases_replaces_the_ordered_list_and_drops_unknown_ids()
    {
        var service = NewService();
        var p1 = service.Add("one", Category.DefaultId, 100, 0, _ => { });
        var p2 = service.Add("two", Category.DefaultId, 100, 0, _ => { });
        var conversation = service.AddConversation("Script");

        var updated = service.SetConversationPhrases(conversation.Id, [p2.Id, "p-nope", p1.Id]);

        // unknown id dropped, order preserved for the rest
        Assert.Equal([p2.Id, p1.Id], updated!.PhraseIds);
        Assert.Equal([p2.Id, p1.Id], Assert.Single(NewService().Conversations).PhraseIds); // persisted
    }

    [Fact]
    public void SetConversationPhrases_unknown_conversation_returns_null()
    {
        Assert.Null(NewService().SetConversationPhrases("v-nope", []));
    }

    [Fact]
    public void Deleting_a_phrase_prunes_it_from_every_conversation_immediately()
    {
        var service = NewService();
        var p1 = service.Add("one", Category.DefaultId, 100, 0, _ => { });
        var p2 = service.Add("two", Category.DefaultId, 100, 0, _ => { });
        var conversation = service.AddConversation("Script");
        service.SetConversationPhrases(conversation.Id, [p1.Id, p2.Id]);

        service.Delete(p1.Id, (_, _) => { });

        // in-session, without waiting for a reload
        Assert.Equal([p2.Id], service.Conversations.Single().PhraseIds);
        Assert.Equal([p2.Id], NewService().Conversations.Single().PhraseIds); // persisted
    }

    [Fact]
    public void Loading_a_library_prunes_conversation_references_to_phrases_that_no_longer_exist()
    {
        // A hand-edited or merge-imported library where a conversation outlived a phrase it referenced.
        Directory.CreateDirectory(_root);
        File.WriteAllText(AdaVoicePaths.LibraryFile(_root), """
            {
              "version": 1,
              "categories": [{ "id": "c-default", "name": "Uncategorized", "color": "#808080", "sortOrder": 0 }],
              "phrases": [],
              "conversations": [{ "id": "v-1", "name": "Script", "phraseIds": ["p-gone"], "sortOrder": 0,
                                   "createdAt": "2024-01-01T00:00:00Z", "updatedAt": "2024-01-01T00:00:00Z" }]
            }
            """);

        var loaded = NewService(); // Load() should prune it

        Assert.Empty(loaded.Conversations.Single().PhraseIds);
        Assert.Empty(NewService().Conversations.Single().PhraseIds); // persisted, not re-derived every load
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
