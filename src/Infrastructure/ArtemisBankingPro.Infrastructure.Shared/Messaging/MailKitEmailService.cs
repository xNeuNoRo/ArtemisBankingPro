using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ArtemisBankingPro.Infrastructure.Shared.Messaging;

/// <summary>
/// Envío de correo con MailKit y plantillas Razor.
/// </summary>
public sealed class MailKitEmailService : IEmailService {
    private static readonly SemaphoreSlim SmtpSemaphore = new(1, 1);

    private readonly EmailSettings _options;
    private readonly IRazorRenderer _renderer;
    private readonly ILogger<MailKitEmailService> _logger;
    private readonly string _host;
    private readonly string _fromAddress;

    public bool IsConfigured => _options.IsConfigured();

    public MailKitEmailService(
        IOptions<EmailSettings> options,
        IRazorRenderer renderer,
        ILogger<MailKitEmailService> logger
    ) {
        _options = options.Value;
        _renderer = renderer;
        _logger = logger;

        _host = _options.Host ?? string.Empty;
        _fromAddress = _options.FromAddress ?? string.Empty;
    }

    public async Task SendAsync<T>(string recipient, T model, CancellationToken ct = default)
        where T : IEmailModel {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentNullException.ThrowIfNull(model);

        if (!IsConfigured) {
            throw new EmailSendException(
                model.Subject,
                new InvalidOperationException(
                    "La configuración SMTP no está completa."
                )
            );
        }

        string body = await _renderer.RenderAsync(model, ct);
        MimeMessage mailMessage = BuildMailMessage(recipient, model.Subject, body);

        bool acquired = await SmtpSemaphore.WaitAsync(
            TimeSpan.FromSeconds(_options.TimeoutSeconds),
            ct
        );
        if (!acquired) {
            throw new EmailSendException(
                model.Subject,
                new TimeoutException(
                    "No se pudo obtener turno en el semáforo SMTP dentro del tiempo configurado."
                )
            );
        }

        try {
            await SendCoreAsync(mailMessage, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(
                ex,
                "No fue posible enviar el correo con asunto {Subject} a {Recipient}. "
                    + "La operación financiera no se revierte.",
                model.Subject,
                RedactRecipient(recipient)
            );
            throw new EmailSendException(model.Subject, ex);
        }
        finally {
            SmtpSemaphore.Release();
        }

        _logger.LogInformation(
            "Correo enviado con asunto {Subject} a {Recipient}.",
            model.Subject,
            RedactRecipient(recipient)
        );
    }

    /// <summary>
    /// Construye el mensaje MIME: remitente configurado, destinatario, asunto
    /// y cuerpo en texto plano (los templates del spec son texto plano).
    /// </summary>
    internal MimeMessage BuildMailMessage(string to, string subject, string body) {
        var mailMessage = new MimeMessage();
        mailMessage.From.Add(
            new MailboxAddress(
                string.IsNullOrWhiteSpace(_options.FromName) ? _fromAddress : _options.FromName,
                _fromAddress
            )
        );
        mailMessage.To.Add(MailboxAddress.Parse(to));
        mailMessage.Subject = subject;
        mailMessage.Body = new TextPart("plain") {
            Text = body,
            ContentTransferEncoding = ContentEncoding.QuotedPrintable,
        };
        return mailMessage;
    }

    private async Task SendCoreAsync(MimeMessage mailMessage, CancellationToken ct) {
        using var client = new SmtpClient { Timeout = _options.TimeoutSeconds * 1000 };

        await client.ConnectAsync(
            _host,
            _options.Port,
            _options.EnableSsl
                ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.None,
            ct
        );

        if (!string.IsNullOrWhiteSpace(_options.UserName)) {
            await client.AuthenticateAsync(
                _options.UserName,
                _options.Password ?? string.Empty,
                ct
            );
        }

        await client.SendAsync(mailMessage, ct);
        await client.DisconnectAsync(true, ct);
    }

    /// <summary>Reduce el destinatario a su dominio para logs seguros.</summary>
    private static string RedactRecipient(string email) =>
        email.Contains('@') ? $"***@{email.Split('@')[^1]}" : "***";
}
