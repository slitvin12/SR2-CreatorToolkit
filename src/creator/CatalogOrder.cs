using System;

namespace CreatorToolkit;

// Sort keys come from native Pedia.Items, never a translated display name.
internal readonly record struct CatalogOrder(int Category, int Family, int Primary, int Secondary, string Reference) : IComparable<CatalogOrder>
{
    internal const int Unknown = int.MaxValue;
    internal static int CategoryRank(string category) => category switch
    { "Слаймы" => 0, "Ларго" => 1, "Еда" => 2, "Плорты" => 3, "Ресурсы" => 4, _ => 5 };
    internal static int FamilyRank(string category, string family) => category switch
    {
        "Еда" => family switch { "Фрукты" => 0, "Овощи" => 1, "Мясо" => 2, _ => 3 },
        "Ресурсы" => family == "Материалы" ? 0 : 1,
        _ => 0
    };
    public int CompareTo(CatalogOrder other)
    {
        int c = Category.CompareTo(other.Category); if (c != 0) return c;
        c = Family.CompareTo(other.Family); if (c != 0) return c;
        c = Primary.CompareTo(other.Primary); if (c != 0) return c;
        c = Secondary.CompareTo(other.Secondary); if (c != 0) return c;
        return string.Compare(Reference, other.Reference, StringComparison.Ordinal);
    }
}