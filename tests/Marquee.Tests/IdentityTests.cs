using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Marquee.Booking.Reservations;
using Marquee.Identity.Auth;
using Marquee.Identity.Endpoints;
using Marquee.Tests.Support;

namespace Marquee.Tests;

[Collection(AppCollection.Name)]
public sealed class IdentityTests(MarqueeApp app)
{
    [Fact]
    public async Task A_browser_keeps_its_guest_session_across_reloads()
    {
        var browser = app.CreateBrowser();

        var first = await (await browser.PostAsync("/api/auth/session", null)).Content.ReadFromJsonAsync<SessionResponse>();
        var second = await (await browser.PostAsync("/api/auth/session", null)).Content.ReadFromJsonAsync<SessionResponse>();

        Assert.False(first!.SignedIn);
        Assert.Equal(first.SessionId, second!.SessionId);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
    }

    /// <summary>Seats picked as a guest are not lost by signing up, and the tickets then show up on every device of the account.</summary>
    [Fact]
    public async Task What_a_guest_bought_belongs_to_the_account_after_signing_up_and_shows_on_another_device()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var browser = await app.CreateAdmittedVisitorAsync(showtimeId);
        var held = await browser.HoldSuccessfullyAsync(showtimeId, "A1", "A2");
        await browser.PaySuccessfullyAsync(held.Id, Guid.NewGuid().ToString());
        await Eventually.TrueAsync(async () => (await browser.GetReservationAsync(held.Id)).Status == "confirmed");

        var email = await app.RegisterAsync(browser);
        var phone = await app.SignInAsync(email);

