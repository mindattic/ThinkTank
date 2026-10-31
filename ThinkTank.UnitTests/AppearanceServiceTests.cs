using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>
/// AppearanceService reads ThinkTank's Vault settings (LAMP, TT-T27): values come from the settings file,
/// clamped to their ranges; a change saved in Vault shows on the next read.
/// </summary>
[TestFixture]
public class AppearanceServiceTests
{
    private static AppearanceService With(Action<ThinkTankSettings> change, out ThinkTankSettingsSource source)
    {
        source = TestLamp.Settings(change);
        return new AppearanceService(new SettingsService(source, TestLamp.Keys()));
    }

    [Test]
    public void Defaults_AreTodaysDefaults()
    {
        var sut = new AppearanceService(TestLamp.Service());
        Assert.That(sut.Mode, Is.EqualTo(AppearanceMode.Dark));
        Assert.That(sut.ControlHeight, Is.EqualTo(40));
        Assert.That(sut.Gutter, Is.EqualTo(10));
        Assert.That(sut.BorderRadius, Is.EqualTo(10));
    }

    // ── Theme round-trips through persistence (regression) ──────────────
    // ParseMode previously only recognized 13 of the 18 themes; the newer ones
    // (Aurora, Ember, Ocean, Forest, Mono) silently reverted to Dark on reload.

    [TestCase("dark", AppearanceMode.Dark)]
    [TestCase("neon", AppearanceMode.Neon)]
    [TestCase("aurora", AppearanceMode.Aurora)]
    [TestCase("ember", AppearanceMode.Ember)]
    [TestCase("ocean", AppearanceMode.Ocean)]
    [TestCase("forest", AppearanceMode.Forest)]
    [TestCase("mono", AppearanceMode.Mono)]
    public void Mode_RestoresEverySavedTheme(string theme, AppearanceMode expected)
    {
        var sut = With(s => s.Theme = theme, out _);
        Assert.That(sut.Mode, Is.EqualTo(expected));
    }

    [TestCase("not-a-real-theme")]
    [TestCase("3")]
    [TestCase("")]
    public void Mode_UnknownTheme_FallsBackToDark(string theme)
    {
        var sut = With(s => s.Theme = theme, out _);
        Assert.That(sut.Mode, Is.EqualTo(AppearanceMode.Dark));
    }

    [Test]
    public void Values_FollowAChangeSavedInVault()
    {
        var sut = With(_ => { }, out var source);
        source.Store.Update(s => { s.Theme = "dracula"; s.ControlHeight = 45; s.Gutter = 15; s.BorderRadius = 12; });

        Assert.That(sut.Mode, Is.EqualTo(AppearanceMode.Dracula));
        Assert.That(sut.ControlHeight, Is.EqualTo(45));
        Assert.That(sut.Gutter, Is.EqualTo(15));
        Assert.That(sut.BorderRadius, Is.EqualTo(12));
    }

    [TestCase(10, 28)]
    [TestCase(100, 60)]
    public void ControlHeight_IsClamped(int saved, int expected) =>
        Assert.That(With(s => s.ControlHeight = saved, out _).ControlHeight, Is.EqualTo(expected));

    [TestCase(-5, 0)]
    [TestCase(99, 30)]
    public void Gutter_IsClamped(int saved, int expected) =>
        Assert.That(With(s => s.Gutter = saved, out _).Gutter, Is.EqualTo(expected));

    [TestCase(-1, 0)]
    [TestCase(50, 24)]
    public void BorderRadius_IsClamped(int saved, int expected) =>
        Assert.That(With(s => s.BorderRadius = saved, out _).BorderRadius, Is.EqualTo(expected));

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
