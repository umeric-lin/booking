using Booking.Api.Contracts;
using Booking.Api.Validation;
using Booking.Application;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

/// <summary>
/// Operations on a single reservation, addressed by its own id rather than by room.
/// </summary>
[ApiController]
[Route("api/reservations")]
[Tags("Reservations")]
[Produces("application/json")]
public sealed class ReservationsController(IBookingService service) : ControllerBase
{
    /// <summary>
    /// Books a room for a time range.
    /// </summary>
    /// <remarks>
    /// The route stays nested under the room because a booking is created <em>in</em> a room, but
    /// the operation lives here: this controller owns everything that creates or changes a
    /// booking, and RoomsController owns only reads of rooms.
    /// <para>
    /// Ranges are half-open: an existing 10:00-11:00 booking does not block 11:00-12:00, but it
    /// does block 10:30-11:30. Timestamps must carry an explicit UTC offset.
    /// </para>
    /// </remarks>
    /// <response code="201">The booking was made.</response>
    /// <response code="400">The range is malformed, reversed, or the title is blank.</response>
    /// <response code="404">No room has that id.</response>
    /// <response code="409">The range overlaps an existing booking in this room.</response>
    [HttpPost("~/api/rooms/{roomId:int}/reservations")]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationResponse>> CreateReservationAsync(
        int roomId,
        [FromBody] CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TimeRangeBinding.TryBind(request.Start, request.End, out var range, ModelState))
        {
            return ValidationProblem(ModelState);
        }

        if (!TitleBinding.TryBind(request.Title, out var title, ModelState))
        {
            return ValidationProblem(ModelState);
        }

        var reservation = await service.CreateReservationAsync(roomId, range, title, cancellationToken);
        var response = ReservationResponse.From(reservation);

        // Linked by route name rather than action name: a typo here fails at startup instead of
        // silently omitting the Location header.
        return CreatedAtRoute("GetReservation", new { reservationId = reservation.Id }, response);
    }

    /// <summary>Gets one reservation.</summary>
    /// <response code="200">The reservation.</response>
    /// <response code="404">No reservation has that id.</response>
    [HttpGet("{reservationId:int}", Name = "GetReservation")]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationResponse>> GetReservationAsync(
        int reservationId,
        CancellationToken cancellationToken)
    {
        var reservation = await service.GetReservationAsync(reservationId, cancellationToken);

        return reservation is null
            ? ReservationNotFound(reservationId)
            : Ok(ReservationResponse.From(reservation));
    }

    /// <summary>
    /// Reschedules a reservation, and optionally retitles it. The new range is checked against
    /// every other booking in the room; overlapping only the reservation being edited is allowed.
    /// </summary>
    /// <response code="200">The updated reservation.</response>
    /// <response code="400">The range is malformed, reversed, or the title is blank.</response>
    /// <response code="404">No reservation has that id.</response>
    /// <response code="409">The new range overlaps a different booking in the same room.</response>
    [HttpPut("{reservationId:int}")]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationResponse>> UpdateReservationAsync(
        int reservationId,
        [FromBody] UpdateReservationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TimeRangeBinding.TryBind(request.Start, request.End, out var range, ModelState))
        {
            return ValidationProblem(ModelState);
        }

        if (!TitleBinding.TryBind(request.Title, out var title, ModelState))
        {
            return ValidationProblem(ModelState);
        }

        var reservation = await service.UpdateReservationAsync(
            reservationId, range, title, cancellationToken);

        return Ok(ReservationResponse.From(reservation));
    }

    /// <summary>Cancels a reservation, freeing the room for that slot.</summary>
    /// <response code="204">The reservation was deleted.</response>
    /// <response code="404">No reservation has that id.</response>
    [HttpDelete("{reservationId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteReservationAsync(int reservationId, CancellationToken cancellationToken)
    {
        await service.DeleteReservationAsync(reservationId, cancellationToken);

        return NoContent();
    }

    private NotFoundObjectResult ReservationNotFound(int reservationId)
        => NotFound(new ProblemDetails
        {
            Title = "Reservation not found.",
            Detail = $"Reservation {reservationId} does not exist.",
            Status = StatusCodes.Status404NotFound,
        });
}