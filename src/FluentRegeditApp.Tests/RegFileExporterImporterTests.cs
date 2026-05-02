using System;
using System.IO;
using System.Text;
using AwesomeAssertions;
using FluentRegeditApp.Models;
using FluentRegeditApp.Services;
using Microsoft.Win32;
using Xunit;

namespace FluentRegeditApp.Tests;

public class RegFileExporterImporterTests
{
    [Fact]
    public void RoundTrip_preserves_all_value_kinds()
    {
        using var sandbox = new SandboxFixture();
        var multi = new[] { "one", "two", "three" };
        var binary = new byte[] { 0x00, 0x01, 0xFE, 0xFF, 0x42 };
        var noneBytes = new byte[] { 0xAA, 0xBB };

        using (var key = sandbox.Open())
        {
            key.SetValue("", "default-value", RegistryValueKind.String);
            key.SetValue("Sz", "hello \"world\" \\ path", RegistryValueKind.String);
            key.SetValue("Expand", @"%SystemRoot%\Temp", RegistryValueKind.ExpandString);
            key.SetValue("Multi", multi, RegistryValueKind.MultiString);
            key.SetValue("Dword", unchecked((int)0xDEADBEEFu), RegistryValueKind.DWord);
            key.SetValue("Qword", unchecked((long)0x0123456789ABCDEFL), RegistryValueKind.QWord);
            key.SetValue("Binary", binary, RegistryValueKind.Binary);
            key.SetValue("None", noneBytes, RegistryValueKind.None);

            using var sub = key.CreateSubKey("Child", writable: true)!;
            sub.SetValue("Inner", 7, RegistryValueKind.DWord);
        }

        var regPath = Path.Combine(Path.GetTempPath(), $"flreg-{Guid.NewGuid():N}.reg");
        try
        {
            var svc = new RegistryService();
            new RegFileExporter(svc).Export(RegistryRoot.CurrentUser, sandbox.SubPath, regPath);

            File.Exists(regPath).Should().BeTrue();
            var firstLine = File.ReadAllText(regPath, Encoding.Unicode).Split("\r\n")[0];
            firstLine.Should().Be("Windows Registry Editor Version 5.00");

            sandbox.DeleteTree();
            Registry.CurrentUser.OpenSubKey(sandbox.SubPath).Should().BeNull();

            var result = new RegFileImporter().Import(regPath);
            result.Errors.Should().BeEmpty();
            result.KeysCreated.Should().BeGreaterThanOrEqualTo(2);
            result.ValuesWritten.Should().BeGreaterThanOrEqualTo(8);

            using var restored = Registry.CurrentUser.OpenSubKey(sandbox.SubPath);
            restored.Should().NotBeNull();

            restored!.GetValue("").Should().Be("default-value");
            restored.GetValue("Sz").Should().Be("hello \"world\" \\ path");
            ((string)restored.GetValue("Expand", null, RegistryValueOptions.DoNotExpandEnvironmentNames)!)
                .Should().Be(@"%SystemRoot%\Temp");
            restored.GetValueKind("Expand").Should().Be(RegistryValueKind.ExpandString);
            ((string[])restored.GetValue("Multi")!).Should().BeEquivalentTo(multi, o => o.WithStrictOrdering());
            ((int)restored.GetValue("Dword")!).Should().Be(unchecked((int)0xDEADBEEFu));
            ((long)restored.GetValue("Qword")!).Should().Be(0x0123456789ABCDEFL);
            ((byte[])restored.GetValue("Binary")!).Should().Equal(binary);
            restored.GetValueKind("None").Should().Be(RegistryValueKind.None);
            ((byte[])restored.GetValue("None")!).Should().Equal(noneBytes);

            using var child = restored.OpenSubKey("Child");
            child.Should().NotBeNull();
            ((int)child!.GetValue("Inner")!).Should().Be(7);
        }
        finally
        {
            if (File.Exists(regPath)) File.Delete(regPath);
        }
    }

