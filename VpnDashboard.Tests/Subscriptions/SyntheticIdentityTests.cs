using VpnDashboard.Data.Entities;
using VpnDashboard.Options;
using VpnDashboard.Subscriptions;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace VpnDashboard.Tests.Subscriptions;

public class SyntheticIdentityTests
{
    private static SyntheticIdentity NewIdentity() => new(
        Create(new AggregationOptions { HwidSecret = "test-secret" }),
        Create(new AdminOptions { PasswordSalt = "test-salt" }));

    [Fact]
    public void HwidFor_IsPlainLowercaseHex_NoDashes_NoGuidFormat()
    {
        // Провайдеры с HWID-лимитами (например, nginx-правило frservice) режут UUID-формат
        // с дефисами в X-Hwid/X-Device-ID ответом 403; hex-строка без дефисов проходит.
        var hwid = NewIdentity().HwidFor(SubscriptionSourceKind.Shared, Guid.NewGuid());

        Assert.Matches("^[0-9a-f]{16}$", hwid);
    }

    [Fact]
    public void HwidFor_IsStableForSameSource()
    {
        var id = Guid.NewGuid();
        var first = NewIdentity().HwidFor(SubscriptionSourceKind.Shared, id);
        var second = NewIdentity().HwidFor(SubscriptionSourceKind.Shared, id);

        Assert.Equal(first, second);
    }

    [Fact]
    public void HwidFor_DiffersAcrossSourcesAndKinds()
    {
        var identity = NewIdentity();
        var a = identity.HwidFor(SubscriptionSourceKind.Shared, Guid.NewGuid());
        var b = identity.HwidFor(SubscriptionSourceKind.Shared, Guid.NewGuid());
        var c = identity.HwidFor(SubscriptionSourceKind.Manual, Guid.NewGuid());

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
    }
}
