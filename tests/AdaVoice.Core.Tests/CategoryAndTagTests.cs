using AdaVoice.Core;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests;

public class CategoryAndTagTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-cat-" + Guid.NewGuid().ToString("N"));

    // A fresh service re-reads from disk, so using one to assert proves the change was persisted.
    private PhraseLibraryService NewService() => new(new JsonPhraseRepository(_root));

    [Fact]
    public void AddCategory_creates_persists_and_appends_sort_order()
    {
        var service = NewService();

        var category = service.AddCategory("Greetings", "#4F8EF7");

        Assert.StartsWith("c-", category.Id);
        Assert.Equal("Greetings", category.Name);
        Assert.Equal(1, category.SortOrder); // the seeded default sits at 0
        Assert.Contains(NewService().Categories, c => c.Id == category.Id);
    }

    [Fact]
    public void AddCategory_blank_name_throws()
    {
        Assert.Throws<ArgumentException>(() => NewService().AddCategory("   ", "#fff"));
    }

    [Fact]
    public void UpdateCategory_renames_and_recolors_unknown_returns_null()
    {
        var service = NewService();
        var category = service.AddCategory("Old", "#000000");

        var updated = service.UpdateCategory(category.Id, "New", "#ffffff");

        Assert.Equal("New", updated!.Name);
        Assert.Equal("#ffffff", updated.Color);
        Assert.Null(service.UpdateCategory("c-nope", "X", "#fff"));
    }

    [Fact]
    public void DeleteCategory_reassigns_its_phrases_to_default_and_persists()
    {
        var service = NewService();
        var category = service.AddCategory("Temp", "#000000");
        service.Add("p", category.Id, 100, 0, _ => { });

        Assert.True(service.DeleteCategory(category.Id));

        var reloaded = NewService();
        Assert.DoesNotContain(reloaded.Categories, c => c.Id == category.Id);
        Assert.Equal(Category.DefaultId, Assert.Single(reloaded.Phrases).CategoryId); // never lost
    }

    [Fact]
    public void DeleteCategory_default_is_protected()
    {
        var service = NewService();

        Assert.False(service.DeleteCategory(Category.DefaultId));
        Assert.Contains(service.Categories, c => c.Id == Category.DefaultId);
    }

    [Fact]
    public void DeleteCategory_unknown_returns_false()
    {
        Assert.False(NewService().DeleteCategory("c-nope"));
    }

    [Fact]
    public void SetPhraseCategory_moves_the_phrase_and_rejects_unknown_targets()
    {
        var service = NewService();
        var category = service.AddCategory("Cat", "#000000");
        var phrase = service.Add("p", Category.DefaultId, 100, 0, _ => { });

        var moved = service.SetPhraseCategory(phrase.Id, category.Id);

        Assert.Equal(category.Id, moved!.CategoryId);
        Assert.Equal(phrase.CreatedAt, moved.CreatedAt);                 // edit preserves CreatedAt
        Assert.Null(service.SetPhraseCategory("p-nope", category.Id));   // unknown phrase
        Assert.Null(service.SetPhraseCategory(phrase.Id, "c-nope"));     // unknown category
    }

    [Fact]
    public void SetPhraseTitle_renames_persists_and_bumps_updated_at()
    {
        var service = NewService();
        var phrase = service.Add("Old title", Category.DefaultId, 100, 0, _ => { });

        var renamed = service.SetPhraseTitle(phrase.Id, "  New title  ");

        Assert.Equal("New title", renamed!.Title);                       // trimmed
        Assert.Equal(phrase.CreatedAt, renamed.CreatedAt);               // edit preserves CreatedAt
        Assert.True(renamed.UpdatedAt >= phrase.UpdatedAt);              // bumped
        Assert.Equal("New title", Assert.Single(NewService().Phrases).Title); // persisted
    }

    [Fact]
    public void SetPhraseTitle_unknown_returns_null()
    {
        Assert.Null(NewService().SetPhraseTitle("p-nope", "X"));
    }

    [Fact]
    public void SetPhraseTitle_blank_throws()
    {
        var service = NewService();
        var phrase = service.Add("Old", Category.DefaultId, 100, 0, _ => { });

        Assert.Throws<ArgumentException>(() => service.SetPhraseTitle(phrase.Id, "   "));
    }

    [Fact]
    public void SetPhraseTags_normalizes_persists_and_rejects_unknown()
    {
        var service = NewService();
        var phrase = service.Add("p", Category.DefaultId, 100, 0, _ => { });

        var updated = service.SetPhraseTags(phrase.Id, ["  opening ", "Greeting", "greeting", "", "  "]);

        // trimmed, blanks dropped, de-duplicated case-insensitively (first wins), order preserved
        Assert.Equal(new[] { "opening", "Greeting" }, updated!.Tags);
        Assert.Null(service.SetPhraseTags("p-nope", ["x"]));
        Assert.Equal(new[] { "opening", "Greeting" }, Assert.Single(NewService().Phrases).Tags); // persisted
    }

    // ---- Tag registry --------------------------------------------------------------------------

    [Fact]
    public void SetPhraseTags_registers_new_tags_with_cycling_palette_colours()
    {
        var service = NewService();
        var phrase = service.Add("p", Category.DefaultId, 100, 0, _ => { });

        service.SetPhraseTags(phrase.Id, ["alpha", "beta", "gamma"]);

        var tags = service.Tags;
        Assert.Equal(["alpha", "beta", "gamma"], tags.Select(t => t.Name));
        Assert.Equal(ColorPalette.Swatches[0], tags[0].Color);
        Assert.Equal(ColorPalette.Swatches[1], tags[1].Color);
        Assert.Equal(ColorPalette.Swatches[2], tags[2].Color);
    }

    [Fact]
    public void SetPhraseTags_keeps_an_existing_tags_colour_and_ignores_case()
    {
        var service = NewService();
        var phrase = service.Add("p", Category.DefaultId, 100, 0, _ => { });
        service.SetPhraseTags(phrase.Id, ["alpha"]);
        var originalColor = service.Tags.Single().Color;

        // Re-use the tag under different casing on another phrase — no new registry entry, same colour.
        var other = service.Add("q", Category.DefaultId, 100, 0, _ => { });
        service.SetPhraseTags(other.Id, ["ALPHA", "beta"]);

        Assert.Equal(["alpha", "beta"], service.Tags.Select(t => t.Name)); // no duplicate "ALPHA"
        Assert.Equal(originalColor, service.Tags.First(t => t.Name == "alpha").Color);
    }

    [Fact]
    public void Tag_registry_round_trips_through_reload()
    {
        var service = NewService();
        var phrase = service.Add("p", Category.DefaultId, 100, 0, _ => { });
        service.SetPhraseTags(phrase.Id, ["alpha", "beta"]);

        var reloaded = NewService();

        Assert.Equal(["alpha", "beta"], reloaded.Tags.Select(t => t.Name));
        Assert.Equal(ColorPalette.Swatches[0], reloaded.Tags[0].Color);
    }

    [Fact]
    public void Loading_a_library_registers_pre_existing_phrase_tags_once()
    {
        // A legacy library file: a phrase carries tags, but there is no tag registry (pre-registry era).
        Directory.CreateDirectory(_root);
        File.WriteAllText(AdaVoicePaths.LibraryFile(_root), """
            {
              "version": 1,
              "categories": [{ "id": "c-default", "name": "Uncategorized", "color": "#808080", "sortOrder": 0 }],
              "phrases": [{ "id": "p-1", "title": "p", "categoryId": "c-default", "tags": ["legacy"],
                            "fileName": "p-1.wav", "durationMs": 100, "gainDb": 0, "sortOrder": 0,
                            "createdAt": "2024-01-01T00:00:00Z", "updatedAt": "2024-01-01T00:00:00Z" }]
            }
            """);

        var migrated = NewService(); // Load() should back-fill the registry

        Assert.Contains(migrated.Tags, t => t.Name == "legacy");
        Assert.Equal(ColorPalette.Swatches[0], migrated.Tags.Single(t => t.Name == "legacy").Color);
        // Idempotent: a second load finds the tag already registered and does not duplicate it.
        Assert.Single(NewService().Tags, t => t.Name == "legacy");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
