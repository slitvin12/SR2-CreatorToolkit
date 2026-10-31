using System;
using System.Collections.Generic;

namespace CreatorToolkit;

// 0.8.1 atlas page (approved mockup v2): seven player-facing groups of layer chips over the
// unchanged map layers. MapTools.Categories stay the storage and the native map panel keys;
// a chip is either a whole layer or one find kind inside a shared layer («Дроны» = research
// and phantom route drones, «Редкое» = statues, gates, receivers, slimes and decor).
// Pure: no Unity, unit-tested offline.
internal static class AtlasGroupPolicy
{
    internal sealed class Chip
    {
        internal readonly string Id,Title,One,Layer,Art;
        internal readonly bool Progress;
        internal Chip(string id,string title,string one,string layer,bool progress,string art="")
        {Id=id;Title=title;One=one;Layer=layer;Progress=progress;Art=art;}
    }
    internal sealed class Group
    {
        internal readonly string Title;
        internal readonly Chip[] Chips;
        internal Group(string title,params Chip[] chips){Title=title;Chips=chips;}
    }
    internal const string Rare="Редкое",Drones="Дроны",Own="Мои метки";
    // Art: "" = the layer icon, "role:X" = a NativeIconPolicy role, otherwise a MapRareIcons kind.
    internal static readonly Group[] Groups=
    {
        new("Сокровища",
            new Chip("pods","Сокровищницы","Сокровищница","Капсулы",true),
            new Chip("gordo","Гордо","Гордо","Гордо",true),
            new Chip("drones","Поисковые дроны","Поисковый дрон",Drones,true),
            new Chip("ghosts","Призрачные дроны","Призрачный дрон",Drones,true),
            new Chip("statues","Статуи для плортов","Статуя для плорта",Rare,true,"Статуя для плорта")),
        new("Пути",
            new Chip("teleports","Телепорты","Телепорт","Телепорты",true),
            new Chip("nodes","Узлы карты","Узел карты","Ноды карты",true),
            new Chip("doors","Двери","Дверь","Двери",true),
            new Chip("gates","Врата плортов","Врата плортов",Rare,true,"Врата плортов"),
            new Chip("mechanisms","Механизмы","Механизм","Механизмы",true)),
        new("Ресурсы",
            new Chip("valuables","Ценное","Ценный ресурс","Ресурсы: ценные",false,"Странный алмаз"),
            new Chip("materials","Материалы","Материал","Ресурсы",false),
            new Chip("plorts","Плорты","Плорт","Плорты",false),
            new Chip("receivers","Приёмники плортов","Приёмник плортов",Rare,true,"Приёмник плортов"),
            new Chip("pots","Горшочки","Горшочек","Горшочки",false)),
        new("Еда",
            new Chip("food","Еда","Еда","Еда",false)),
        new("Слаймы",
            new Chip("spawners","Места появления","Место появления","Спавнеры",false),
            new Chip("radiant","Лучистые слаймы","Лучистый слайм",Rare,false,"Лучистый слайм"),
            new Chip("rareslimes","Редкие слаймы","Редкий слайм",Rare,false,"Золотой слайм")),
        new("Метки игры",
            new Chip("decor","Декор","Декор",Rare,false,"Декор")),
        new("Мои метки",
            new Chip("own","Мои метки","Своя метка",Own,false)),
    };
    private static Dictionary<string,Chip> _byId;
    private static Dictionary<string,int> _groupOf,_order;
    private static Dictionary<string,Chip[]> _byLayer;
    private static void Index()
    {
        if(_byId!=null)return;
        var byId=new Dictionary<string,Chip>(StringComparer.Ordinal);
        var groupOf=new Dictionary<string,int>(StringComparer.Ordinal);
        var order=new Dictionary<string,int>(StringComparer.Ordinal);
        var layers=new Dictionary<string,List<Chip>>(StringComparer.Ordinal);
        for(int g=0;g<Groups.Length;g++)
            foreach(var chip in Groups[g].Chips)
            {
                byId[chip.Id]=chip;groupOf[chip.Id]=g;order[chip.Id]=order.Count;
                if(!layers.TryGetValue(chip.Layer,out var list))layers[chip.Layer]=list=new List<Chip>();
                list.Add(chip);
            }
        var byLayer=new Dictionary<string,Chip[]>(StringComparer.Ordinal);
        foreach(var pair in layers)byLayer[pair.Key]=pair.Value.ToArray();
        _groupOf=groupOf;_order=order;_byLayer=byLayer;_byId=byId;
    }
    internal static Chip Find(string id){Index();return id!=null&&_byId.TryGetValue(id,out var chip)?chip:null;}
    internal static int GroupOf(string id){Index();return id!=null&&_groupOf.TryGetValue(id,out int g)?g:-1;}
    // List order: groups, then chips as declared; an unknown chip sorts last.
    internal static int Order(string id){Index();return id!=null&&_order.TryGetValue(id,out int o)?o:int.MaxValue;}
    internal static Chip[] Siblings(string layer){Index();return layer!=null&&_byLayer.TryGetValue(layer,out var chips)?chips:Array.Empty<Chip>();}
    internal static bool Shared(string layer)=>Siblings(layer).Length>1;

