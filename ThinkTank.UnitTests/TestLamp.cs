using MindAttic.Vault.Credentials;
using MindAttic.Vault.Settings;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>ThinkTank settings and keys over throwaway folders under the assembly's temp Vault roots.</summary>
public static class TestLamp
{
    private static string NewDir(string kind) =>
        Directory.CreateDirectory(Path.Combine(TestAssemblySetup.VaultRoot, kind + "-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>A settings store in its own temp folder.</summary>
    public static JsonSettingsStore<ThinkTankSettings> Store() => new(NewDir("settings"));

    /// <summary>A settings source over its own temp store, optionally pre-saved with changes.</summary>
    public static ThinkTankSettingsSource Settings(Action<ThinkTankSettings>? change = null)
    {
        var store = Store();
        if (change is not null) store.Update(change);
        return new ThinkTankSettingsSource(store);
    }

    /// <summary>A named-key store (keys + assignments) in its own temp roaming root.</summary>
    public static NamedKeyStore KeyStore()
    {
        var root = NewDir("keys");
        return new NamedKeyStore(root, new KeyAssignmentStore(Path.Combine(root, KeyAssignmentStore.Bucket)));
    }

    /// <summary>A key selector over <paramref name="store"/> (a fresh empty one when null).</summary>
    public static KeySelector Keys(NamedKeyStore? store = null, KeyHealthStore? health = null) =>
        new(new FileKeySource(store ?? KeyStore()), health, classifier: ThinkTankKeys.ClassifyLegionFailure);

    /// <summary>A key selector holding one key per (provider, value) pair, named key1, key2, ...</summary>
    public static KeySelector KeysWith(params (string Provider, string Value)[] keys)
    {
        var store = KeyStore();
        var i = 0;
        foreach (var (provider, value) in keys) store.Add(provider, $"key{++i}", value);
        return Keys(store);
    }

    /// <summary>A settings service over temp settings and keys.</summary>
    public static SettingsService Service(Action<ThinkTankSettings>? change = null, KeySelector? keys = null) =>
        new(Settings(change), keys ?? Keys());
}
