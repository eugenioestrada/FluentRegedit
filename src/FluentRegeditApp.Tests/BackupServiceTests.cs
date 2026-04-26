using System;
using System.IO;
using System.Text;
using AwesomeAssertions;
using FluentRegeditApp.Models;
using FluentRegeditApp.Services;
using Microsoft.Win32;
using Xunit;

namespace FluentRegeditApp.Tests;

public class BackupServiceTests
{
    [Fact]
    public void CreateSnapshot_writes_reg_file_in_backup_dir()
    {
        using var sandbox = new SandboxFixture();
        using (var key = sandbox.Open())
        {
            key.SetValue("Marker", "hello", RegistryValueKind.String);
        }

        var backupDir = Path.Combine(Path.GetTempPath(), $"flreg-backups-{Guid.NewGuid():N}");
        try
        {
            var exporter = new RegFileExporter(new RegistryService());
            var service = new BackupService(exporter, backupDir);
            service.BackupDirectory.Should().Be(backupDir);
            Directory.Exists(backupDir).Should().BeTrue();

            var snapshotPath = service.CreateSnapshot(RegistryRoot.CurrentUser, sandbox.SubPath, label: "unit");

            File.Exists(snapshotPath).Should().BeTrue();
            Path.GetDirectoryName(snapshotPath).Should().Be(backupDir);
            Path.GetExtension(snapshotPath).Should().Be(".reg");

            var text = File.ReadAllText(snapshotPath, Encoding.Unicode);
            var lines = text.Split("\r\n");
            lines[0].Should().Be("Windows Registry Editor Version 5.00");
            text.Should().Contain($"[HKEY_CURRENT_USER\\{sandbox.SubPath}]");
            text.Should().Contain("\"Marker\"=\"hello\"");
        }
        finally
        {
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, recursive: true);
        }
    }
}
