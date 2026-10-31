using Xunit;

namespace Tamp.SpfxCli.Tests;

public sealed class SpfxCliResolverTests
{
    private const string Node = "/usr/bin/node";

    [Fact]
    public void Local_package_runs_through_node_with_its_version()
    {
        using var t = new TempDir();
        var tool = t.Dir("tool");
        var script = t.InstallLocal(tool, "0.1.0-pre.3");

        var r = SpfxCliResolver.TryResolve(tool, Node, null)!;

        Assert.Equal(SpfxCliResolutionSource.LocalPackage, r.Source);
        Assert.Equal(Node, r.Executable);
        Assert.Equal(new[] { Path.GetFullPath(script) }, r.PrefixArguments);
        Assert.Equal("0.1.0-pre.3", r.Version);
    }

    [Fact]
    public void Local_package_is_found_from_a_subdirectory()
    {
        using var t = new TempDir();
        var tool = t.Dir("tool");
        t.InstallLocal(tool);
        var r = SpfxCliResolver.TryResolve(t.Dir("tool", "a", "b"), Node, null)!;
        Assert.Equal(SpfxCliResolutionSource.LocalPackage, r.Source);
    }

    [Fact]
    public void Local_package_beats_a_global_install()
    {
        using var t = new TempDir();
        var tool = t.Dir("tool");
        t.InstallLocal(tool);
        var r = SpfxCliResolver.TryResolve(tool, Node, Path.Combine(t.Root, "global", "spfx"))!;
        Assert.Equal(SpfxCliResolutionSource.LocalPackage, r.Source);
    }

    [Fact]
    public void Without_node_the_local_shim_is_used()
    {
        using var t = new TempDir();
        var tool = t.Dir("tool");
        t.InstallLocal(tool);
        var shim = t.InstallShim(tool);
        var r = SpfxCliResolver.TryResolve(tool, null, null)!;
        Assert.Equal(SpfxCliResolutionSource.Shim, r.Source);
        Assert.Equal(shim, r.Executable);
        Assert.Empty(r.PrefixArguments);
    }

    [Fact]
    public void Global_npm_install_on_windows_layout_runs_through_node()
    {
        using var t = new TempDir();
        var npmDir = t.Dir("npm");
        var shim = Path.Combine(npmDir, "spfx.cmd");
        File.WriteAllText(shim, "");
        var script = TempDir.WritePackage(Path.Combine(npmDir, "node_modules", "@microsoft", "spfx-cli"), "0.1.0-pre.2");

        var r = SpfxCliResolver.TryResolve(t.Dir("elsewhere"), Node, shim)!;

        Assert.Equal(SpfxCliResolutionSource.GlobalPackage, r.Source);
        Assert.Equal(new[] { Path.GetFullPath(script) }, r.PrefixArguments);
        Assert.Equal("0.1.0-pre.2", r.Version);
    }

    [Fact]
    public void Global_npm_install_on_unix_prefix_layout_runs_through_node()
    {
        using var t = new TempDir();
        var bin = t.Dir("prefix", "bin");
        var shim = Path.Combine(bin, "spfx");
        File.WriteAllText(shim, "");
        TempDir.WritePackage(Path.Combine(t.Root, "prefix", "lib", "node_modules", "@microsoft", "spfx-cli"));

        var r = SpfxCliResolver.TryResolve(t.Dir("elsewhere"), Node, shim)!;

        Assert.Equal(SpfxCliResolutionSource.GlobalPackage, r.Source);
    }

    [Fact]
    public void Global_command_with_no_findable_package_is_run_as_a_shim()
    {
        using var t = new TempDir();
        var shim = Path.Combine(t.Dir("bin"), "spfx");
        File.WriteAllText(shim, "");
        var r = SpfxCliResolver.TryResolve(t.Dir("elsewhere"), Node, shim)!;
        Assert.Equal(SpfxCliResolutionSource.Shim, r.Source);
        Assert.Null(r.Version);
    }

    [Fact]
    public void Nothing_installed_returns_null()
    {
        using var t = new TempDir();
        Assert.Null(SpfxCliResolver.TryResolve(t.Dir("empty"), Node, null));
    }

    [Fact]
    public void A_package_with_a_different_name_is_ignored()
    {
        using var t = new TempDir();
        TempDir.WritePackage(Path.Combine(t.Dir("tool"), "node_modules", "@microsoft", "spfx-cli"), name: "evil-package");
        Assert.Null(SpfxCliResolver.TryResolve(Path.Combine(t.Root, "tool"), Node, null));
    }

    [Fact]
    public void A_bin_that_escapes_the_package_is_refused()
    {
        using var t = new TempDir();
        var pkg = Path.Combine(t.Dir("tool"), "node_modules", "@microsoft", "spfx-cli");
        TempDir.WritePackage(pkg, binJson: "\"../../../outside.js\"");
        File.WriteAllText(Path.Combine(t.Root, "tool", "outside.js"), "");
        Assert.Null(SpfxCliResolver.ReadBinScript(pkg, out _));
    }

    [Fact]
    public void String_form_bin_is_accepted()
    {
        using var t = new TempDir();
        var pkg = Path.Combine(t.Dir("tool"), "node_modules", "@microsoft", "spfx-cli");
        var script = TempDir.WritePackage(pkg, binJson: "\"bin/spfx\"");
        Assert.Equal(Path.GetFullPath(script), SpfxCliResolver.ReadBinScript(pkg, out var v));
        Assert.Equal("0.1.0-pre.3", v);
    }

    [Fact]
    public void Malformed_package_json_is_ignored()
    {
        using var t = new TempDir();
        var pkg = t.Dir("pkg");
        File.WriteAllText(Path.Combine(pkg, "package.json"), "{ not json");
        Assert.Null(SpfxCliResolver.ReadBinScript(pkg, out _));
    }

    [Fact]
    public void Missing_bin_script_is_ignored()
    {
        using var t = new TempDir();
        var pkg = t.Dir("pkg");
        TempDir.WritePackage(pkg, writeScript: false);
        Assert.Null(SpfxCliResolver.ReadBinScript(pkg, out _));
    }

    [Fact]
    public void Empty_tool_directory_throws()
    {
        Assert.Throws<ArgumentException>(() => SpfxCliResolver.TryResolve(" ", Node, null));
    }
}
