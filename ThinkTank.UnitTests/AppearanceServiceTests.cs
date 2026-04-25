using NUnit.Framework;
using LLMThinkTank.Core.Models;
using LLMThinkTank.Core.Services;

namespace LLMThinkTank.UnitTests;

[TestFixture]
public class AppearanceServiceTests
{
    private SettingsService settings = null!;
    private AppearanceService sut = null!;

    [SetUp]
    public void SetUp()
    {
        settings = new SettingsService();
        sut = new AppearanceService(settings);
    }

    // ── Constructor ─────────────────────────────────────────────────────

    [Test]
    public void Constructor_LoadsFromSettings()
    {
        Assert.That(sut.Mode, Is.TypeOf<AppearanceMode>());
        Assert.That(sut.ControlHeight, Is.GreaterThanOrEqualTo(28).And.LessThanOrEqualTo(60));
        Assert.That(sut.Gutter, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(30));
        Assert.That(sut.BorderRadius, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(24));
    }

    // ── SetMode ─────────────────────────────────────────────────────────

    [Test]
    public void SetMode_ChangesMode()
    {
        sut.SetMode(AppearanceMode.Neon);
        Assert.That(sut.Mode, Is.EqualTo(AppearanceMode.Neon));
    }

    [Test]
    public void SetMode_FiresChangedEvent()
    {
        var fired = false;
        sut.Changed += () => fired = true;
        sut.SetMode(AppearanceMode.Matrix);
        Assert.That(fired, Is.True);
    }

    [Test]
    public void SetMode_SameValue_DoesNotFireChanged()
    {
        sut.SetMode(AppearanceMode.Dark); // already Dark
        var fired = false;
        sut.Changed += () => fired = true;
        sut.SetMode(AppearanceMode.Dark);
        Assert.That(fired, Is.False);
    }

    [Test]
    public void SetMode_PersistsToSettings()
    {
        sut.SetMode(AppearanceMode.Dracula);
        Assert.That(settings.AppearanceTheme, Is.EqualTo("dracula"));
    }

    // ── SetControlHeight ────────────────────────────────────────────────

    [Test]
    public void SetControlHeight_ClampsToMin()
    {
        sut.SetControlHeight(10);
        Assert.That(sut.ControlHeight, Is.EqualTo(28));
    }

    [Test]
    public void SetControlHeight_ClampsToMax()
    {
        sut.SetControlHeight(100);
        Assert.That(sut.ControlHeight, Is.EqualTo(60));
    }

    [Test]
    public void SetControlHeight_AcceptsValidValue()
    {
        sut.SetControlHeight(45);
        Assert.That(sut.ControlHeight, Is.EqualTo(45));
    }

    [Test]
    public void SetControlHeight_SameValue_DoesNotFireChanged()
    {
        sut.SetControlHeight(40); // default is 40
        var fired = false;
        sut.Changed += () => fired = true;
        sut.SetControlHeight(40);
        Assert.That(fired, Is.False);
    }

    [Test]
    public void SetControlHeight_FiresChangedEvent()
    {
        var fired = false;
        sut.Changed += () => fired = true;
        sut.SetControlHeight(50);
        Assert.That(fired, Is.True);
    }

    // ── SetGutter ───────────────────────────────────────────────────────

    [Test]
    public void SetGutter_ClampsToMin()
    {
        sut.SetGutter(-5);
        Assert.That(sut.Gutter, Is.EqualTo(0));
    }

    [Test]
    public void SetGutter_ClampsToMax()
    {
        sut.SetGutter(50);
        Assert.That(sut.Gutter, Is.EqualTo(30));
    }

    [Test]
    public void SetGutter_AcceptsValidValue()
    {
        sut.SetGutter(15);
        Assert.That(sut.Gutter, Is.EqualTo(15));
    }

    [Test]
    public void SetGutter_SameValue_DoesNotFireChanged()
    {
        sut.SetGutter(15); // set to known value first
        var fired = false;
        sut.Changed += () => fired = true;
        sut.SetGutter(15); // same value again
        Assert.That(fired, Is.False);
    }

    // ── SetBorderRadius ─────────────────────────────────────────────────

    [Test]
    public void SetBorderRadius_ClampsToMin()
    {
        sut.SetBorderRadius(-1);
        Assert.That(sut.BorderRadius, Is.EqualTo(0));
    }

    [Test]
    public void SetBorderRadius_ClampsToMax()
    {
        sut.SetBorderRadius(50);
        Assert.That(sut.BorderRadius, Is.EqualTo(24));
    }

    [Test]
    public void SetBorderRadius_AcceptsValidValue()
    {
        sut.SetBorderRadius(12);
        Assert.That(sut.BorderRadius, Is.EqualTo(12));
    }

    [Test]
    public void SetBorderRadius_SameValue_DoesNotFireChanged()
    {
        sut.SetBorderRadius(12); // set to known value first
        var fired = false;
        sut.Changed += () => fired = true;
        sut.SetBorderRadius(12); // same value again
        Assert.That(fired, Is.False);
    }

    // ── ToThemeValue ────────────────────────────────────────────────────

    [TestCase(AppearanceMode.Dark, "dark")]
    [TestCase(AppearanceMode.Light, "light")]
    [TestCase(AppearanceMode.Matrix, "matrix")]
    [TestCase(AppearanceMode.Neon, "neon")]
    [TestCase(AppearanceMode.Dracula, "dracula")]
    [TestCase(AppearanceMode.Aurora, "aurora")]
    [TestCase(AppearanceMode.Mono, "mono")]
    public void ToThemeValue_ReturnsLowercase(AppearanceMode mode, string expected)
    {
        Assert.That(AppearanceService.ToThemeValue(mode), Is.EqualTo(expected));
    }
}
