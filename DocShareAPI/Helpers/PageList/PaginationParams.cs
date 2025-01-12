namespace DocShareAPI.Helpers.PageList
{
    public class PaginationParams
    {
        private const int MaxPageSize = 10; // Giới hạn số lượng bản ghi tối đa mỗi trang
        public int PageNumber { get; set; } = 1; // Trang mặc định là 1

        private int _pageSize = 10;
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value > MaxPageSize ? MaxPageSize : value;
        }
    }
}
