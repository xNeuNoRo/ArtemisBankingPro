namespace ArtemisBankingPro.Application.Models.Emails;

/// <summary>Correo de activación de cuenta con enlace (flujo MVC).</summary>
public sealed record AccountActivationModel(string CustomerName, string ActivationLink)
    : IEmailModel
{
    public string Subject => "Activación de cuenta";

    public string TemplateName => "AccountActivation";
}

/// <summary>Correo de restablecimiento de contraseña con enlace (flujo MVC).</summary>
public sealed record PasswordResetModel(string CustomerName, string ResetLink) : IEmailModel
{
    public string Subject => "Restablecimiento de contraseña";

    public string TemplateName => "PasswordReset";
}

/// <summary>Correo de activación de cuenta con token (flujo API).</summary>
public sealed record AccountActivationTokenModel(string CustomerName, string Token) : IEmailModel
{
    public string Subject => "Token de activación de cuenta";

    public string TemplateName => "AccountActivationToken";
}

/// <summary>Correo de restablecimiento de contraseña con token (flujo API).</summary>
public sealed record PasswordResetTokenModel(string CustomerName, string Token) : IEmailModel
{
    public string Subject => "Token de restablecimiento de contraseña";

    public string TemplateName => "PasswordResetToken";
}
