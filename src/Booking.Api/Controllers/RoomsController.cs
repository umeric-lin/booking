using Booking.Api.Contracts;
using Booking.Api.Validation;
using Booking.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

/// <summary>
/// Rooms, and the reservations made in them.
/// </summary>
[ApiController]
[Route("api/rooms")]
[Tags("Rooms")]
[Produces("application/json")]
public sealed class RoomsController(IBookingService service) : ControllerBase
{
    /// <summary>
    /// Lists rooms. Without a window, every room. With <c>start</c> and <c>end</c>, only the rooms
    /// that are free for the whole of that window.
    /// </summary>
    /// <remarks>Public: the room list is not sensitive, so no token is required.</remarks>
    /// <response code="200">The matching rooms.</response>
    /// <response code="400">The window is malformed, reversed, or only half-supplied.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<RoomResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<RoomResponse>>> ListAsync(
        [FromQuery] string? start,
        [FromQuery] string? end,
        CancellationToken cancellationToken)
    {
        if (!TimeRangeBinding.TryBindOptional(start, end, out var window, ModelState))
        {
            return ValidationProblem(ModelState);
        }

        var rooms = window is { } range
            ? await service.ListAvailableRoomsAsync(range, cancellationToken)
            : await service.ListRoomsAsync(cancellationToken);

        return Ok(rooms.Select(RoomResponse.From).ToList());
    }

    /// <summary>Gets one room.</summary>
    /// <remarks>Public: the room list is not sensitive, so no token is required.</remarks>
    /// <response code="200">The room.</response>
    /// <response code="404">No room has that id.</response>
    [HttpGet("{roomId:int}")]
    [AllowAnonymous]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomResponse>> GetAsync(int roomId, CancellationToken cancellationToken)
    {
        var room = await service.GetRoomAsync(roomId, cancellationToken);

        return room is null
            ? RoomNotFound(roomId)
            : Ok(RoomResponse.From(room));
    }

    /// <summary>
    /// Lists a room's reservations, optionally limited to those intersecting a window. A
    /// reservation that merely touches the edge of the window is included.
    /// </summary>
    /// <response code="200">The matching reservations, earliest first.</response>
    /// <response code="400">The window is malformed, reversed, or only half-supplied.</response>
    /// <response code="404">No room has that id.</response>
    [HttpGet("{roomId:int}/reservations")]
    [ProducesResponseType<IReadOnlyList<ReservationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ReservationResponse>>> ListReservationsAsync(
        int roomId,
        [FromQuery] string? start,
        [FromQuery] string? end,
        CancellationToken cancellationToken)
    {
        if (!TimeRangeBinding.TryBindOptional(start, end, out var window, ModelState))
        {
            return ValidationProblem(ModelState);
        }

        // No room-existence check here: the service already raises RoomNotFoundException, which the
        // exception handler turns into the same 404. Checking in both places meant the order of
        // the two rules was decided by whichever happened to be written first.
        var reservations = await service.ListReservationsAsync(roomId, window, cancellationToken);

        return Ok(reservations.Select(ReservationResponse.From).ToList());
    }

    private NotFoundObjectResult RoomNotFound(int roomId)
        => NotFound(new ProblemDetails
        {
            Title = "Room not found.",
            Detail = $"Room {roomId} does not exist.",
            Status = StatusCodes.Status404NotFound,
        });
}