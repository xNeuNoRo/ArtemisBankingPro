using System.Security.Cryptography;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Infrastructure.Shared;
using ArtemisBankingPro.Infrastructure.Shared.Security;
using ArtemisBankingPro.Infrastructure.Shared.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class BusinessClockTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static readonly TimeZoneInfo SantoDomingo =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    [Fact]
    public void Today_MatchesBusinessTimeZoneNotUtcDate() {
        // 2026-08-06 03:00 UTC = 2026-08-05 23:00 en Santo Domingo (UTC-4, sin DST).
        var clock = new BusinessClock(
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 6, 3, 0, 0, TimeSpan.Zero)),
            Options.Create(new BusinessClockOptions())
        );

        clock.Today.Should().Be(new DateOnly(2026, 8, 5));
        clock.NowUtc.Should().Be(new DateTimeOffset(2026, 8, 6, 3, 0, 0, TimeSpan.Zero));
        clock.Now.Offset.Should().Be(TimeSpan.FromHours(-4));
    }

    [Fact]
    public void ToBusinessTime_ConvertsUtcToSantoDomingo() {
        var clock = new BusinessClock(
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 6, 16, 30, 0, TimeSpan.Zero)),
            Options.Create(new BusinessClockOptions())
        );

        DateTimeOffset business = clock.ToBusinessTime(
            new DateTimeOffset(2026, 8, 6, 16, 30, 0, TimeSpan.Zero)
        );

        business.Should().Be(new DateTimeOffset(2026, 8, 6, 12, 30, 0, TimeSpan.FromHours(-4)));
        Assert.Equal(SantoDomingo, clock.BusinessTimeZone);
    }

    [Fact]
    public void BusinessTimeZone_IsConfigurable() {
        var clock = new BusinessClock(
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 6, 3, 0, 0, TimeSpan.Zero)),
            Options.Create(new BusinessClockOptions { TimeZoneId = "UTC" })
        );

        clock.Today.Should().Be(new DateOnly(2026, 8, 6));
    }

    [Fact]
    public void InvalidTimeZone_ThrowsWithClearMessage() {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new BusinessClock(
                new FakeTimeProvider(DateTimeOffset.UtcNow),
                Options.Create(new BusinessClockOptions { TimeZoneId = "Mars/Olympus" })
            )
        );

        exception.Message.Should().Contain("Mars/Olympus");
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

[Collection("SqlServer")]
public sealed class SharedEmailServiceTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static ServiceProvider BuildSharedProvider(
        IReadOnlyDictionary<string, string?>? extraConfiguration = null
    ) {
        var configurationData = new Dictionary<string, string?> {
            ["Email:Smtp:Host"] = "smtp.test.local",
            ["Email:Smtp:Port"] = "587",
            ["Email:Smtp:FromAddress"] = "no-reply@artemis.test",
        };

        if (extraConfiguration is not null) {
            foreach ((string key, string? value) in extraConfiguration) {
                configurationData[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSharedInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void MailKitEmailService_WithoutHost_ReportsNotConfigured() {
        using var provider = BuildSharedProvider(new Dictionary<string, string?> {
            ["Email:Smtp:Host"] = null,
        });

        IEmailService service = provider.GetRequiredService<IEmailService>();
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void MailKitEmailService_WithoutFromAddress_ReportsNotConfigured() {
        using var provider = BuildSharedProvider(new Dictionary<string, string?> {
            ["Email:Smtp:FromAddress"] = null,
        });

        IEmailService service = provider.GetRequiredService<IEmailService>();
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void MailKitEmailService_WithValidConfiguration_Resolves() {
        using var provider = BuildSharedProvider();
        var service = provider.GetRequiredService<IEmailService>();
        Assert.IsType<ArtemisBankingPro.Infrastructure.Shared.Messaging.MailKitEmailService>(service);
        service.IsConfigured.Should().BeTrue();
    }
}

[Collection("SqlServer")]
public sealed class CardSecurityServiceTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private const string FingerprintKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string CvcPepperKey = "ZmUwMTIzNDU2Nzg5YWJjZGVmMDEyMzQ1Njc4OWFiY2Rl";

    private static CardSecurityService CreateService(string? fingerprintKey = FingerprintKey, string? cvcPepperKey = CvcPepperKey) =>
        new(Options.Create(new CardSecurityOptions {
            FingerprintKey = fingerprintKey,
            CvcPepperKey = cvcPepperKey,
        }));

    [Fact]
    public void ComputePanFingerprint_IsDeterministicHex64() {
        var service = CreateService();

        string first = service.ComputePanFingerprint("4111111111111111");
        string second = service.ComputePanFingerprint("4111111111111111");

        first.Should().Be(second);
        first.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputePanFingerprint_NormalizesSeparators() {
        var service = CreateService();

        string without = service.ComputePanFingerprint("4111111111111111");
        string withSeparators = service.ComputePanFingerprint("4111 1111 1111 1111");

        without.Should().Be(withSeparators);
    }

    [Fact]
    public void ComputePanFingerprint_DifferentKeys_DifferentFingerprints() {
        string keyA = CreateService().ComputePanFingerprint("4111111111111111");
        string keyB = CreateService(fingerprintKey: CvcPepperKey).ComputePanFingerprint("4111111111111111");

        keyA.Should().NotBe(keyB);
    }

    [Fact]
    public void VerifyCvc_MatchesOnlyCorrectCvc() {
        var service = CreateService();
        string digest = service.ComputeCvcDigest("859");

        service.VerifyCvc("859", digest).Should().BeTrue();
        service.VerifyCvc("851", digest).Should().BeFalse();
        service.VerifyCvc("", digest).Should().BeFalse();
        service.VerifyCvc("859", "").Should().BeFalse();
        service.VerifyCvc("85", digest).Should().BeFalse();
        service.VerifyCvc("85A", digest).Should().BeFalse();
    }

    [Fact]
    public void ComputeCvcDigest_IsNotPlainSha256OfCvc() {
        var service = CreateService();

        string digest = service.ComputeCvcDigest("859");

        // SHA-256 plano de "859" sería e2a7...; el digest es HMAC con pepper.
        digest.Should().MatchRegex("^[0-9a-f]{64}$");
        digest.Should().NotBe(Convert.ToHexString(SHA256.HashData("859"u8)).ToLowerInvariant());
    }

    [Fact]
    public void MissingKeys_ThrowWithClearMessage() {
        Action act = () => CreateService(fingerprintKey: null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*FingerprintKey*");

        Action act2 = () => CreateService(cvcPepperKey: null);
        act2.Should().Throw<InvalidOperationException>().WithMessage("*CvcPepperKey*");
    }

    [Fact]
    public void ShortKeys_AreRejectedBeforeCardOperations() {
        string shortKey = Convert.ToBase64String(new byte[16]);

        Action fingerprint = () => CreateService(fingerprintKey: shortKey);
        Action cvc = () => CreateService(cvcPepperKey: shortKey);

        fingerprint.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
        cvc.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
    }

    [Fact]
    public void InvalidPan_IsRejectedInsteadOfBeingSilentlyNormalized() {
        var service = CreateService();

        Action act = () => service.ComputePanFingerprint("4111-1111-1111-111A");

        act.Should().Throw<ArgumentException>();
    }
}
