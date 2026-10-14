using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Booking.Api.Validation;

internal static class TitleBinding
{
    public const int MaxLength = 200;

    public static bool TryBind(string? title, out string value, ModelStateDictionary modelState)
    {
        var trimmed = title?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            value = string.Empty;
            modelState.AddModelError("title", "'title' is required and cannot be blank.");
            return false;
        }

        if (trimmed.Length > MaxLength)
        {
            value = string.Empty;
            modelState.AddModelError("title", $"'title' cannot be longer than {MaxLength} characters.");
            return false;
        }

        value = trimmed;
        return true;
    }
}