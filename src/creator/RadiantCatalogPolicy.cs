using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// Pure catalogue contract: no Unity, native calls, world discovery or save state.
internal static class RadiantCatalogPolicy
{
    internal readonly record struct Species(string Token, string Russian)
    {
        internal string Reference => "SlimeDefinition." + Token;
        internal string Key => CatalogKey(Reference, true);
        internal string Search(string localized) =>
            ("лучист лучистый radiant shimmering glowing " + Token + " " + Russian + " " + localized + " " + Reference).ToLowerInvariant();
    }

    // Explicit LO order, not Pedia rank, shared-prefab identity or a fuzzy name match.
    internal static readonly IReadOnlyList<Species> Ordered = Array.AsReadOnly(new[]
    {
        new Species("Pink", "Розовый"),
        new Species("Cotton", "Хлопковый"),
        new Species("Tabby", "Мяуровый"),
        new Species("Phosphor", "Фосфорный"),
        new Species("Angler", "Удильщик"),
        new Species("Rock", "Скалистый"),
        new Species("Batty", "Нетопырёк"),
        new Species("Flutter", "Порхающий"),
        new Species("Ringtail", "Кольцехвостый"),
        new Species("Boom", "Бум"),
        new Species("Honey", "Медовый"),
        new Species("Puddle", "Лужица"),
        new Species("Crystal", "Кристальный"),
        new Species("Hunter", "Охотник"),
        new Species("Fire", "Огненный"),
        new Species("Saber", "Саблезубый"),
        new Species("Tangle", "Вьюнок"),
        new Species("Dervish", "Дервиш"),
        new Species("Yolky", "Слайм-желток"),
        new Species("Sloomber", "Дремотный"),
        new Species("Twin", "Сиамский"),
        new Species("Hyper", "Гиперслайм")
    });

    internal static string CatalogKey(string reference, bool radiant) =>
        reference == null ? null : reference + (radiant ? "|radiant" : "");

    internal static bool TryGet(string token, out Species species)
    {
        foreach (var candidate in Ordered)
            if (string.Equals(candidate.Token, token, StringComparison.Ordinal))
            { species = candidate; return true; }
        species = default;
        return false;
    }

    internal static string Availability(int matches, bool exactIdentity, bool isLargo,
        bool hasAppearance, bool hasPrefab, int componentCount)
    {
        if (matches == 0) return "Нативный базовый тип не зарегистрирован в этой версии.";
        if (matches != 1) return "Неоднозначный нативный базовый тип; создание отключено.";
        if (!exactIdentity) return "Нативная регистрация не совпадает с точным базовым типом.";
        if (isLargo) return "Лучистый каталог принимает только базовый вид, не ларго.";
        if (!hasAppearance) return "У базового вида отсутствует нативный RadiantBase appearance.";
        if (!hasPrefab) return "У базового вида отсутствует нативный префаб.";
        if (componentCount == 0) return "В нативном префабе отсутствует SlimeRadiant.";
        if (componentCount != 1) return "Неоднозначный SlimeRadiant в нативном префабе.";
        return null;
    }
}
