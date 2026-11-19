using MindAttic.Legion;
using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>
/// TT-T43: Pause, Close and the circuit ending cancel the provider call in flight. CallProvider passes its
/// token all the way to the HTTP request, so a cancelled call stops at once instead of running to completion.
/// </summary>
[TestFixture, NonParallelizable]
public class RunCancellationTests
{
    /// <summary>Never answers: completes only when the request is cancelled.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        public int Requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static (ThinkTankService Service, HangingHandler Http) Wire()
    {
        var http = new HangingHandler();
        var settings = new SettingsService(TestLamp.Settings(), TestLamp.KeysWith(("openai", "sk-test-not-a-key")));
        var psychometrics = new PsychometricProfileService(Path.Combine(TestAssemblySetup.VaultRoot, "psy-" + Guid.NewGuid().ToString("N")));
        return (new ThinkTankService(new LegionClient(new HttpClient(http)), settings, psychometrics), http);
    }

    [Test]
    public void CancellingTheToken_AbortsTheCallInFlight()
    {
        var (service, http) = Wire();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Assert.That(async () => await service.CallProvider("openai", "persona", null, "topic", new List<SharedTurn>(), ct: cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(http.Requests, Is.GreaterThanOrEqualTo(1), "the request was sent, then cancelled");
    }

    [Test]
    public void AnAlreadyCancelledToken_SendsNothing()
    {
        var (service, http) = Wire();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(async () => await service.CallProvider("openai", "persona", null, "topic", new List<SharedTurn>(), ct: cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(http.Requests, Is.Zero);
    }
}
