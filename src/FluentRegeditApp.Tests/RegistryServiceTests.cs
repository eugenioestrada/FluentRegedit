using System.Linq;
using AwesomeAssertions;
using FluentRegeditApp.Models;
using FluentRegeditApp.Services;
using Microsoft.Win32;
using Xunit;

namespace FluentRegeditApp.Tests;

public class RegistryServiceTests
{
    [Fact]
    public void GetSubKeyNames_returns_created_subkeys_sorted()
    {
        using var sandbox = new SandboxFixture();
        using (var key = sandbox.Open())
        {
            key.CreateSubKey("Zeta")?.Dispose();
            key.CreateSubKey("alpha")?.Dispose();
            key.CreateSubKey("Mike")?.Dispose();
        }

        var svc = new RegistryService();
        var names = svc.GetSubKeyNames(RegistryRoot.CurrentUser, sandbox.SubPath).ToList();

        names.Should().BeEquivalentTo(new[] { "alpha", "Mike", "Zeta" }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void GetValues_returns_values_with_kinds_and_synthetic_default()
    {
        using var sandbox = new SandboxFixture();
        using (var key = sandbox.Open())
        {
            key.SetValue("StringVal", "hello", RegistryValueKind.String);
            key.SetValue("DwordVal", 42, RegistryValueKind.DWord);
            key.SetValue("BinaryVal", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
        }

        var svc = new RegistryService();
        var values = svc.GetValues(RegistryRoot.CurrentUser, sandbox.SubPath);

        values.Should().Contain(v => v.Name == "StringVal" && v.Kind == RegistryValueKind.String && (string)v.RawData! == "hello");
        values.Should().Contain(v => v.Name == "DwordVal" && v.Kind == RegistryValueKind.DWord && (int)v.RawData! == 42);
        values.Should().Contain(v => v.Name == "BinaryVal" && v.Kind == RegistryValueKind.Binary);

        values.Should().Contain(v => v.IsDefault);
        values.First().IsDefault.Should().BeTrue();
    }

    [Fact]
    public void OpenKey_returns_null_for_missing_path()
    {
        var svc = new RegistryService();
        var key = svc.OpenKey(RegistryRoot.CurrentUser, @"Software\FluentRegedit\Tests\__definitely_does_not_exist__");
        key.Should().BeNull();
    }
}
