using Bunit;
using NUnit.Framework;
using ThinkTank.Shared.Components.Shared;

namespace ThinkTank.UnitTests;

/// <summary>
/// bUnit coverage for ConfirmationDialog — the reusable confirm modal whose
/// public surface is <see cref="ConfirmationDialog.ShowAsync"/> returning a
/// Task&lt;bool&gt;. The TaskCompletionSource semantics are easy to break
/// silently (e.g., flipping Cancel to also resolve true), so pin them here.
/// </summary>
[TestFixture]
public class ConfirmationDialogComponentTests
{
    private Bunit.TestContext ctx = null!;

    [SetUp] public void SetUp() => ctx = new Bunit.TestContext();
    [TearDown] public void TearDown() => ctx.Dispose();

    [Test]
    public void Hidden_UntilShowAsyncCalled()
    {
        var cut = ctx.RenderComponent<ConfirmationDialog>();
        Assert.That(cut.FindAll(".dialog-box"), Is.Empty);
    }

    [Test]
    public async Task ShowAsync_RendersTitleMessageAndConfirmText()
    {
        var cut = ctx.RenderComponent<ConfirmationDialog>();

        var task = cut.InvokeAsync(() => cut.Instance.ShowAsync("Delete?", "This is permanent.", "Remove"));
        cut.Render();

        Assert.That(cut.Find(".dialog-title").TextContent, Is.EqualTo("Delete?"));
        Assert.That(cut.Find(".dialog-message").TextContent, Is.EqualTo("This is permanent."));
        Assert.That(cut.Find("button.start-btn").TextContent, Does.Contain("Remove"));

        // Resolve so the Task isn't orphaned.
        cut.Find("button.reset-btn").Click();
        var result = await task;
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task Confirm_ResolvesTaskTrue_AndHidesDialog()
    {
        var cut = ctx.RenderComponent<ConfirmationDialog>();
        var task = cut.InvokeAsync(() => cut.Instance.ShowAsync("Remove", "OK?", "Yes"));
        cut.Render();

        cut.Find("button.start-btn").Click();

        var result = await task;
        Assert.That(result, Is.True);
        Assert.That(cut.FindAll(".dialog-box"), Is.Empty);
    }

    [Test]
    public async Task Cancel_ResolvesTaskFalse_AndHidesDialog()
    {
        var cut = ctx.RenderComponent<ConfirmationDialog>();
        var task = cut.InvokeAsync(() => cut.Instance.ShowAsync("Remove", "OK?", "Yes"));
        cut.Render();

        cut.Find("button.reset-btn").Click();

        var result = await task;
        Assert.That(result, Is.False);
        Assert.That(cut.FindAll(".dialog-box"), Is.Empty);
    }
}
