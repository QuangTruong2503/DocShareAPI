namespace DocShareAPI.Helpers.PageList
{
    public class PaginationParams
    {
        private const int MaxPageSize = 10; // Giới hạn số lượng bản ghi tối đa mỗi trang
        public int PageNumber { get; set; } = 1; // Trang mặc định là 1

        private int _pageSize = 8;
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value <= 0 ? 8 : value > MaxPageSize ? MaxPageSize : value;
        }

        public int ValidPageNumber => PageNumber <= 0 ? 1 : PageNumber;
    }
}
