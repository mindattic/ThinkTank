using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MindAttic.Legion;
using NUnit.Framework;
using ThinkTank.Core.Services;
using ThinkTank.Shared.Components.Pages;

namespace ThinkTank.UnitTests;

/// <summary>
/// bUnit component-render coverage for SettingsAppearance.razor — the only
/// page that's both purely UI-state-driven (no LLM, no chat history) and
/// has a strict contract (theme list size, slider ranges) worth pinning
/// against accidental change. Settings.razor and Chat.razor would require
/// rendering the entire conversation graph, which is out of scope here.
/// </summary>
[TestFixture]
public class SettingsAppearanceComponentTests
{
    private Bunit.TestContext ctx = null!;

    [SetUp]
    public void SetUp()
    {
        var sandbox = MindAtticCredentialStore.CredentialDirectory;
        if (Directory.Exists(sandbox))
        {
            foreach (var f in Directory.EnumerateFiles(sandbox))
                File.Delete(f);
        }

        ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<ThinkTankSettingsService, SettingsService>();
        ctx.Services.AddSingleton<AppearanceService>();
        // SettingsAppearance calls JS interop in OnAfterRenderAsync — accept
        // any invocation rather than asserting the exact JS contract here.
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public void TearDown() => ctx.Dispose();

    [Test]
    public void Renders_WithoutThrowing()
    {
        var cut = ctx.RenderComponent<SettingsAppearance>();
        Assert.That(cut.Find("#appearance-theme"), Is.Not.Null);
    }

    [Test]
    public void ThemeSelect_HasAllEighteenThemes()
    {
        var cut = ctx.RenderComponent<SettingsAppearance>();
        var options = cut.FindAll("#appearance-theme option");
        // 18 themes per README.md "Available Themes" table.
        Assert.That(options.Count, Is.EqualTo(18));
    }

    [Test]
    public void ControlHeightSlider_HasRange28To60()
    {
        var cut = ctx.RenderComponent<SettingsAppearance>();
        var slider = cut.Find("#appearance-control-height");
        Assert.That(slider.GetAttribute("min"), Is.EqualTo("28"));
        Assert.That(slider.GetAttribute("max"), Is.EqualTo("60"));
        Assert.That(slider.GetAttribute("type"), Is.EqualTo("range"));
    }

    [Test]
    public void GutterSlider_HasRange0To30()
    {
        var cut = ctx.RenderComponent<SettingsAppearance>();
        var slider = cut.Find("#appearance-gutter");
        Assert.That(slider.GetAttribute("min"), Is.EqualTo("0"));
        Assert.That(slider.GetAttribute("max"), Is.EqualTo("30"));
    }

    [Test]
    public void BorderRadiusSlider_HasRange0To24()
    {
        var cut = ctx.RenderComponent<SettingsAppearance>();
        var slider = cut.Find("#appearance-border-radius");
        Assert.That(slider.GetAttribute("min"), Is.EqualTo("0"));
        Assert.That(slider.GetAttribute("max"), Is.EqualTo("24"));
    }

    [Test]
    public void ChangingControlHeight_UpdatesAppearanceService()
    {
        var cut = ctx.RenderComponent<SettingsAppearance>();
        var appearance = ctx.Services.GetRequiredService<AppearanceService>();

        cut.Find("#appearance-control-height").Input("48");

        Assert.That(appearance.ControlHeight, Is.EqualTo(48));
    }
}
