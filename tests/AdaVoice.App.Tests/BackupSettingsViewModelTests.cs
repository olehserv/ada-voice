using System;
using System.IO;
using AdaVoice.App.ViewModels;
using AdaVoice.Core.Storage;

namespace AdaVoice.App.Tests;

public class BackupSettingsViewModelTests
{
    private static BackupSettingsViewModel NewVm(
        FakeSettingsHost host,
        Func<string?>? pickExportPath = null,
        Func<(string Path, ImportMode Mode)?>? pickImportFile = null,
        Action? confirmAndRestart = null,
        List<string>? errors = null,
        List<string>? infos = null) =>
        new(host,
            pickExportPath ?? (() => null),
            () => Task.FromResult(pickImportFile is null ? null : pickImportFile()),
            () =>
            {
                confirmAndRestart?.Invoke();
                return Task.CompletedTask;
            },
            message =>
            {
                errors?.Add(message);
                return Task.CompletedTask;
            },
            message =>
            {
                infos?.Add(message);
                return Task.CompletedTask;
            });

    [Fact]
    public void Initializes_language_and_last_backup_date_from_the_host()
    {
        var host = new FakeSettingsHost { Language = "uk", LastBackupDate = new DateOnly(2026, 7, 1) };

        var vm = NewVm(host);

        Assert.Equal("uk", vm.Language);
        Assert.Equal(new DateOnly(2026, 7, 1), vm.LastBackupDate);
    }

    [Fact]
    public void Changing_language_saves_and_offers_a_restart()
    {
        var host = new FakeSettingsHost { Language = "en" };
        var restarted = false;
        var vm = NewVm(host, confirmAndRestart: () => restarted = true);

        vm.Language = "pl";

        Assert.Equal("pl", host.Language);
        Assert.Equal(1, host.SaveCount);
        Assert.True(restarted);
    }

    [Fact]
    public void Export_uses_the_picked_path()
    {
        var host = new FakeSettingsHost();
        var vm = NewVm(host, pickExportPath: () => @"C:\exports\out.zip");

        vm.ExportCommand.Execute(null);

        Assert.Equal(@"C:\exports\out.zip", host.ExportedPath);
    }

    [Fact]
    public void Export_does_nothing_when_the_picker_is_cancelled()
    {
        var host = new FakeSettingsHost();
        var vm = NewVm(host, pickExportPath: () => null);

        vm.ExportCommand.Execute(null);

        Assert.Null(host.ExportedPath);
    }

    /// <summary>Review finding 2: export silently drops version recordings (v1 limitation) — the
    /// operator must be told when it happens, not just have it logged.</summary>
    [Fact]
    public void Export_with_dropped_versions_shows_an_info_notice()
    {
        var host = new FakeSettingsHost { NextExportDroppedVersions = 2 };
        var infos = new List<string>();
        var vm = NewVm(host, pickExportPath: () => @"C:\exports\out.zip", infos: infos);

        vm.ExportCommand.Execute(null);

        Assert.Single(infos);
        Assert.Contains("2", infos[0]);
    }

    [Fact]
    public void Export_with_no_dropped_versions_shows_no_notice()
    {
        var host = new FakeSettingsHost { NextExportDroppedVersions = 0 };
        var infos = new List<string>();
        var vm = NewVm(host, pickExportPath: () => @"C:\exports\out.zip", infos: infos);

        vm.ExportCommand.Execute(null);

        Assert.Empty(infos);
    }

    [Fact]
    public void Export_failure_is_surfaced_without_throwing()
    {
        var host = new ThrowingExportSettingsHost();
        var errors = new List<string>();
        var vm = NewVm(host, pickExportPath: () => @"C:\bad\out.zip", errors: errors);

        vm.ExportCommand.Execute(null);

        Assert.Single(errors);
    }

    [Fact]
    public void Import_uses_the_picked_path_and_mode()
    {
        var host = new FakeSettingsHost { NextImportResult = new ImportResult(true, 3, 1) };
        var vm = NewVm(host, pickImportFile: () => (@"C:\imports\in.zip", ImportMode.Merge));

        vm.ImportCommand.Execute(null);

        Assert.Equal((@"C:\imports\in.zip", ImportMode.Merge), host.ImportedWith);
    }

    [Fact]
    public void Import_success_tells_the_operator_a_restart_is_needed()
    {
        var host = new FakeSettingsHost { NextImportResult = new ImportResult(true, 3, 1) };
        var infos = new List<string>();
        var vm = NewVm(host, pickImportFile: () => (@"C:\imports\in.zip", ImportMode.Merge), infos: infos);

        vm.ImportCommand.Execute(null);

        Assert.Contains(infos, i => i.Contains("restart", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Import_does_nothing_when_the_picker_is_cancelled()
    {
        var host = new FakeSettingsHost();
        var vm = NewVm(host, pickImportFile: () => null);

        vm.ImportCommand.Execute(null);

        Assert.Null(host.ImportedWith);
    }

    [Fact]
    public void Import_failure_is_surfaced_without_throwing()
    {
        var host = new FakeSettingsHost
        {
            NextImportResult = new ImportResult(false, 0, 0, ImportErrorCode.NoValidLibraryJson),
        };
        var errors = new List<string>();
        var vm = NewVm(host, pickImportFile: () => (@"C:\bad.zip", ImportMode.Replace), errors: errors);

        vm.ImportCommand.Execute(null);

        Assert.Contains(errors, e => e.Contains("no valid library.json"));
    }

    [Fact]
    public void Import_unexpected_exception_is_surfaced_without_crashing()
    {
        var host = new ThrowingImportSettingsHost();
        var errors = new List<string>();
        var vm = NewVm(host, pickImportFile: () => (@"C:\bad.zip", ImportMode.Replace), errors: errors);

        vm.ImportCommand.Execute(null);

        Assert.Single(errors);
    }

    // A minimal ISettingsHost that throws on Export, to prove the view-model catches it. Must use
    // `override`, not `new` — BackupSettingsViewModel calls Export through the ISettingsHost
    // interface reference, and only a true override participates in that virtual dispatch; `new`
    // would silently keep running the non-throwing base implementation.
    private sealed class ThrowingExportSettingsHost : FakeSettingsHost
    {
        public override int Export(string destinationZipPath) => throw new IOException("disk full");
    }

    // Same reasoning as ThrowingExportSettingsHost, for the Import path: LibraryArchiveService.Import
    // can throw past its own internal try/catch (e.g. InvalidDataException from a corrupt zip entry
    // during extraction, or IOException from a failed Save) — the view-model must not crash on that.
    private sealed class ThrowingImportSettingsHost : FakeSettingsHost
    {
        public override ImportResult Import(string sourceZipPath, ImportMode mode) =>
            throw new IOException("disk full");
    }
}
