namespace TelegramControlStudio.Core.Operations;

public static class PagingPolicy
{
    public const int DefaultPageSize = 100;
    public const int DefaultPageWindow = 5;

    public static int TotalPages(int itemCount, int pageSize = DefaultPageSize)
    {
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        return itemCount <= 0 ? 1 : (int)Math.Ceiling(itemCount / (double)pageSize);
    }

    public static int Clamp(int page, int totalPages) => Math.Clamp(page, 1, Math.Max(totalPages, 1));

    public static (int Skip, int Take) Slice(int page, int pageSize, int itemCount)
    {
        var current = Clamp(page, TotalPages(itemCount, pageSize));
        var skip = (current - 1) * pageSize;
        return (skip, Math.Clamp(itemCount - skip, 0, pageSize));
    }

    public static IReadOnlyList<int> Window(int currentPage, int totalPages, int maxButtons = DefaultPageWindow)
    {
        if (maxButtons <= 0) throw new ArgumentOutOfRangeException(nameof(maxButtons));
        if (totalPages <= 0) return [];

        var current = Clamp(currentPage, totalPages);
        var start = Math.Max(1, current - maxButtons / 2);
        var end = Math.Min(totalPages, start + maxButtons - 1);
        start = Math.Max(1, end - maxButtons + 1);

        var pages = new List<int>(end - start + 1);
        for (var page = start; page <= end; page++) pages.Add(page);
        return pages;
    }
}
