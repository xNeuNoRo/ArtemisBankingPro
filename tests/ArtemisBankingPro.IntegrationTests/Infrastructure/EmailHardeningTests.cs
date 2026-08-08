using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Settings;
using ArtemisBankingPro.Infrastructure.Shared.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class EmailHardeningTests {
    [Fact]
    public void RazorRenderer_WithTraversalTemplateName_Rejects() {
        var renderer = new RazorRenderer();
        var model = new EvilTemplateNameModel();

        Func<Task> act = () => renderer.RenderAsync(model);

        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*no es un nombre de archivo válido*");
    }

    [Fact]
    public void RazorRenderer_WithSeparatorInTemplateName_Rejects() {
        var renderer = new RazorRenderer();
        var model = new SeparatorTemplateNameModel();

        Func<Task> act = () => renderer.RenderAsync(model);

        act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RazorRenderer_WithMissingTemplate_ThrowsWithTemplateName() {
        var renderer = new RazorRenderer();
        var model = new MissingTemplateNameModel();

        Func<Task> act = () => renderer.RenderAsync(model);

        (await act.Should().ThrowAsync<Exception>())
            .WithMessage("*MissingTemplate*");
    }

    [Fact]
    public void BuildMailMessage_SetsFromToSubjectAndPlainTextBody() {
        var service = new MailKitEmailService(
            Options.Create(new EmailSettings {
                Host = "smtp.test.local",
                Port = 587,
                FromAddress = "no-reply@artemis.test",
                FromName = "Artemis Banking",
            }),
            new RazorRenderer(),
            NullLogger<MailKitEmailService>.Instance
        );

        MimeKit.MimeMessage message = service.BuildMailMessage(
            "cliente@example.com",
            "Asunto de prueba",
            "Hola Juan,\n\nSu préstamo fue aprobado."
        );

        var from = Assert.IsType<MimeKit.MailboxAddress>(message.From.Single());
        var to = Assert.IsType<MimeKit.MailboxAddress>(message.To.Single());
        from.Name.Should().Be("Artemis Banking");
        from.Address.Should().Be("no-reply@artemis.test");
        to.Address.Should().Be("cliente@example.com");
        message.Subject.Should().Be("Asunto de prueba");

        MimeKit.TextPart body = Assert.IsType<MimeKit.TextPart>(message.Body);
        body.Text.Should().Contain("Hola Juan");
        body.Text.Should().Contain("préstamo fue aprobado");
        message.TextBody.Should().Contain("Hola Juan");
    }

    [Fact]
    public void BuildMailMessage_FallsBackToAddressWhenNoDisplayName() {
        var service = new MailKitEmailService(
            Options.Create(new EmailSettings {
                Host = "smtp.test.local",
                Port = 587,
                FromAddress = "no-reply@artemis.test",
            }),
            new RazorRenderer(),
            NullLogger<MailKitEmailService>.Instance
        );

        MimeKit.MimeMessage message = service.BuildMailMessage(
            "cliente@example.com",
            "Asunto",
            "Cuerpo"
        );

        var from = Assert.IsType<MimeKit.MailboxAddress>(message.From.Single());
        from.Name.Should().Be("no-reply@artemis.test");
        from.Address.Should().Be("no-reply@artemis.test");
    }

    private sealed class EvilTemplateNameModel : IEmailModel {
        public string Subject => "Evil";

        public string TemplateName => "../../../Templates/Emails/LoanApproved";
    }

    private sealed class SeparatorTemplateNameModel : IEmailModel {
        public string Subject => "Separador";

        public string TemplateName => "sub/dir/AccountActivation";
    }

    private sealed class MissingTemplateNameModel : IEmailModel {
        public string Subject => "Inexistente";

        public string TemplateName => "MissingTemplate";
    }
}