        var onPhone = await phone.GetFromJsonAsync<List<ReservationResponse>>("/api/reservations");
        Assert.Equal(held.Id, Assert.Single(onPhone!).Id);
        Assert.Equal("confirmed", (await browser.GetReservationAsync(held.Id)).Status);
    }

    [Fact]
    public async Task Someone_elses_ticket_is_not_found_signed_in_or_not()
    {
        var showtimeId = await app.ScheduleShowtimeAsync();
        var owner = await app.CreateAdmittedVisitorAsync(showtimeId);
        var held = await owner.HoldSuccessfullyAsync(showtimeId, "A1");
        await app.RegisterAsync(owner);
        var stranger = await app.CreateVisitorAsync();
        var otherCustomer = await app.CreateVisitorAsync();
        await app.RegisterAsync(otherCustomer);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/reservations/{held.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherCustomer.GetAsync($"/api/reservations/{held.Id}")).StatusCode);
    }

    [Fact]
    public async Task Signing_up_always_makes_a_customer_whatever_the_request_says()
    {
        var browser = await app.CreateVisitorAsync();

        var response = await browser.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Sneaky", email = $"sneaky-{Guid.NewGuid():N}@test.local", password = MarqueeApp.Password, role = "admin",
        });
        MarqueeApp.UseSession(browser, (await response.Content.ReadFromJsonAsync<AuthEndpoints.SignInResponse>())!.Session!);
        var me = await browser.GetFromJsonAsync<AuthEndpoints.MeResponse>("/api/me");

        Assert.Equal("customer", me!.Account!.Role);
        Assert.Empty(me.Permissions);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_address_get_the_same_answer_and_both_lock_after_five_tries()
    {
        var email = await app.RegisterAsync(await app.CreateVisitorAsync());
        var unknown = $"nobody-{Guid.NewGuid():N}@test.local";
        var browser = app.CreateBrowser();

        var wrong = await browser.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "not-the-password" });
        var missing = await browser.PostAsJsonAsync("/api/auth/sign-in", new { email = unknown, password = "not-the-password" });
        Assert.Equal("invalid_credentials", await wrong.ErrorCodeAsync());
        Assert.Equal("invalid_credentials", await missing.ErrorCodeAsync());

        for (var i = 0; i < 4; i++)
        {
            await browser.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "not-the-password" });
            await browser.PostAsJsonAsync("/api/auth/sign-in", new { email = unknown, password = "not-the-password" });
        }

        var right = await browser.PostAsJsonAsync("/api/auth/sign-in", new { email, password = MarqueeApp.Password });
        Assert.Equal("too_many_attempts", await right.ErrorCodeAsync());
        Assert.Equal("too_many_attempts", await (await browser.PostAsJsonAsync(
            "/api/auth/sign-in", new { email = unknown, password = "anything-at-all" })).ErrorCodeAsync());
    }

    [Fact]
    public async Task The_session_cookie_is_http_only_strict_and_scoped_to_the_auth_endpoints()
    {
        var response = await app.CreateBrowser().PostAsync("/api/auth/session", null);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_refresh_token_used_twice_ends_the_session()
    {
        var browser = app.CreateBrowser();
        var first = await browser.PostAsync("/api/auth/session", null);
        var stolen = Assert.Single(first.Headers.GetValues("Set-Cookie")).Split(';')[0].Split('=', 2)[1];
        (await browser.PostAsync("/api/auth/refresh", null)).EnsureSuccessStatusCode();

        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.Add("Cookie", $"marquee-session={stolen}");
        var thief = await app.CreateBrowser().SendAsync(replay);

        Assert.Equal("session_revoked", await thief.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Auth_requests_without_the_client_header_are_refused()
    {
        var bare = app.CreateBrowser();
        bare.DefaultRequestHeaders.Remove(AuthEndpoints.ClientHeader);

        var response = await bare.PostAsync("/api/auth/session", null);

        Assert.Equal("client_header_missing", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task A_tampered_token_is_refused()
    {
        var visitor = await app.CreateVisitorAsync();
        var parts = visitor.DefaultRequestHeaders.Authorization!.Parameter!.Split('.');
        var forged = app.CreateBrowser();
        forged.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", $"{parts[0]}.{parts[1]}.{(parts[2][0] == 'A' ? 'B' : 'A')}{parts[2][1..]}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/reservations")).StatusCode);
    }

    [Fact]
    public async Task A_new_staff_member_can_do_nothing_but_choose_a_password()
    {
        var admin = await app.SignInAsAdminAsync();
        var email = $"auditor-{Guid.NewGuid():N}@test.local";
        var created = await (await admin.PostAsJsonAsync("/api/staff", new { fullName = "Yeni Denetçi", email, role = "auditor" }))
            .Content.ReadFromJsonAsync<StaffEndpoints.TemporaryPassword>();

        var member = await app.SignInAsync(email, created!.Password);
        var me = await member.GetFromJsonAsync<AuthEndpoints.MeResponse>("/api/me");

        Assert.True(me!.Account!.MustChangePassword);
        Assert.Empty(me.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/audit/changes")).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_signs_the_other_devices_out_at_once()
    {
        var email = await app.RegisterAsync(await app.CreateVisitorAsync());
        var laptop = await app.SignInAsync(email);
        var phone = await app.SignInAsync(email);

        var changed = await phone.PostAsJsonAsync(
            "/api/account/password", new { currentPassword = MarqueeApp.Password, newPassword = "an entirely new passphrase" });
        MarqueeApp.UseSession(phone, (await changed.Content.ReadFromJsonAsync<SessionResponse>())!);

        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetAsync("/api/reservations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/reservations")).StatusCode);
    }

    [Fact]
    public async Task Two_step_sign_in_needs_a_fresh_code_and_a_recovery_code_works_once()
    {
        var browser = await app.CreateVisitorAsync();
        var email = await app.RegisterAsync(browser);
        var setup = await (await browser.PostAsync("/api/account/two-step/setup", null)).Content
            .ReadFromJsonAsync<AccountEndpoints.TwoStepSetup>();
        var key = Totp.FromBase32(setup!.Secret);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var enabled = await (await browser.PostAsJsonAsync("/api/account/two-step/enable", new { code = Totp.Code(key, step) }))
            .Content.ReadFromJsonAsync<AccountEndpoints.RecoveryCodes>();

        var reused = await SecondStepAsync(email, Totp.Code(key, step));
        var fresh = await SecondStepAsync(email, Totp.Code(key, step + 1));
        var recovery = await SecondStepAsync(email, enabled!.Codes[0]);
        var recoveryAgain = await SecondStepAsync(email, enabled.Codes[0]);

        Assert.Equal("invalid_code", await reused.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        Assert.Equal("invalid_code", await recoveryAgain.ErrorCodeAsync());
    }

    private async Task<HttpResponseMessage> SecondStepAsync(string email, string code)
    {
        var browser = app.CreateBrowser();
        var first = await (await browser.PostAsJsonAsync("/api/auth/sign-in", new { email, password = MarqueeApp.Password }))
            .Content.ReadFromJsonAsync<AuthEndpoints.SignInResponse>();
        Assert.Null(first!.Session);
        return await browser.PostAsJsonAsync("/api/auth/sign-in/second-step", new { ticket = first.TwoStepTicket, code });
    }
}
