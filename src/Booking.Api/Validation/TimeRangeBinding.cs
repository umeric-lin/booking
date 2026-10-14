using System.Globalization;
using System.Text.RegularExpressions;
using Booking.Core;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Booking.Api.Validation;

/// <summary>
/// Binds an optional <c>start</c>/<c>end</c> pair of query or body strings into a
/// <see cref="TimeRange"/>.
/// </summary>
/// <remarks>
/// Booking APIs fail in two ways that are easy to ship and hard to debug: silently interpreting
/// an offset-less timestamp in the server's local zone (so the same request books different
/// instants depending on where the server runs), and accepting a reversed range. Both are caught
/// here and added to <c>ModelState</c>, which the controller returns as a field-keyed 400.
/// </remarks>
internal static partial class TimeRangeBinding
{
    [GeneratedRegex(@"(?:Z|z|[+-]\d{2}:?\d{2})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex UtcOffsetSuffix();

    /// <summary>
    /// Parses a required range from two fields, adding one error per offending field.
    /// </summary>
    /// <returns><c>true</c> when <paramref name="range"/> was populated and no error was added.</returns>
    public static bool TryBind(
        string? start,
        string? end,
        out TimeRange range,
        ModelStateDictionary modelState)
    {
        var parsedStart = TryParseOffset(start, "start", modelState);
        var parsedEnd = TryParseOffset(end, "end", modelState);

        if (parsedStart is null || parsedEnd is null)
        {
            range = default;
            return false;
        }

        if (parsedEnd <= parsedStart)
        {
            modelState.AddModelError("end", "'end' must be strictly after 'start'.");
            range = default;
            return false;
        }

        range = new TimeRange(parsedStart.Value, parsedEnd.Value);
        return true;
    }

    /// <summary>
    /// Parses an optional range: absent values leave <paramref name="range"/> null and succeed.
    /// One present without the other is an error, because a half-specified window silently
    /// returning everything is the kind of bug nobody notices for months.
    /// </summary>
    public static bool TryBindOptional(
        string? start,
        string? end,
        out TimeRange? range,
        ModelStateDictionary modelState)
    {
        if (start is null && end is null)
        {
            range = null;
            return true;
        }

        if (start is null || end is null)
        {
            modelState.AddModelError(
                start is null ? "start" : "end",
                "'start' and 'end' must be supplied together.");

            range = null;
            return false;
        }

        if (!TryBind(start, end, out var parsed, modelState))
        {
            range = null;
            return false;
        }

        range = parsed;
        return true;
    }

    private static DateTimeOffset? TryParseOffset(
        string? value,
        string field,
        ModelStateDictionary modelState)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            modelState.AddModelError(field, $"'{field}' is required.");
            return null;
        }

        var trimmed = value.Trim();

        if (!UtcOffsetSuffix().IsMatch(trimmed))
        {
            modelState.AddModelError(field,
                $"'{trimmed}' has no UTC offset. Use an ISO-8601 value such as " +
                "2026-01-05T10:00:00Z or 2026-01-05T11:00:00+01:00.");

            return null;
        }

        if (!DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            modelState.AddModelError(field, $"'{trimmed}' is not a valid ISO-8601 date and time.");
            return null;
        }

        return parsed;
    }
}