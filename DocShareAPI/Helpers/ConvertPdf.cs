namespace DocShareAPI.Helpers
{
    public class ConvertPdf
    {
        public static string ConvertPdfTitleToJpg(string pdfUrl)
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

            // Tìm vị trí của "/upload/" trong URL
            int uploadIndex = pdfUrl.IndexOf("/upload/", StringComparison.OrdinalIgnoreCase);
            if (uploadIndex == -1)
            {
                throw new ArgumentException("Invalid Cloudinary URL format.", nameof(pdfUrl));
            }

            // Tách URL thành 2 phần: trước và sau "/upload/"
            string baseUrl = pdfUrl.Substring(0, uploadIndex + "/upload/".Length);
            string filePart = pdfUrl.Substring(uploadIndex + "/upload/".Length);

            // Thay thế phần đuôi ".pdf" bằng ".jpg" trong phần file
            string jpgFilePart = filePart.Substring(0, filePart.LastIndexOf(".pdf", StringComparison.OrdinalIgnoreCase)) + ".jpg";

            // Thêm tham số "w_400,q_auto" vào URL
            string optimizedJpgUrl = $"{baseUrl}w_400,q_auto/{jpgFilePart}";

            return optimizedJpgUrl;
        }
        public static string ConvertPdfTitle(string pdfUrl)
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
            string title = pdfUrl.Substring(0, pdfUrl.LastIndexOf(".pdf", StringComparison.OrdinalIgnoreCase));

            return title;
        }
    }
}
