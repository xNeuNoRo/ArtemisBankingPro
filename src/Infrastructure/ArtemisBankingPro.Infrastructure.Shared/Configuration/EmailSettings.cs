namespace ArtemisBankingPro.Infrastructure.Shared.Configuration;

/// <summary>
/// SMTP configuration owned by the infrastructure adapter. TLS is mandatory;
/// credentials must be provided together or omitted together.
/// </summary>
public sealed class EmailSettings {
    public const string SectionName = "Email:Smtp";

    public string? Host { get; init; }

    public int Port { get; init; } = 587;

    public bool EnableSsl { get; init; } = true;

    public string? UserName { get; init; }

    public string? Password { get; init; }

    public string? FromAddress { get; init; }

    public string? FromName { get; init; }

    public int TimeoutSeconds { get; init; } = 15;

    public bool IsConfigured() {
        bool hasUserName = !string.IsNullOrWhiteSpace(UserName);
        bool hasPassword = !string.IsNullOrWhiteSpace(Password);

        return !string.IsNullOrWhiteSpace(Host)
            && Port is > 0 and <= 65_535
            && EnableSsl
            && TimeoutSeconds is > 0 and <= 300
            && IsAddress(FromAddress)
            && hasUserName == hasPassword;
    }

    private static bool IsAddress(string? value) {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)) {
            return false;
        }

        try {
            var address = new System.Net.Mail.MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException) {
            return false;
        }
    }
}
