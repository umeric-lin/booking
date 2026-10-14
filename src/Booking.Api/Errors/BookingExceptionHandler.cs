using Booking.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Errors;

/// <summary>
/// Translates domain exceptions into RFC 9457 problem details.
/// </summary>
/// <remarks>
/// Booking failures are expected outcomes of a well-used API, not incidents: asking for a slot
/// that is taken should produce a 409 a client can act on, and a 500 would page whoever is on
/// call. Anything not recognised here is logged and reported as a 500 with no detail leaked.
/// </remarks>
internal sealed class BookingExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<BookingExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            // A rule violation is a bad request, not a conflict: nothing is occupied, the caller is
            // simply asking for something outside the limits. Keyed by field so a client can point
            // at the input to change.
            BookingRuleViolationException violation => new ValidationProblemDetails(
                new Dictionary<string, string[]> { [violation.Field] = [violation.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Booking rule violated",
                Type = "https://httpstatuses.io/400",
            },

            ReservationConflictException conflict => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Reservation conflict",
                Detail = conflict.Message,
                Type = "https://httpstatuses.io/409",
                Extensions =
                {
                    ["conflictingReservationId"] = conflict.ConflictingReservation.Id,
                    ["conflictingStart"] = conflict.ConflictingReservation.Start,
                    ["conflictingEnd"] = conflict.ConflictingReservation.End,
                },
            },

            RoomNotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Room not found",
                Detail = notFound.Message,
                Type = "https://httpstatuses.io/404",
            },

            ReservationNotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Reservation not found",
                Detail = notFound.Message,
                Type = "https://httpstatuses.io/404",
            },

            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unexpected error",
                Detail = "An unexpected error occurred while processing the request.",
                Type = "https://httpstatuses.io/500",
            },
        };

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("{Status} for {Method} {Path}: {Detail}",
                problem.Status, httpContext.Request.Method, httpContext.Request.Path, problem.Detail);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}