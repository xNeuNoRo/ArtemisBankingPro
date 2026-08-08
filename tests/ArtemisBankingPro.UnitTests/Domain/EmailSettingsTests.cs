using ArtemisBankingPro.Domain.Settings;

namespace ArtemisBankingPro.UnitTests.Domain;

public sealed class EmailSettingsTests {
    [Fact]
    public void Defaults_HaveSmtpPortSslAndTimeout() {
        var settings = new EmailSettings();

        settings.Port.Should().Be(587);
        settings.EnableSsl.Should().BeTrue();
        settings.TimeoutSeconds.Should().Be(15);
        settings.Host.Should().BeNull();
        settings.FromAddress.Should().BeNull();
    }

    [Fact]
    public void IsConfigured_RequiresHostAndFromAddress() {
        new EmailSettings { Host = "smtp.local", Port = 587, FromAddress = "a@b.test" }
            .IsConfigured().Should().BeTrue();

        new EmailSettings { Host = "smtp.local", Port = 587 }
            .IsConfigured().Should().BeFalse();

        new EmailSettings { Host = "smtp.local", FromAddress = "a@b.test" }
            .IsConfigured().Should().BeTrue();

        new EmailSettings { Port = 587, FromAddress = "a@b.test" }
            .IsConfigured().Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_RejectsInvalidPort() {
        new EmailSettings { Host = "smtp.local", Port = 0, FromAddress = "a@b.test" }
            .IsConfigured().Should().BeFalse();

        new EmailSettings { Host = "smtp.local", Port = -1, FromAddress = "a@b.test" }
            .IsConfigured().Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_AcceptsMissingCredentials_ForUnauthenticatedServers() {
        new EmailSettings { Host = "smtp.local", Port = 25, FromAddress = "a@b.test" }
            .IsConfigured().Should().BeTrue();
    }
}
