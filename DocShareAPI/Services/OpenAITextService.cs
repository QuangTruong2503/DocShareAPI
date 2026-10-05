using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DocShareAPI.Services;

public interface IOpenAITextService
{
    Task<string> GenerateAsync(string instructions, string input, CancellationToken cancellationToken);
}

public sealed class OpenAITextService(HttpClient http, OpenAIOptions options) : IOpenAITextService
{
    public async Task<string> GenerateAsync(string instructions, string input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey)) throw new AIServiceException(503, "AI_NOT_CONFIGURED", "Chức năng AI chưa được cấu hình.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                request.Content = JsonContent.Create(new { model = options.Model, instructions, input, max_output_tokens = options.MaxOutputTokens, store = false });
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                    var delay = retryAfter ?? TimeSpan.FromMilliseconds(500 * (attempt + 1));
                    // Do not retry earlier than the provider asks, or exceed the request deadline.
                    if (delay > TimeSpan.FromSeconds(5)) throw UpstreamError(response.StatusCode);
                    await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, deadline.Token);
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw UpstreamError(response.StatusCode);
                var body = await response.Content.ReadAsByteArrayAsync(deadline.Token);
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;
                if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
                    throw new AIServiceException(502, "AI_INCOMPLETE_RESPONSE", "AI chưa tạo được câu trả lời đầy đủ. Vui lòng thử lại hoặc rút ngắn nội dung.");
                var texts = new List<string>();
                if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                    foreach (var item in output.EnumerateArray())
                    {
                        if (!item.TryGetProperty("type", out var type) || type.GetString() != "message" || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                        foreach (var part in content.EnumerateArray())
                        {
                            if (!part.TryGetProperty("type", out var kind)) continue;
                            if (kind.GetString() == "refusal") throw new AIServiceException(422, "AI_REFUSED", "AI không thể hỗ trợ nội dung yêu cầu này.");
                            if (kind.GetString() == "output_text" && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) texts.Add(text.GetString()!);
                        }
                    }
                var result = string.Join("\n", texts).Trim();
                if (result.Length == 0) throw new AIServiceException(502, "AI_EMPTY_RESPONSE", "AI không trả về nội dung. Vui lòng thử lại.");
                return result;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AIServiceException(504, "AI_TIMEOUT", "AI phản hồi quá lâu. Vui lòng thử lại."); }
        catch (HttpRequestException) { throw new AIServiceException(502, "AI_CONNECTION_FAILED", "Không kết nối được dịch vụ AI. Vui lòng thử lại."); }
        catch (JsonException) { throw new AIServiceException(502, "AI_INVALID_RESPONSE", "Dịch vụ AI trả về dữ liệu không hợp lệ."); }
        catch (InvalidOperationException) { throw new AIServiceException(502, "AI_INVALID_RESPONSE", "Dịch vụ AI trả về dữ liệu không hợp lệ."); }
    }

    private static AIServiceException UpstreamError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => new(429, "AI_RATE_LIMITED", "Dịch vụ AI đang đạt giới hạn sử dụng. Vui lòng thử lại sau."),
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(503, "AI_CONFIGURATION_ERROR", "Cấu hình truy cập dịch vụ AI cần được kiểm tra."),
        _ => new(502, "AI_UPSTREAM_ERROR", "Dịch vụ AI tạm thời không xử lý được yêu cầu.")
    };
}
