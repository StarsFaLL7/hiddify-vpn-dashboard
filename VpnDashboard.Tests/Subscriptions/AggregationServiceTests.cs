using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VpnDashboard.Data;
using VpnDashboard.Data.Entities;
using VpnDashboard.Options;
using VpnDashboard.Probing;
using VpnDashboard.Services;
using VpnDashboard.Subscriptions;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace VpnDashboard.Tests.Subscriptions;

public class AggregationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;

    public AggregationServiceTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static AggregationOptions AggregationOptions() => new()
    {
        CacheTtlMinutes = 60,
        GroupInsertAfter = 1,
        GroupRules =
        [
            new ConfigGroupRule
            {
                Name = "\U0001F1F7\U0001F1FA\U0001F6F0️ Обход белых списков",
                MatchContains = ["LTE", "Обход", "белые", "белых", "списк"],
                Slug = "bypass",
                ShortName = "Обход БС"
            }
        ]
    };

    private AggregationService CreateService()
    {
        var dbFactory = new SingletonDbContextFactory(_db);
        var aggregation = Create(AggregationOptions());
        var probe = Create(new ProbeOptions { Enabled = false });
        var health = new ServerHealthService(
            dbFactory,
            new ProbeService(
                new NetworkProbe(probe),
                new XrayProbe(probe, NullLogger<XrayProbe>.Instance),
                probe,
                NullLogger<ProbeService>.Instance),
            probe,
            NullLogger<ServerHealthService>.Instance);
        var fetcher = new UpstreamSubscriptionFetcher(
            new HttpClient(),
            new SyntheticIdentity(aggregation, Create(new AdminOptions())),
            aggregation,
            NullLogger<UpstreamSubscriptionFetcher>.Instance);

        return new AggregationService(
            dbFactory, fetcher, new SourceLockRegistry(), health,
            aggregation, probe, NullLogger<AggregationService>.Instance);
    }

    [Fact]
    public async Task GroupedEntries_GetGroupBaseName_WithProviderPrefixAndOriginalFlag()
    {
        var sourceId = Guid.NewGuid();
        var url = "https://provider.test/sub/token";
        _db.SubscriptionSnapshots.Add(new SubscriptionSnapshot
        {
            Id = Guid.NewGuid(),
            SourceKind = SubscriptionSourceKind.Shared,
            SourceId = sourceId,
            SourceUrl = url,
            ContentGzip = SnapshotContent.Compress(
            [
                "vless://11111111-1111-1111-1111-111111111111@1.1.1.1:443?type=tcp#\U0001F1F3\U0001F1F1 Amsterdam",
                "vless://22222222-2222-2222-2222-222222222222@2.2.2.2:443?type=tcp#\U0001F1F7\U0001F1FA LTE Обход МСК",
                "vless://33333333-3333-3333-3333-333333333333@3.3.3.3:443?type=tcp#\U0001F1E9\U0001F1EA Berlin",
                "vless://44444444-4444-4444-4444-444444444444@4.4.4.4:443?type=tcp#Bypass LTE 2"
            ]),
            ConfigCount = 4,
            FetchedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var user = new UserSubscriptions(
            Guid.NewGuid(), "tester",
        [
            new SubscriptionSource(SubscriptionSourceKind.Shared, sourceId, "Test Sub", "Test", url, url, null)
        ]);

        var result = await CreateService().AggregateAsync(user);

        var names = result.Configs.Select(c => SubscriptionEntry.Name(c!)).ToList();

        // Обычные серверы: флаг + префикс провайдера + имя. Записи группы идут блоком
        // после GroupInsertAfter=1 обычных под общим именем: флаг исходного сервера
        // (запасной — флаг из имени правила), префикс провайдера, база правила, номер.
        Assert.Equal(
        [
            "\U0001F1F3\U0001F1F1 [Test] Amsterdam",
            "\U0001F1F7\U0001F1FA [Test] \U0001F6F0️ Обход белых списков #1",
            "\U0001F1F7\U0001F1FA [Test] \U0001F6F0️ Обход белых списков #2",
            "\U0001F1E9\U0001F1EA [Test] Berlin"
        ], names);
    }

    private sealed class SingletonDbContextFactory(AppDbContext db) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => db;
    }
}
