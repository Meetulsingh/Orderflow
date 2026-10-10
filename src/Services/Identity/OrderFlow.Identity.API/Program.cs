using FluentValidation;
using Microsoft.AspNetCore.Identity;
using OrderFlow.Identity.API.Auth;
using OrderFlow.Identity.API.Data;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPostgres<IdentityDbContext>(IdentityDbContext.Schema);
builder.AddMessaging("identity");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

var app = builder.Build();

app.UseServiceDefaults();

var auth = app.MapGroup("/api/auth").WithTags("Auth");

auth.MapPost("/register", (RegisterRequest request, AuthService service, CancellationToken ct) =>
        service.RegisterAsync(request, ct))
    .Validate<RegisterRequest>()
    .WithSummary("Create a customer account and sign in");

auth.MapPost("/login", (LoginRequest request, AuthService service, CancellationToken ct) =>
        service.LoginAsync(request, ct))
    .Validate<LoginRequest>()
    .WithSummary("Exchange credentials for an access + refresh token pair");

auth.MapPost("/refresh", (RefreshRequest request, AuthService service, CancellationToken ct) =>
        service.RefreshAsync(request, ct))
    .Validate<RefreshRequest>()
    .WithSummary("Rotate the refresh token and issue a new access token");

auth.MapPost("/logout", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
    {
        await service.LogoutAsync(request, ct);
        return Results.NoContent();
    })
    .Validate<RefreshRequest>()
    .WithSummary("Revoke a refresh token");

auth.MapGet("/me", (HttpContext http, AuthService service, CancellationToken ct) =>
        service.GetUserAsync(http.User.GetUserId(), ct))
    .RequireAuthorization()
    .WithSummary("Current user profile");

await app.MigrateDatabaseAsync<IdentityDbContext>(db =>
    IdentitySeeder.SeedAsync(db, app.Configuration, app.Services.GetRequiredService<IPasswordHasher<User>>()));

app.Run();
