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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
