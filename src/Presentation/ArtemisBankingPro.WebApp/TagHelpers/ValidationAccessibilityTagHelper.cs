using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace ArtemisBankingPro.WebApp.TagHelpers;

/// <summary>
/// Keeps MVC validation output associated with its control even when a view
/// does not need to hand-author the same ARIA attributes for every field.
/// </summary>
[HtmlTargetElement("input", Attributes = "asp-for")]
[HtmlTargetElement("select", Attributes = "asp-for")]
[HtmlTargetElement("textarea", Attributes = "asp-for")]
public sealed class ValidationControlAccessibilityTagHelper : TagHelper {
    [HtmlAttributeName("asp-for")]
    public ModelExpression For { get; set; } = default!;

    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override int Order => int.MaxValue;

    public override void Process(TagHelperContext context, TagHelperOutput output) {
        string fieldName = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        string errorId = CreateValidationId(fieldName);

        if (string.Equals(
                output.Attributes["type"]?.Value?.ToString(),
                "hidden",
                StringComparison.OrdinalIgnoreCase
            )) {
            return;
        }

        if (!ViewContext.ViewData.ModelState.TryGetValue(fieldName, out ModelStateEntry? state)
            || state.Errors.Count == 0) {
            return;
        }

        output.Attributes.SetAttribute("aria-invalid", "true");
        string describedBy = output.Attributes["aria-describedby"]?.Value?.ToString() ?? string.Empty;
        if (!describedBy.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(errorId, StringComparer.Ordinal)) {
            output.Attributes.SetAttribute(
                "aria-describedby",
                string.IsNullOrWhiteSpace(describedBy) ? errorId : $"{describedBy} {errorId}"
            );
        }
    }

    private static string CreateValidationId(string fieldName) =>
        $"{TagBuilder.CreateSanitizedId(fieldName, "_")}-error";
}

[HtmlTargetElement("span", Attributes = "asp-validation-for")]
public sealed class ValidationMessageAccessibilityTagHelper : TagHelper {
    [HtmlAttributeName("asp-validation-for")]
    public ModelExpression For { get; set; } = default!;

    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override int Order => int.MaxValue;

    public override void Process(TagHelperContext context, TagHelperOutput output) {
        string fieldName = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        string validationId = TagBuilder.CreateSanitizedId(fieldName, "_") + "-error";
        output.Attributes.SetAttribute("id", validationId);
    }
}
