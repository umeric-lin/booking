using Booking.Api.Auth;
using Booking.Api.Errors;
using Booking.Application;
using Booking.Infrastructure;
using Booking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BookingExceptionHandler>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = jwt.CreateSigningKey(),
        ValidateLifetime = true,
        // The default five-minute grace would keep accepting a token well past its expiry.
        ClockSkew = TimeSpan.FromSeconds(30),
    });

builder.Services.AddAuthorization(options =>
{
    // Deny by default: an endpoint is protected unless it opts out with [AllowAnonymous], so a
    // newly added action cannot accidentally ship unauthenticated.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Room Booking API",
        Version = "v1",
        Description =
            "Bookings for conference rooms. Timestamps must be ISO-8601 with an explicit UTC " +
            "offset (for example 2026-01-05T10:00:00Z or 2026-01-05T11:00:00+01:00). " +
            "Ranges are half-open: 10:00-11:00 and 11:00-12:00 may coexist, but 10:30-11:30 " +
            "is rejected with 409.\n\n" +
            "Room listings are public; everything touching reservations needs a bearer token. " +
            "Get one from POST /auth/token, then use the Authorize button.",
    });

    // Serves the <summary> and <response> blocks on the controllers as endpoint descriptions.
    options.IncludeXmlComments(
        Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml"));

    // Left to itself Swashbuckle omits operationId, which client generators depend on.
    // ActionName is the method name with the Async suffix already stripped, and the actions are
    // named so these stay unique across both controllers.
    options.CustomOperationIds(descriptor => descriptor.ActionDescriptor switch
    {
        ControllerActionDescriptor controller => controller.ActionName,
        var action => action.DisplayName,
    });

    // Gives Swagger UI its Authorize button.
    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
    });

    // The reference needs the host document to serialise; without it the requirement is written out
// as an empty object, and an empty security requirement means "authentication optional" — which
// is why Swagger UI would silently send no Authorization header.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = [],
    });
});

var connectionString = builder.Configuration.GetConnectionString("Booking")
                       ?? "Data Source=booking.db;Default Timeout=30";

builder.Services.AddBookingPersistence(connectionString);
builder.Services.AddBookingApplication();

var app = builder.Build();

// Must come before the authorization middleware so domain exceptions raised in actions are
// converted rather than surfacing as bare 500s.
app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Room Booking API v1");
    options.DocumentTitle = "Room Booking API";
});

// After Swagger, which answers and short-circuits first: the documents must stay reachable
// without a token, or a client has no way to discover how to get one. Must also come before
// the controllers so a rejected token becomes a 401 challenge instead of the action running
// without one.
app.UseAuthentication();
app.UseAuthorization();

// So a reviewer who opens the root URL lands on the docs. Marked anonymous because the fallback
// authorization policy otherwise protects the redirect itself.
app.MapGet("/", () => Results.Redirect("/swagger"))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.MapControllers();

await InitializeDatabaseAsync(app);

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    await DatabaseInitializer.InitializeAsync(dbContext);
}

// Exposed so the integration tests can host the real pipeline with WebApplicationFactory.
public partial class Program;