using System.Net;
using System.Net.Http.Json;
using Marquee.Common.Security;
using Marquee.Identity.Endpoints;
using Marquee.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Marquee.Tests;

[Collection(AppCollection.Name)]
public sealed class AuthorizationTests(MarqueeApp app)
{
    /// <summary>Anything open to the public is open on purpose: a new endpoint that forgets to say so needs a token.</summary>
    [Fact]
    public void Only_the_programme_and_the_sign_in_endpoints_are_open_without_a_session()
    {
        var open = Endpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText!)
            .Where(route => route.StartsWith("/api", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "/api/auth/refresh", "/api/auth/register", "/api/auth/session", "/api/auth/sign-in", "/api/auth/sign-in/second-step",
                "/api/auth/sign-out", "/api/movies", "/api/movies/{movieId:guid}", "/api/showtimes/{showtimeId:guid}",
            ],
            open);
    }

    [Fact]
    public void Every_staff_endpoint_asks_for_a_permission()
    {
        var staffAreas = new[] { "/api/audit", "/api/programme", "/api/staff" };
        var staffEndpoints = Endpoints().Where(e => staffAreas.Any(area => e.RoutePattern.RawText!.StartsWith(area, StringComparison.Ordinal))).ToList();

        Assert.NotEmpty(staffEndpoints);
        Assert.All(staffEndpoints, endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<Permission>()));
    }

    [Fact]
    public async Task The_audit_trail_is_for_auditors_only()
    {
        var guest = await app.CreateVisitorAsync();
        var customer = await app.CreateVisitorAsync();
        await app.RegisterAsync(customer);
        var (_, programmer) = await app.AddStaffAsync("programmer");
        var (_, auditor) = await app.AddStaffAsync("auditor");

        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/api/audit/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/audit/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await programmer.GetAsync("/api/audit/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync("/api/audit/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateBrowser().GetAsync("/api/audit/changes")).StatusCode);
    }

    [Fact]
    public async Task A_programmer_puts_a_film_on_sale_and_nobody_else_can()
    {
        var (movieId, hallId) = await app.AddMovieAndHallAsync();
        var (_, programmer) = await app.AddStaffAsync("programmer");
        var (_, auditor) = await app.AddStaffAsync("auditor");
        var startsAt = DateTimeOffset.UtcNow.AddDays(2);
        object Request(DateTimeOffset at) => new { movieId, hallId, startsAt = at, basePrice = 150m };

        var byAuditor = await auditor.PostAsJsonAsync("/api/programme/showtimes", Request(startsAt));
        var byProgrammer = await programmer.PostAsJsonAsync("/api/programme/showtimes", Request(startsAt));
        var planned = await byProgrammer.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.Equal(HttpStatusCode.Forbidden, byAuditor.StatusCode);
        Assert.Equal(HttpStatusCode.Created, byProgrammer.StatusCode);
        var showtimeId = Guid.Parse(planned!["id"].ToString()!);
        var visitor = await app.CreateVisitorAsync();
        await Eventually.TrueAsync(async () => (await visitor.GetAsync($"/api/showtimes/{showtimeId}/seats")).IsSuccessStatusCode
            && (await visitor.GetSeatStatusesAsync(showtimeId)).Count == 8);
    }

    /// <summary>A 100-minute film plus 20 minutes to clean the hall: nothing else may start in that hall in between.</summary>
    [Fact]
    public async Task Two_showings_cannot_overlap_in_one_hall_even_when_planned_at_the_same_moment()
    {
        var (movieId, hallId) = await app.AddMovieAndHallAsync(durationMinutes: 100);
        var (_, programmer) = await app.AddStaffAsync("programmer");
        var at = new DateTimeOffset(DateTimeOffset.UtcNow.AddDays(3).Date.AddHours(14), TimeSpan.Zero);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            programmer.PostAsJsonAsync("/api/programme/showtimes", new { movieId, hallId, startsAt = at.AddMinutes(i * 10), basePrice = 150m })));
        var tooEarly = await programmer.PostAsJsonAsync(
            "/api/programme/showtimes", new { movieId, hallId, startsAt = at.AddMinutes(-60), basePrice = 150m });
        var afterCleaning = await programmer.PostAsJsonAsync(
            "/api/programme/showtimes", new { movieId, hallId, startsAt = at.AddMinutes(240), basePrice = 150m });

        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal("hall_busy", await tooEarly.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Created, afterCleaning.StatusCode);
    }

    [Fact]
    public async Task Only_administrators_manage_staff_and_customers_are_not_staff()
    {
        var (_, programmer) = await app.AddStaffAsync("programmer");
        var customer = await app.CreateVisitorAsync();
        var email = await app.RegisterAsync(customer);
        var admin = await app.SignInAsAdminAsync();
        var customerId = (await customer.GetFromJsonAsync<AuthEndpoints.MeResponse>("/api/me"))!.Account!.Id;

        var byProgrammer = await programmer.PostAsJsonAsync("/api/staff", new { fullName = "X", email = "x@test.local", role = "admin" });
        var promoteCustomer = await admin.PutAsJsonAsync($"/api/staff/{customerId}", new { role = "admin" });

        Assert.Equal(HttpStatusCode.Forbidden, byProgrammer.StatusCode);
        Assert.Equal("member_not_found", await promoteCustomer.ErrorCodeAsync());
        Assert.NotNull(email);
    }

    /// <summary>A demotion takes effect on the next request, not when the old token expires.</summary>
    [Fact]
    public async Task A_new_role_takes_effect_at_once_and_deactivation_shuts_the_door()
    {
        var (auditorId, auditor) = await app.AddStaffAsync("auditor");
        var (programmerId, programmer) = await app.AddStaffAsync("programmer");
        var admin = await app.SignInAsAdminAsync();

        (await admin.PutAsJsonAsync($"/api/staff/{auditorId}", new { role = "programmer" })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/staff/{programmerId}", new { isActive = false })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await auditor.GetAsync("/api/audit/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await programmer.GetAsync("/api/programme/halls")).StatusCode);
    }

    [Fact]
    public async Task There_is_always_an_administrator_and_no_one_changes_their_own_access()
    {
        var admin = await app.SignInAsAdminAsync();
        var adminId = (await admin.GetFromJsonAsync<AuthEndpoints.MeResponse>("/api/me"))!.Account!.Id;
        var (secondId, second) = await app.AddStaffAsync("admin");

        var demoteSelf = await admin.PutAsJsonAsync($"/api/staff/{adminId}", new { role = "auditor" });
        var results = await Task.WhenAll(
            admin.PutAsJsonAsync($"/api/staff/{secondId}", new { isActive = false }),
            second.PutAsJsonAsync($"/api/staff/{adminId}", new { isActive = false }));

        Assert.Equal("cannot_change_self", await demoteSelf.ErrorCodeAsync());
        Assert.Contains(results, response => response.IsSuccessStatusCode);

        // Put things back for the other tests, from whichever administrator is still active.
        var survivor = results[0].IsSuccessStatusCode ? admin : second;
        var deactivated = results[0].IsSuccessStatusCode ? secondId : adminId;
        if (results.All(response => response.IsSuccessStatusCode))
        {
            Assert.Fail("Both administrators were deactivated.");
        }

        (await survivor.PutAsJsonAsync($"/api/staff/{deactivated}", new { isActive = true })).EnsureSuccessStatusCode();
    }

    private IEnumerable<RouteEndpoint> Endpoints() =>
        app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
}
