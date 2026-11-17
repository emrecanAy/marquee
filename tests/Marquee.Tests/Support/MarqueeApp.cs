using System.Net.Http.Headers;
using System.Net.Http.Json;
using Marquee.Booking.Persistence;
using Marquee.Booking.Reservations;
using Marquee.Catalog.Domain;
using Marquee.Catalog.Features;
using Marquee.Catalog.Persistence;
using Marquee.Identity.Auth;
using Marquee.Identity.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Marquee.Tests.Support;

/// <summary>
/// The whole application running in-process against real PostgreSQL and Redis containers.
/// Row versions, SKIP LOCKED and Lua scripts are the things under test, so nothing is faked.
/// </summary>
public sealed class MarqueeApp : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;

    public IServiceProvider Services => _factory.Services;

    public IConnectionMultiplexer Redis => Services.GetRequiredService<IConnectionMultiplexer>();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            host.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
            host.UseSetting("Identity:SigningKey", "integration-tests-signing-key-0123456789abcdef");
            host.UseSetting("Identity:EncryptionKey", "integration-tests-encryption-key-0123456789ab");
            host.UseSetting("Identity:AdminEmail", AdminEmail);
            host.UseSetting("Identity:AdminPassword", Password);
            // The test client talks plain HTTP; a Secure cookie would never be sent back.
            host.UseSetting("Identity:SecureCookies", "false");
            // Tests open hundreds of sessions from one address; the limits get their own tests elsewhere.
            host.UseSetting("Identity:SignInRequestsPerMinute", "100000");
            host.UseSetting("Identity:GuestSessionsPerMinute", "100000");
            host.UseSetting("Database:MigrateOnStartup", "true");
            host.UseSetting("Database:SeedDemoData", "false");
            host.UseSetting("Outbox:PollInterval", "00:00:00.100");
            // Large enough that the waiting room never interferes with tests about something else.
            host.UseSetting("WaitingRoom:Capacity", "1000");
        });

        // Forces the host to start, which applies the migrations.
        _factory.CreateClient().Dispose();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    public const string AdminEmail = "admin@test.local";
    public const string Password = "correct-horse-battery";

    /// <summary>A browser: a cookie jar and the header the auth endpoints require, but no session yet.</summary>
    public HttpClient CreateBrowser()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthEndpoints.ClientHeader, AuthEndpoints.ClientHeaderValue);
        return client;
    }

    /// <summary>A new anonymous visitor with its own guest session, like a browser opening the site.</summary>
    public async Task<HttpClient> CreateVisitorAsync()
    {
        var client = CreateBrowser();
        var response = await client.PostAsync("/api/auth/session", null);
        response.EnsureSuccessStatusCode();
        UseSession(client, (await response.Content.ReadFromJsonAsync<SessionResponse>())!);
        return client;
    }

    /// <summary>Registers a customer on this browser, which keeps its guest session, and switches to the account's token.</summary>
    public async Task<string> RegisterAsync(HttpClient browser, string? email = null)
    {
        email ??= $"customer-{Guid.NewGuid():N}@test.local";
        var response = await browser.PostAsJsonAsync("/api/auth/register", new { fullName = "Test Customer", email, password = Password });
        response.EnsureSuccessStatusCode();
        UseSession(browser, (await response.Content.ReadFromJsonAsync<AuthEndpoints.SignInResponse>())!.Session!);
        return email;
    }

    /// <summary>Signs in on the given browser (or a new one) and returns it.</summary>
    public async Task<HttpClient> SignInAsync(string email, string password = Password, HttpClient? browser = null)
    {
        browser ??= CreateBrowser();
        var response = await browser.PostAsJsonAsync("/api/auth/sign-in", new { email, password });
        response.EnsureSuccessStatusCode();
        UseSession(browser, (await response.Content.ReadFromJsonAsync<AuthEndpoints.SignInResponse>())!.Session!);
        return browser;
    }

    public Task<HttpClient> SignInAsAdminAsync() => SignInAsync(AdminEmail);

    /// <summary>A member of staff with the given role, added by the administrator, who has replaced the temporary password.</summary>
    public async Task<(Guid Id, HttpClient Api)> AddStaffAsync(string role)
    {
        var admin = await SignInAsAdminAsync();
        var email = $"{role}-{Guid.NewGuid():N}@test.local";
        var response = await admin.PostAsJsonAsync("/api/staff", new { fullName = $"Test {role}", email, role });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<StaffEndpoints.TemporaryPassword>())!;

        var member = await SignInAsync(email, created.Password);
        var changed = await member.PostAsJsonAsync(
            "/api/account/password", new { currentPassword = created.Password, newPassword = Password });
        changed.EnsureSuccessStatusCode();
        UseSession(member, (await changed.Content.ReadFromJsonAsync<SessionResponse>())!);
        return (created.Member.Id, member);
    }

    internal static void UseSession(HttpClient client, SessionResponse session) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

    /// <summary>A visitor who has already passed the waiting room of the given showtime.</summary>
    public async Task<HttpClient> CreateAdmittedVisitorAsync(Guid showtimeId)
    {
        var visitor = await CreateVisitorAsync();
        (await visitor.PostAsync($"/api/showtimes/{showtimeId}/queue", null)).EnsureSuccessStatusCode();
        return visitor;
    }

    /// <summary>Runs the expiry sweep as if every open hold had run out.</summary>
    public async Task ExpireHoldsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var expiry = scope.ServiceProvider.GetRequiredService<ReservationExpiry>();
        while (await expiry.ExpireOverdueAsync(DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None) > 0)
        {
        }
    }

    /// <summary>A film of the given length and an empty hall, for scheduling tests.</summary>
    public async Task<(Guid MovieId, Guid HallId)> AddMovieAndHallAsync(int durationMinutes = 100)
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var movie = new Movie
        {
            Title = $"Test film {Guid.NewGuid():N}", Tagline = "-", Synopsis = "-", Genre = "Drama", Director = "-",
            DurationMinutes = durationMinutes, AgeRating = "All", PosterHue = 0,
        };
        var hall = new Hall { Name = $"Hall {Guid.NewGuid():N}", RowCount = 2, SeatsPerRow = 4, PremiumRowCount = 1 };
        catalog.AddRange(movie, hall);
        await catalog.SaveChangesAsync();
        return (movie.Id, hall.Id);
    }

    /// <summary>
    /// Schedules a showtime through Catalog and waits until Booking has received the event and
    /// created the seats, i.e. until the outbox has done its job.
    /// </summary>
    public async Task<Guid> ScheduleShowtimeAsync(int rows = 2, int seatsPerRow = 4)
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var movie = new Movie
        {
            Title = $"Test film {Guid.NewGuid():N}", Tagline = "-", Synopsis = "-", Genre = "Drama", Director = "-",
            DurationMinutes = 90, AgeRating = "All", PosterHue = 0,
        };
        var hall = new Hall { Name = $"Hall {Guid.NewGuid():N}", RowCount = rows, SeatsPerRow = seatsPerRow, PremiumRowCount = 1 };
        catalog.AddRange(movie, hall);
        scope.ServiceProvider.GetRequiredService<ShowtimeScheduler>()
            .Schedule(movie, hall, DateTimeOffset.UtcNow.AddDays(1), basePrice: 100m);
        await catalog.SaveChangesAsync();

        var showtimeId = await catalog.Showtimes.Where(s => s.MovieId == movie.Id).Select(s => s.Id).SingleAsync();
        var booking = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await Eventually.TrueAsync(() => booking.Screenings.AnyAsync(screening => screening.Id == showtimeId));
        return showtimeId;
    }
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<MarqueeApp>
{
    public const string Name = "app";
}
