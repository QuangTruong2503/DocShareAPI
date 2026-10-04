using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocShareAPI.Services;

public record AIExtractedDocument(string Text, bool Truncated, int TotalPages, int ProcessedPages);
public interface IAIDocumentReader { Task<AIExtractedDocument> ReadAsync(string fileUrl, CancellationToken cancellationToken); }

public sealed class AIDocumentReader(IHttpClientFactory clients, ICloudinaryService cloud, OpenAIOptions options) : IAIDocumentReader
{
    public async Task<AIExtractedDocument> ReadAsync(string fileUrl, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "res.cloudinary.com" || uri.UserInfo.Length > 0 || !uri.AbsolutePath.StartsWith($"/{cloud.CloudName}/", StringComparison.Ordinal))
            throw new AIServiceException(422, "AI_INVALID_DOCUMENT_URL", "Đường dẫn tài liệu không hợp lệ.");
        try
        {
            using var response = await clients.CreateClient("ai-documents").GetAsync(AssetDelivery.OriginUrl(cloud, fileUrl), HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) throw new AIServiceException(502, "AI_DOCUMENT_UNAVAILABLE", "Không tải được nội dung tài liệu.");
            if (response.Content.Headers.ContentLength > options.MaxPdfBytes) throw TooLarge();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream(); var block = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(block, token)) > 0)
            {
                if (buffer.Length + count > options.MaxPdfBytes) throw TooLarge();
                await buffer.WriteAsync(block.AsMemory(0, count), token);
            }
            var bytes = buffer.ToArray();
            if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new AIServiceException(422, "AI_INVALID_PDF", "Tài liệu không phải file PDF hợp lệ.");
            using var pdf = PdfDocument.Open(bytes);
            var text = new StringBuilder(); var processed = 0; var truncated = false;
            foreach (var page in pdf.GetPages())
            {
                token.ThrowIfCancellationRequested();
                if (processed >= options.MaxDocumentPages) { truncated = true; break; }
                var pageText = ContentOrderTextExtractor.GetText(page).Replace("\0", "").Trim();
                var remaining = options.MaxDocumentCharacters - text.Length;
                if (pageText.Length + 2 > remaining) { text.Append(pageText.AsSpan(0, Math.Min(pageText.Length, Math.Max(0, remaining)))); processed++; truncated = true; break; }
                text.AppendLine(pageText); text.AppendLine(); processed++;
            }
            if (string.IsNullOrWhiteSpace(text.ToString())) throw new AIServiceException(422, "AI_PDF_NO_TEXT", "PDF không có văn bản đọc được. File scan cần OCR trước khi tóm tắt.");
            return new(text.ToString().Trim(), truncated, pdf.NumberOfPages, processed);
        }
        catch (AIServiceException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AIServiceException(504, "AI_DOCUMENT_TIMEOUT", "Tải tài liệu quá lâu. Vui lòng thử lại."); }
        catch (HttpRequestException) { throw new AIServiceException(502, "AI_DOCUMENT_UNAVAILABLE", "Không tải được nội dung tài liệu."); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { throw new AIServiceException(422, "AI_INVALID_PDF", "Không đọc được PDF. File có thể bị lỗi hoặc được bảo vệ bằng mật khẩu."); }
    }
    private static AIServiceException TooLarge() => new(413, "AI_DOCUMENT_TOO_LARGE", "PDF vượt quá giới hạn 10 MB để tóm tắt.");
}
