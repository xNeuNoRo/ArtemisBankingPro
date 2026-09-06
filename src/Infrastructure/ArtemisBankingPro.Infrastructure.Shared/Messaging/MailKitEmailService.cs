using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Infrastructure.Shared.Configuration;
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

        try {
            string body = await _renderer.RenderAsync(model, ct);
            MimeMessage mailMessage = BuildMailMessage(recipient, model.Subject, body);
            await SendCoreAsync(mailMessage, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            _logger.LogError(
                ex,
                "No fue posible enviar el correo {EmailType} a {RecipientDomain}; "
                    + "tipo de error {ExceptionType}. La operación financiera no se revierte.",
                typeof(T).Name,
                RedactRecipient(recipient),
                ex.GetType().Name
            );
            throw new EmailSendException(model.Subject, ex);
        }

        _logger.LogInformation(
            "Correo enviado {EmailType} a {RecipientDomain}.",
            typeof(T).Name,
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
        if (!MailboxAddress.TryParse(to, out MailboxAddress? recipient)
            || !string.Equals(recipient.Address, to, StringComparison.OrdinalIgnoreCase)) {
            throw new FormatException("El destinatario de correo no es válido.");
        }

        mailMessage.To.Add(recipient);
        mailMessage.Subject = subject;
        mailMessage.Body = new TextPart("plain") {
            Text = body,
            ContentTransferEncoding = ContentEncoding.QuotedPrintable,
        };
        return mailMessage;
    }

    private async Task SendCoreAsync(MimeMessage mailMessage, CancellationToken ct) {
        using var client = new SmtpClient { Timeout = checked(_options.TimeoutSeconds * 1000) };
        try {
            SecureSocketOptions socketOptions = _options.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;
            await client.ConnectAsync(_host, _options.Port, socketOptions, ct);

            if (!string.IsNullOrWhiteSpace(_options.UserName)) {
                await client.AuthenticateAsync(_options.UserName, _options.Password!, ct);
            }

            await client.SendAsync(mailMessage, ct);
        }
        finally {
            if (client.IsConnected) {
                try {
                    await client.DisconnectAsync(true, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) {
                    _logger.LogDebug(
                        ex,
                        "La desconexión SMTP no pudo completarse ({ExceptionType}).",
                        ex.GetType().Name
                    );
                }
            }
        }
    }

    /// <summary>Reduce el destinatario a su dominio para logs seguros.</summary>
    private static string RedactRecipient(string email) =>
        email.Contains('@') ? $"***@{email.Split('@')[^1]}" : "***";
}