    // The chip of one map point. Every point of a known layer lands on exactly one chip;
    // an unknown «Редкое» kind stays reachable through «Декор».
    internal static string ChipOf(string category,string filterKind,bool ghost)
    {
        category??="";
        if(category==Drones)return ghost?"ghosts":"drones";
        if(category==Rare)return RareChip(filterKind);
        var chips=Siblings(category);
        return chips.Length==1?chips[0].Id:"";
    }
    private static bool Starts(string text,string prefix)=>text.StartsWith(prefix,StringComparison.Ordinal);
    internal static string RareChip(string kind)
    {
        kind=(kind??"").Trim();
        if(Starts(kind,"Статуя для плорта"))return "statues";
        if(Starts(kind,"Врата плортов")||Starts(kind,"Теневые врата"))return "gates";
        if(Starts(kind,"Приёмник плортов")||Starts(kind,"Сборщик теневых плортов"))return "receivers";
        if(Starts(kind,"Лучистый слайм"))return "radiant";
        if(Starts(kind,"Золотой слайм")||Starts(kind,"Слайм удачи"))return "rareslimes";
        return "decor";
    }

    // A point is shown when its layer is on and its own chip is not hidden.
    internal static bool Visible(string category,string chip,ICollection<string> layers,ICollection<string> hidden)=>
        layers.Contains(category??"")&&(string.IsNullOrEmpty(chip)||!hidden.Contains(chip));
    internal static bool Active(Chip chip,ICollection<string> layers,ICollection<string> hidden)=>
        chip!=null&&layers.Contains(chip.Layer)&&!hidden.Contains(chip.Id);
    // One click per chip. A shared layer switched on by one chip shows only that chip; the
    // last sibling switched off turns the whole layer off and forgets the hidden marks.
    internal static void Toggle(Chip chip,ICollection<string> layers,ICollection<string> hidden)
    {
        if(chip==null)return;
        var siblings=Siblings(chip.Layer);
        if(Active(chip,layers,hidden))
        {
            if(siblings.Length<=1){layers.Remove(chip.Layer);return;}
            hidden.Add(chip.Id);
            foreach(var s in siblings)if(!hidden.Contains(s.Id))return;
            layers.Remove(chip.Layer);
            foreach(var s in siblings)hidden.Remove(s.Id);
            return;
        }
        if(!layers.Contains(chip.Layer))
        {
            layers.Add(chip.Layer);
            foreach(var s in siblings)if(s!=chip)hidden.Add(s.Id);
        }
        hidden.Remove(chip.Id);
    }
    // The native map panel switches whole layers: every sibling chip follows.
    internal static void SetLayer(string layer,bool on,ICollection<string> layers,ICollection<string> hidden)
    {
        if(string.IsNullOrEmpty(layer))return;
        if(on)layers.Add(layer);else layers.Remove(layer);
        foreach(var s in Siblings(layer))hidden.Remove(s.Id);
    }