    [Fact]
    public void Import_supports_delete_value_and_delete_key_conventions()
    {
        using var sandbox = new SandboxFixture();
        using (var key = sandbox.Open())
        {
            key.SetValue("Keep", "stay", RegistryValueKind.String);
            key.SetValue("Drop", "gone", RegistryValueKind.String);
            using var doomed = key.CreateSubKey("Doomed", writable: true)!;
            doomed.SetValue("X", 1, RegistryValueKind.DWord);
        }

        var regPath = Path.Combine(Path.GetTempPath(), $"flreg-del-{Guid.NewGuid():N}.reg");
        try
        {
            var sb = new StringBuilder();
            sb.Append("Windows Registry Editor Version 5.00\r\n\r\n");
            sb.Append($"[HKEY_CURRENT_USER\\{sandbox.SubPath}]\r\n");
            sb.Append("\"Drop\"=-\r\n\r\n");
            sb.Append($"[-HKEY_CURRENT_USER\\{sandbox.SubPath}\\Doomed]\r\n\r\n");

            File.WriteAllText(regPath, sb.ToString(), new UnicodeEncoding(false, true));

            var result = new RegFileImporter().Import(regPath);
            result.Errors.Should().BeEmpty();
            result.ValuesDeleted.Should().Be(1);
            result.KeysDeleted.Should().Be(1);

            using var key = sandbox.Open(writable: false);
            key.GetValue("Keep").Should().Be("stay");
            key.GetValue("Drop").Should().BeNull();
            key.OpenSubKey("Doomed").Should().BeNull();
        }
        finally
        {
            if (File.Exists(regPath)) File.Delete(regPath);
        }
    }

    [Fact]
    public void ExportValue_writes_only_the_selected_value()
    {
        using var sandbox = new SandboxFixture();
        using (var key = sandbox.Open())
        {
            key.SetValue("Keep", "selected", RegistryValueKind.String);
            key.SetValue("Other", "not exported", RegistryValueKind.String);
            using var child = key.CreateSubKey("Child", writable: true)!;
            child.SetValue("Inner", 1, RegistryValueKind.DWord);
        }

        var regPath = Path.Combine(Path.GetTempPath(), $"flreg-value-{Guid.NewGuid():N}.reg");
        try
        {
            var svc = new RegistryService();
            new RegFileExporter(svc).ExportValue(RegistryRoot.CurrentUser, sandbox.SubPath, "Keep", regPath);

            var text = File.ReadAllText(regPath, Encoding.Unicode);
            text.Should().Contain($"[HKEY_CURRENT_USER\\{sandbox.SubPath}]");
            text.Should().Contain("\"Keep\"=\"selected\"");
            text.Should().NotContain("Other");
            text.Should().NotContain("Child");
        }
        finally
        {
            if (File.Exists(regPath)) File.Delete(regPath);
        }
    }

    [Fact]
    public void ImportSingleValue_writes_one_value_to_target_key()
    {
        using var source = new SandboxFixture();
        using var target = new SandboxFixture();
        using (var key = source.Open())
        {
            key.SetValue("Only", 123, RegistryValueKind.DWord);
            key.SetValue("IgnoredByExport", "ignored", RegistryValueKind.String);
        }

        var regPath = Path.Combine(Path.GetTempPath(), $"flreg-single-import-{Guid.NewGuid():N}.reg");
        try
        {
            var svc = new RegistryService();
            new RegFileExporter(svc).ExportValue(RegistryRoot.CurrentUser, source.SubPath, "Only", regPath);

            var result = new RegFileImporter().ImportSingleValue(regPath, RegistryRoot.CurrentUser, target.SubPath);

            result.Errors.Should().BeEmpty();
            result.ValuesWritten.Should().Be(1);
            using var restored = target.Open(writable: false);
            restored.GetValue("Only").Should().Be(123);
            restored.GetValueKind("Only").Should().Be(RegistryValueKind.DWord);
            restored.GetValue("IgnoredByExport").Should().BeNull();
        }
        finally
        {
            if (File.Exists(regPath)) File.Delete(regPath);
        }
    }

    [Fact]
    public void ImportSingleValue_rejects_multiple_values()
    {
        using var sandbox = new SandboxFixture();
        var regPath = Path.Combine(Path.GetTempPath(), $"flreg-multi-import-{Guid.NewGuid():N}.reg");
        try
        {
            var sb = new StringBuilder();
            sb.Append("Windows Registry Editor Version 5.00\r\n\r\n");
            sb.Append($"[HKEY_CURRENT_USER\\{sandbox.SubPath}]\r\n");
            sb.Append("\"One\"=\"1\"\r\n");
            sb.Append("\"Two\"=\"2\"\r\n\r\n");
            File.WriteAllText(regPath, sb.ToString(), new UnicodeEncoding(false, true));

            var result = new RegFileImporter().ImportSingleValue(regPath, RegistryRoot.CurrentUser, sandbox.SubPath);

            result.Errors.Should().ContainSingle(e => e.Contains("Expected exactly one value entry"));
            result.ValuesWritten.Should().Be(0);
            using var key = sandbox.Open(writable: false);
            key.GetValue("One").Should().BeNull();
            key.GetValue("Two").Should().BeNull();
        }
        finally
        {
            if (File.Exists(regPath)) File.Delete(regPath);
        }
    }
}
