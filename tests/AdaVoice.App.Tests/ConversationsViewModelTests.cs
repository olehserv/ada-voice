using AdaVoice.App.ViewModels;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class ConversationsViewModelTests
{
    private static FakePlaybackHost HostWithPhrases() => new()
    {
        Phrases = [
            new PhraseEntry { Id = "p-1", Title = "Intro" },
            new PhraseEntry { Id = "p-2", Title = "Pricing" },
        ],
    };

    [Fact]
    public void Rows_come_from_the_host()
    {
        var host = HostWithPhrases();
        host.Conversations = [new Conversation { Id = "v-1", Name = "Cold call" }];

        var vm = new ConversationsViewModel(host);

        Assert.Equal("Cold call", Assert.Single(vm.Rows).Name);
    }

    [Fact]
    public void Add_creates_a_row_and_persists()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Escalation" };

        vm.AddCommand.Execute(null);

        Assert.Contains(vm.Rows, r => r.Name == "Escalation");
        Assert.Contains(host.Conversations, c => c.Name == "Escalation"); // through the seam
        Assert.Equal("", vm.NewName); // input cleared
    }

    [Fact]
    public void Add_ignores_a_blank_name()
    {
        var vm = new ConversationsViewModel(HostWithPhrases()) { NewName = "   " };

        vm.AddCommand.Execute(null);

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Rename_renames_through_the_seam()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Old" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();

        row.Name = "New";
        vm.RenameCommand.Execute(row);

        Assert.Contains(host.Conversations, c => c.Name == "New");
    }

    /// <summary>Review finding 8: blanking the name used to be silently ignored, leaving the field
    /// blank on screen while storage still had the old name — revert it instead.</summary>
    [Fact]
    public void Renaming_to_blank_reverts_the_field_to_the_persisted_name()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Cold call" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();

        row.Name = "   ";
        vm.RenameCommand.Execute(row);

        Assert.Equal("Cold call", row.Name);
        Assert.Contains(host.Conversations, c => c.Name == "Cold call"); // storage unchanged
    }

    /// <summary>Review finding 9: a bound checkbox that writes straight through would otherwise throw
    /// inside the binding engine (swallowed silently by WPF) when the library refuses writes — the
    /// row exposes IsWritable so the view can disable the control instead.</summary>
    [Fact]
    public void Row_is_not_writable_when_the_library_refuses_writes()
    {
        var host = HostWithPhrases();
        host.Conversations = [new Conversation { Id = "v-1", Name = "Cold call" }];
        host.IsWritable = false;

        var vm = new ConversationsViewModel(host);

        Assert.False(vm.Rows.Single().IsWritable);
    }

    [Fact]
    public void Delete_removes_the_row_and_persists()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Temp" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();

        vm.DeleteCommand.Execute(row);

        Assert.Empty(vm.Rows);
        Assert.Empty(host.Conversations);
    }

    [Fact]
    public void A_rows_addable_phrases_exclude_its_current_members()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();

        row.PhraseToAdd = host.Phrases[0]; // Intro
        row.AddPhraseCommand.Execute(null);

        Assert.DoesNotContain(row.AddablePhrases, p => p.Id == "p-1");
        Assert.Contains(row.AddablePhrases, p => p.Id == "p-2");
    }

    [Fact]
    public void AddPhrase_appends_a_member_and_persists_through_the_seam()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();

        row.PhraseToAdd = host.Phrases[0];
        row.AddPhraseCommand.Execute(null);
        row.PhraseToAdd = host.Phrases[1];
        row.AddPhraseCommand.Execute(null);

        Assert.Equal(["p-1", "p-2"], row.Members.Select(m => m.PhraseId));
        Assert.Equal(["p-1", "p-2"], host.Conversations.Single().PhraseIds);
        Assert.Null(row.PhraseToAdd); // reset after adding
    }

    [Fact]
    public void RemovePhrase_drops_a_member_and_persists()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();
        row.PhraseToAdd = host.Phrases[0];
        row.AddPhraseCommand.Execute(null);
        var member = row.Members.Single();

        row.RemovePhraseCommand.Execute(member);

        Assert.Empty(row.Members);
        Assert.Empty(host.Conversations.Single().PhraseIds);
    }

    [Fact]
    public void MoveUp_and_MoveDown_reorder_members_and_persist()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();
        row.PhraseToAdd = host.Phrases[0]; // p-1
        row.AddPhraseCommand.Execute(null);
        row.PhraseToAdd = host.Phrases[1]; // p-2
        row.AddPhraseCommand.Execute(null);

        row.MoveUpCommand.Execute(row.Members[1]); // p-2 up to first

        Assert.Equal(["p-2", "p-1"], row.Members.Select(m => m.PhraseId));
        Assert.Equal(["p-2", "p-1"], host.Conversations.Single().PhraseIds);

        row.MoveDownCommand.Execute(row.Members[0]); // p-2 back down

        Assert.Equal(["p-1", "p-2"], row.Members.Select(m => m.PhraseId));
    }

    [Fact]
    public void MoveUp_at_the_top_and_MoveDown_at_the_bottom_are_no_ops()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();
        row.PhraseToAdd = host.Phrases[0];
        row.AddPhraseCommand.Execute(null);

        row.MoveUpCommand.Execute(row.Members[0]);
        row.MoveDownCommand.Execute(row.Members[0]);

        Assert.Equal(["p-1"], row.Members.Select(m => m.PhraseId));
    }
}