    // A whole group from the native map «Слои карты» panel: every chip follows the switch.
    internal static void SetGroup(Group group,bool on,ICollection<string> layers,ICollection<string> hidden)
    {
        if(group==null)return;
        foreach(var chip in group.Chips)if(Active(chip,layers,hidden)!=on)Toggle(chip,layers,hidden);
    }
    internal static int ActiveCount(Group group,ICollection<string> layers,ICollection<string> hidden)
    {
        int count=0;
        if(group!=null)foreach(var chip in group.Chips)if(Active(chip,layers,hidden))count++;
        return count;
    }

    // One-click sets of the «Слои карты» panel (mockup 06). Own markers stay in every set.
    internal sealed class Preset
    {
        internal readonly string Title;
        internal readonly string[] Chips;
        internal Preset(string title,params string[] chips){Title=title;Chips=chips;}
    }
    internal static readonly Preset[] Presets=
    {
        new("Минимум","teleports","own"),
        new("Сокровища","pods","gordo","drones","ghosts","statues","own"),
        new("Ресурсы","valuables","materials","plorts","receivers","pots","own"),
        new("Всё"),
    };
    // «Всё» lists no chips: it means every chip of every group.
    internal static bool InPreset(Preset preset,string chip)=>preset!=null&&(preset.Chips.Length==0||Array.IndexOf(preset.Chips,chip)>=0);
    internal static void ApplyPreset(Preset preset,ICollection<string> layers,ICollection<string> hidden)
    {
        if(preset==null)return;
        layers.Clear();hidden.Clear();
        foreach(var group in Groups)
            foreach(var chip in group.Chips)
                if(InPreset(preset,chip.Id)&&!Active(chip,layers,hidden))Toggle(chip,layers,hidden);
    }
    // The preset the current layers equal exactly, else -1.
    internal static int MatchingPreset(ICollection<string> layers,ICollection<string> hidden)
    {
        for(int p=0;p<Presets.Length;p++)
        {
            bool same=true;
            foreach(var group in Groups)
            {
                foreach(var chip in group.Chips)
                    if(Active(chip,layers,hidden)!=InPreset(Presets[p],chip.Id)){same=false;break;}
                if(!same)break;
            }
            if(same)return p;
        }
        return -1;
    }

    // Treasure pod rows are named by their contents: «Странный алмаз и ещё 2».
    internal static string PodTitle(IReadOnlyList<string> titles,string fallback)
    {
        if(titles==null||titles.Count==0||string.IsNullOrWhiteSpace(titles[0]))return fallback??"";
        return titles.Count==1?titles[0]:titles[0]+" и ещё "+(titles.Count-1);
    }
    // «ресурсы» when the pod holds only items, else the first blueprint/component kind.
    internal static string PodKind(IReadOnlyList<string> kinds)
    {
        if(kinds==null||kinds.Count==0)return "";
        foreach(string kind in kinds)
            if(!string.IsNullOrEmpty(kind)&&kind!="Предмет")return kind=="Чертёж"?"чертёж":kind=="Компонент улучшения"?"компонент улучшения":kind.ToLowerInvariant();
        return "ресурсы";
    }
    // «1 предмет», «3 предмета», «5 предметов».
    internal static string Items(int count)
    {
        int n=Math.Abs(count)%100,last=n%10;
        string word=n>=11&&n<=14?"предметов":last==1?"предмет":last>=2&&last<=4?"предмета":"предметов";
        return count+" "+word;
    }

    // Horizontal carousels (groups, chips): wheel and arrows move inside [0, content-viewport].
    internal static float CarouselClamp(float offset,float content,float viewport)
    {
        float limit=Math.Max(0f,content-viewport);
        if(float.IsNaN(offset))return 0f;
        return Math.Min(limit,Math.Max(0f,offset));
    }
    internal static float CarouselStep(float offset,int direction,float content,float viewport)=>
        CarouselClamp(offset+Math.Sign(direction)*Math.Max(80f,viewport*.6f),content,viewport);
    // Brings [x, x+width] fully into view with the smallest move.
    internal static float CarouselReveal(float offset,float x,float width,float content,float viewport)
    {
        if(x<offset)offset=x;
        else if(x+width>offset+viewport)offset=x+width-viewport;
        return CarouselClamp(offset,content,viewport);
    }
}
