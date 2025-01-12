namespace DocShareAPI.Helpers
{
    public class ConvertPdf
    {
        public static string ConvertPdfToJpg(string pdfUrl)
        {
            if (string.IsNullOrEmpty(pdfUrl))
            {
                throw new ArgumentException("URL cannot be null or empty", nameof(pdfUrl));
            }

            // Kiểm tra xem URL có phải là file PDF hay không
            if (!pdfUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The provided URL is not a PDF file.", nameof(pdfUrl));
            }

            // Thay thế phần đuôi ".pdf" bằng ".jpg"
            string jpgUrl = pdfUrl.Substring(0, pdfUrl.LastIndexOf(".pdf", StringComparison.OrdinalIgnoreCase)) + ".jpg";

            return jpgUrl;
        }
    }
}
