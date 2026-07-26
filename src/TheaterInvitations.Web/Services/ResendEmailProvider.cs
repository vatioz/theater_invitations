using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TheaterInvitations.Web.Services;

public interface IEmailProvider
{
    Task<EmailProviderResult> SendAsync(EmailProviderMessage message, CancellationToken cancellationToken);
}

public sealed record EmailProviderMessage(string From, string ReplyTo, string To, string Subject, string Html, string Text, string IdempotencyKey);
public sealed record EmailProviderResult(bool IsAccepted, bool IsTransientFailure, string? ProviderMessageId, string? FailureCategory);

public sealed class ResendOptions
{
    public string? ApiKey { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.resend.com";
}

public sealed class PublicAppOptions { public string? BaseUrl { get; set; } }

public sealed class ResendEmailProvider(HttpClient client, IConfiguration configuration) : IEmailProvider
{
    public async Task<EmailProviderResult> SendAsync(EmailProviderMessage message, CancellationToken cancellationToken)
    {
        var options = configuration.GetSection("Resend").Get<ResendOptions>() ?? new ResendOptions();
        if (string.IsNullOrWhiteSpace(options.ApiKey)) return new EmailProviderResult(false, false, null, "provider-not-configured");
        client.BaseAddress = new Uri(options.ApiBaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/emails");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", message.IdempotencyKey);
        request.Content = JsonContent.Create(new { from = message.From, to = new[] { message.To }, reply_to = message.ReplyTo, subject = message.Subject, html = message.Html, text = message.Text });
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ResendResponse>(cancellationToken: cancellationToken);
                return new EmailProviderResult(true, false, result?.Id, null);
            }
            return new EmailProviderResult(false, response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500, null, response.StatusCode == HttpStatusCode.TooManyRequests ? "provider-rate-limited" : "provider-rejected");
        }
        catch (HttpRequestException) { return new EmailProviderResult(false, true, null, "provider-network-error"); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new EmailProviderResult(false, true, null, "provider-timeout"); }
    }

    private sealed class ResendResponse { [JsonPropertyName("id")] public string? Id { get; set; } }
}
