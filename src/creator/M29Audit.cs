using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.Prestige;
using Il2CppMonomiPark.SlimeRancher.Ranch;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.Shop;
using Il2CppMonomiPark.SlimeRancher.Shop.Runtime;
using UnityEngine;

namespace CreatorToolkit;

// M29 read-only audits for the ct-test relay (tools\ct-test\TESTING.md). They only read
// loaded native data and the mod's own state and return a text report; nothing is bought,
// granted, toggled or saved.

// Generic il2cpp object printer (field reflection): scalars, strings, enums, arrays, lists,
// localized strings, Addressables references (GUID, optionally named) and Unity objects by
// name. Event queries (CompositeQueryComponent trees, Query assets) are expanded.
internal static class M29Il2CppDump
{
    private const Il2CppSystem.Reflection.BindingFlags Declared=Il2CppSystem.Reflection.BindingFlags.Instance|
        Il2CppSystem.Reflection.BindingFlags.Public|Il2CppSystem.Reflection.BindingFlags.NonPublic|
        Il2CppSystem.Reflection.BindingFlags.DeclaredOnly;
    private const int MaxItems=40;
    private static readonly HashSet<string> Scalars=new(StringComparer.Ordinal){"System.Boolean","System.Byte","System.SByte","System.Int16",
        "System.UInt16","System.Int32","System.UInt32","System.Int64","System.UInt64","System.Single","System.Double","System.Char","System.Decimal"};
    private static readonly HashSet<string> Roots=new(StringComparer.Ordinal){"System.Object","System.ValueType","UnityEngine.Object",
        "UnityEngine.ScriptableObject","UnityEngine.Component","UnityEngine.Behaviour","UnityEngine.MonoBehaviour"};
    private static readonly Dictionary<string,Il2CppSystem.Reflection.FieldInfo[]> Cache=new(StringComparer.Ordinal);
    private static Il2CppSystem.Type _unity,_delegate;
    // Optional: asset GUID (normalised) -> known asset name, appended to references.
    internal static Func<string,string> GuidName;

    internal static string Describe(Il2CppSystem.Object value,int depth=8,int budget=6000)
    {
        var sb=new StringBuilder(256);
        try{Write(sb,value,depth,new HashSet<IntPtr>(),1,true,ref budget);}
        catch(Exception e){sb.Append(" !").Append(e.GetType().Name).Append(": ").Append(e.Message);}
        return sb.ToString();
    }
    internal static string Norm(string guid)
    {
        if(string.IsNullOrEmpty(guid))return "";
        var sb=new StringBuilder(32);
        foreach(char c in guid)if(Uri.IsHexDigit(c))sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
    internal static Il2CppSystem.Object FieldValue(Il2CppSystem.Object value,string name)
    {
        if(value==null)return null;
        var field=Find(value.GetIl2CppType(),name);
        return field?.GetValue(value);
    }
    internal static string Text(Il2CppSystem.Object value)
    {
        if(value==null||value.Pointer==IntPtr.Zero)return "";
        return value.GetIl2CppType()?.FullName=="System.String"?IL2CPP.Il2CppStringToManaged(value.Pointer)??"":value.ToString();
    }
    private static Il2CppSystem.Reflection.FieldInfo Find(Il2CppSystem.Type type,string name)
    {
        foreach(var field in Fields(type))if(field!=null&&field.Name==name)return field;
        return null;
    }
    private static Il2CppSystem.Reflection.FieldInfo[] Fields(Il2CppSystem.Type type)
    {
        string key=type?.AssemblyQualifiedName??type?.FullName??"?";
        if(Cache.TryGetValue(key,out var cached))return cached;
        var chain=new List<Il2CppSystem.Type>();
        for(var t=type;t!=null&&chain.Count<16;t=t.BaseType){if(Roots.Contains(t.FullName??""))break;chain.Add(t);}
        var list=new List<Il2CppSystem.Reflection.FieldInfo>();
        for(int i=chain.Count-1;i>=0;i--)
        {
            var declared=chain[i].GetFields(Declared);
            for(int k=0;declared!=null&&k<declared.Length;k++)if(declared[k]!=null)list.Add(declared[k]);
        }
        return Cache[key]=list.ToArray();
    }
    private static bool IsEnum(Il2CppSystem.Type type){try{return type.IsEnum;}catch{return false;}}
    private static bool QueryLike(Il2CppSystem.Type type)=>(type.Namespace??"").Contains("Event.Query",StringComparison.Ordinal);
    private static string Clean(string name)
    {
        if(name==null)return "?";
        int end=name.IndexOf(">k__BackingField",StringComparison.Ordinal);
        return name.StartsWith("<",StringComparison.Ordinal)&&end>1?name.Substring(1,end-1):name;
    }
    private static void Pad(StringBuilder sb,int indent){sb.Append('\n');sb.Append(' ',Math.Min(indent,24)*2);}
    private static void Write(StringBuilder sb,Il2CppSystem.Object value,int depth,HashSet<IntPtr> seen,int indent,bool expand,ref int budget)
    {
        if(budget<=0){sb.Append('…');return;}
        if(value==null||value.Pointer==IntPtr.Zero){sb.Append("null");return;}
        var type=value.GetIl2CppType();
        string full=type?.FullName??"?";
        if(full=="System.String"){string s=IL2CPP.Il2CppStringToManaged(value.Pointer)??"";sb.Append('"').Append(s).Append('"');budget-=s.Length+2;return;}
        // Boxed scalars are read from the unboxed data: ToString() through the interop object
        // runs the value type's method on the box header and prints garbage.
        if(Scalars.Contains(full)){string s=Scalar(value,full);sb.Append(s);budget-=s.Length+1;return;}
        if(IsEnum(type)){string s=value.ToString();sb.Append(s);budget-=s.Length+1;return;}
        _unity??=Il2CppType.Of<UnityEngine.Object>();
        _delegate??=Il2CppType.Of<Il2CppSystem.Delegate>();
        if(_delegate.IsAssignableFrom(type)){sb.Append('<').Append(type.Name).Append('>');return;}
        if(full=="System.RuntimeType"||full=="System.MonoType"){sb.Append("typeof(").Append(value.TryCast<Il2CppSystem.Type>()?.FullName).Append(')');return;}
        if(full.StartsWith("UnityEngine.Localization.LocalizedString",StringComparison.Ordinal))
        {
            string text;try{text=value.TryCast<UnityEngine.Localization.LocalizedString>()?.GetLocalizedString();}catch(Exception e){text="!"+e.GetType().Name;}
            sb.Append("loc\"").Append(text).Append('"');budget-=(text?.Length??0)+6;return;
        }
        if((type.Name??"").StartsWith("AssetReference",StringComparison.Ordinal))
        {
            string guid=Norm(Text(FieldValue(value,"m_AssetGUID")));
            sb.Append("ref:").Append(guid.Length>0?guid:"-");
            string known=guid.Length>0?GuidName?.Invoke(guid):null;
            if(!string.IsNullOrEmpty(known))sb.Append('=').Append(known);
            budget-=40;return;
        }
        bool unity=_unity.IsAssignableFrom(type);
        if(unity)
        {
            var obj=value.TryCast<UnityEngine.Object>();
            string name=obj!=null?obj.name:"?";
            sb.Append(type.Name).Append(":\"").Append(name).Append('"');budget-=(type.Name?.Length??0)+name.Length+4;
            if(!expand&&!QueryLike(type))return;
        }
        if(!seen.Add(value.Pointer)){sb.Append(" ^").Append(type.Name);return;}
        if(depth<=0){sb.Append(unity?" ":type.Name).Append("{…}");return;}
        if(type.IsArray)
        {
            var items=new List<Il2CppSystem.Object>();int length=0;
            try
            {
                length=(int)IL2CPP.il2cpp_array_length(value.Pointer);
                var element=type.GetElementType();
                if(element!=null&&!element.IsValueType)
                {var array=new Il2CppReferenceArray<Il2CppSystem.Object>(value.Pointer);for(int i=0;i<length&&i<MaxItems;i++)items.Add(array[i]);}
                else{var array=value.Cast<Il2CppSystem.Array>();for(int i=0;i<length&&i<MaxItems;i++)items.Add(array.GetValue(i));}
            }
            catch(Exception e){sb.Append('[').Append(length).Append(" !").Append(e.GetType().Name).Append(']');return;}
            Items(sb,items,length,depth,seen,indent,ref budget);return;
        }
        if(type.Name=="List`1"&&(type.Namespace??"").StartsWith("System.Collections",StringComparison.Ordinal))
        {
            var items=new List<Il2CppSystem.Object>();int size=0;
            try
            {
                size=Math.Max(0,Int(FieldValue(value,"_size")));
                var raw=FieldValue(value,"_items");
                var element=raw?.GetIl2CppType()?.GetElementType();
                if(raw!=null&&element!=null&&!element.IsValueType)
                {var array=new Il2CppReferenceArray<Il2CppSystem.Object>(raw.Pointer);for(int i=0;i<size&&i<MaxItems;i++)items.Add(array[i]);}
                else if(raw!=null){var array=raw.Cast<Il2CppSystem.Array>();for(int i=0;i<size&&i<MaxItems;i++)items.Add(array.GetValue(i));}
            }
            catch(Exception e){sb.Append("List[").Append(size).Append(" !").Append(e.GetType().Name).Append(']');return;}
            Items(sb,items,size,depth,seen,indent,ref budget);return;
        }
        if((type.Name??"").StartsWith("Dictionary`",StringComparison.Ordinal)||(type.Name??"").StartsWith("HashSet`",StringComparison.Ordinal))
        {sb.Append(type.Name).Append("{…}");return;}
        sb.Append(unity?" {":type.Name+"{");
        bool first=true;
        foreach(var field in Fields(type))
        {
            if(budget<=0){sb.Append(" …");break;}
            Il2CppSystem.Object child;
            try{child=field.GetValue(value);}
            catch(Exception e){sb.Append(first?"":" ").Append(Clean(field.Name)).Append("=!").Append(e.GetType().Name);first=false;continue;}
            sb.Append(first?"":" ").Append(Clean(field.Name)).Append('=');
            Write(sb,child,depth-1,seen,indent+1,false,ref budget);
            first=false;
        }
        sb.Append('}');
    }
    internal static int Int(Il2CppSystem.Object boxed)
    {
        if(boxed==null||boxed.Pointer==IntPtr.Zero)return 0;
        IntPtr data=IL2CPP.il2cpp_object_unbox(boxed.Pointer);
        return data==IntPtr.Zero?0:Marshal.ReadInt32(data);
    }
    private static string Scalar(Il2CppSystem.Object value,string full)
    {
        IntPtr p=IL2CPP.il2cpp_object_unbox(value.Pointer);
        if(p==IntPtr.Zero)return "?";
        var c=CultureInfo.InvariantCulture;
        switch(full)
        {
            case "System.Boolean": return Marshal.ReadByte(p)!=0?"True":"False";
            case "System.Byte": return Marshal.ReadByte(p).ToString(c);
            case "System.SByte": return ((sbyte)Marshal.ReadByte(p)).ToString(c);
            case "System.Int16": return Marshal.ReadInt16(p).ToString(c);
            case "System.UInt16": return ((ushort)Marshal.ReadInt16(p)).ToString(c);
            case "System.Char": return "'"+(char)Marshal.ReadInt16(p)+"'";
            case "System.Int32": return Marshal.ReadInt32(p).ToString(c);
            case "System.UInt32": return ((uint)Marshal.ReadInt32(p)).ToString(c);
            case "System.Int64": return Marshal.ReadInt64(p).ToString(c);
            case "System.UInt64": return ((ulong)Marshal.ReadInt64(p)).ToString(c);
            case "System.Single": return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p)).ToString("0.####",c);
            case "System.Double": return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p)).ToString("0.####",c);
        }
        return value.ToString();
    }
    private static void Items(StringBuilder sb,List<Il2CppSystem.Object> items,int total,int depth,HashSet<IntPtr> seen,int indent,ref int budget)
    {
        sb.Append('[');
        if(total==0){sb.Append(']');return;}
        foreach(var item in items){Pad(sb,indent+1);Write(sb,item,depth-1,seen,indent+1,false,ref budget);if(budget<=0)break;}
        if(total>items.Count){Pad(sb,indent+1);sb.Append("… +").Append(total-items.Count);}
        Pad(sb,indent);sb.Append(']');
    }
}

// Task 3: where the native data grants each vacpack style (VacPalette): shop tables
// (fixed and random entries with their availability query), the rule set / category /
// real-date schedule above them, and the end-game rewards kiosk tracks.
internal static class M29PaletteAudit
{
    internal static readonly string[] Hidden={"VacPalette_HotRod","VacPalette_Gold","VacPalette_DreamPop"};
    private sealed class Style
    {
        internal string Name,Display,Guid,RefId,Owned,Description;
        internal readonly List<string> Sources=new();
    }
    internal static string Report(ToolkitMod app)
    {
        var sb=new StringBuilder(32768);
        var styles=new List<Style>();
        var byGuid=new Dictionary<string,Style>(StringComparer.Ordinal);
        var byRef=new Dictionary<string,Style>(StringComparer.Ordinal);
        var byPointer=new Dictionary<IntPtr,Style>();
        var director=app.Context?.PaletteDirector;
        var list=director?._palettes;
        var candidates=new List<VacPalette>();
        for(int i=0;list!=null&&i<list.Count&&i<512;i++){var vac=list[i]?.TryCast<VacPalette>();if(vac!=null&&!vac.WasCollected)candidates.Add(vac);}
        int extra=0;
        foreach(var vac in Resources.FindObjectsOfTypeAll<VacPalette>())
            if(vac!=null&&!vac.WasCollected&&candidates.All(c=>c.Pointer!=vac.Pointer)){candidates.Add(vac);extra++;}
        foreach(var vac in candidates)
        {
            var style=new Style{Name=vac.name,Display=Safe(()=>vac.DisplayName?.GetLocalizedString()),Guid=M29Il2CppDump.Norm(Safe(()=>vac.Guid)),
                RefId=Safe(()=>vac.ReferenceId),Owned=director!=null?Safe(()=>director.IsOwned(vac).ToString()):"?",
                Description=Safe(()=>vac.Description?.GetLocalizedString())};
            styles.Add(style);byPointer[vac.Pointer]=style;
            if(style.Guid.Length>0)byGuid[style.Guid]=style;
            if(!string.IsNullOrEmpty(style.RefId)){byRef[style.RefId]=style;
                string tail=style.RefId.StartsWith("Palette",StringComparison.Ordinal)?M29Il2CppDump.Norm(style.RefId.Substring(7)):"";
                if(tail.Length>0&&!byGuid.ContainsKey(tail))byGuid[tail]=style;}
        }
        // Names of the shop assets for the reference printer.
        var names=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var style in styles)if(style.Guid.Length>0)names[style.Guid]=style.Name;
        var ruleSets=new Dictionary<string,ShopCategorySourceRuleSet>(StringComparer.Ordinal);
        foreach(var set in Resources.FindObjectsOfTypeAll<ShopCategorySourceRuleSet>())
            if(set!=null&&!set.WasCollected){string g=M29Il2CppDump.Norm(Safe(()=>set._assetGuid));if(g.Length>0){ruleSets[g]=set;names[g]="ruleSet "+set.name;}}
        var categories=new Dictionary<string,ShopItemCategoryDescription>(StringComparer.Ordinal);
        foreach(var cat in Resources.FindObjectsOfTypeAll<ShopItemCategoryDescription>())
            if(cat!=null&&!cat.WasCollected){string g=M29Il2CppDump.Norm(Safe(()=>cat._assetGuid));if(g.Length>0){categories[g]=cat;names[g]="category "+cat.name;}}
        M29Il2CppDump.GuidName=g=>names.TryGetValue(g,out var n)?n:null;
        try
        {
            sb.Append("styles=").Append(styles.Count).Append(" (catalog ").Append(list?.Count??0).Append(", loaded outside it ").Append(extra)
              .Append(") ruleSets=").Append(ruleSets.Count).Append(" categories=").Append(categories.Count).Append('\n');
            foreach(var style in styles)
                sb.Append("STYLE | ").Append(style.Name).Append(" | «").Append(style.Display).Append("» | guid=").Append(style.Guid)
                  .Append(" | ref=").Append(style.RefId).Append(" | owned=").Append(style.Owned).Append(" | text=").Append(OneLine(style.Description)).Append('\n');
            Style Match(string guid,string refId)
            {
                string g=M29Il2CppDump.Norm(guid);
                if(g.Length>0&&byGuid.TryGetValue(g,out var s))return s;
                return !string.IsNullOrEmpty(refId)&&byRef.TryGetValue(refId,out s)?s:null;
            }
            // Shop: fixed tables.
            var hitTables=new List<ShopDataSourceBase>();
            int fixedTables=0,fixedEntries=0,randomTables=0,randomEntries=0;
            sb.Append("\n== shop fixed tables\n");
            foreach(var table in Resources.FindObjectsOfTypeAll<ShopFixedItemsTable>())
            {
                if(table==null||table.WasCollected)continue;fixedTables++;
                var items=table._itemListings;int n=items?.Length??0;
                for(int i=0;i<n;i++)
                {
                    var entry=items[i];if(entry==null)continue;fixedEntries++;
                    ShopItemCost cost=null;try{cost=entry._itemCost;}catch{}
                    var item=cost?.ShopItem;
                    var style=item==null?null:Match(Safe(()=>item.AssetGuid),Safe(()=>item._identifiableReferenceId));
                    if(style==null)continue;
                    if(!hitTables.Any(t=>t.Pointer==table.Pointer))hitTables.Add(table);
                    string where="магазин · таблица "+table.name+" #"+i+" (фикс.)";
                    style.Sources.Add(where);
                    sb.Append("SHOP-FIXED | ").Append(table.name).Append(" #").Append(i).Append('/').Append(n).Append(" | ").Append(style.Name)
                      .Append(" | unlock=").Append(Safe(()=>cost.UnlockType.ToString())).Append(" | cost=").Append(Cost(Safe(()=>cost.PurchaseCost)))
                      .Append(" | limit=").Append(M29Il2CppDump.Describe(M29Il2CppDump.FieldValue(entry,"_acquireLimit"),3,300)).Append('\n');
                    sb.Append("    available=").Append(M29Il2CppDump.Describe(entry.AvailableCondition)).Append('\n');
                }
            }
            sb.Append("\n== shop random tables\n");
            foreach(var table in Resources.FindObjectsOfTypeAll<ShopRandomItemsTable>())
            {
                if(table==null||table.WasCollected)continue;randomTables++;
                var items=table._items;int n=items?.Length??0;
                for(int i=0;i<n;i++)
                {
                    var entry=items[i];if(entry==null)continue;randomEntries++;
                    var item=entry._item;
                    var style=item==null?null:Match(Safe(()=>item.AssetGuid),Safe(()=>item._identifiableReferenceId));
                    if(style==null)continue;
                    if(!hitTables.Any(t=>t.Pointer==table.Pointer))hitTables.Add(table);
                    style.Sources.Add("магазин · таблица "+table.name+" #"+i+" (случайн.)");
                    sb.Append("SHOP-RANDOM | ").Append(table.name).Append(" #").Append(i).Append('/').Append(n).Append(" | ").Append(style.Name)
                      .Append(" | unlock=").Append(Safe(()=>entry._acquireType.ToString())).Append(" | weight=").Append(Safe(()=>entry._occurenceWeight.ToString("0.###",CultureInfo.InvariantCulture))).Append('\n');
                    var costs=entry._costs;
                    for(int k=0;costs!=null&&k<costs.Length;k++)
                    {
                        var rate=costs[k];if(rate==null)continue;
                        sb.Append("    cost#").Append(k).Append(" weight=").Append(Safe(()=>rate._occurenceWeight.ToString("0.###",CultureInfo.InvariantCulture)))
                          .Append(" limit=").Append(M29Il2CppDump.Describe(M29Il2CppDump.FieldValue(rate,"_acquireLimit"),3,300))
                          .Append(" price=").Append(Cost(Safe(()=>rate._purchaseCost))).Append('\n');
                    }
                    sb.Append("    available=").Append(M29Il2CppDump.Describe(entry.AvailableCondition)).Append('\n');
                }
            }
            sb.Append("tables fixed=").Append(fixedTables).Append(" (").Append(fixedEntries).Append(" entries) random=").Append(randomTables)
              .Append(" (").Append(randomEntries).Append(" entries) with styles=").Append(hitTables.Count).Append('\n');
            // Which shop owns each category (runtime of the ShopDirector).
            var shopOf=new Dictionary<string,string>(StringComparer.Ordinal);
            sb.Append("\n== shops\n");
            var runtimes=app.Context?.ShopDirector?._runtimes;
            for(int i=0;runtimes!=null&&i<runtimes.Count;i++)
            {
                var runtime=runtimes[i];if(runtime==null)continue;
                var definition=Safe(()=>runtime._shopDefinition?.TryCast<ShopDefinition>());
                string tag=definition!=null?Safe(()=>definition._shopAssetTag):"?";
                sb.Append("SHOP | ").Append(definition?.name??"?").Append(" | tag=").Append(tag).Append(" | available=")
                  .Append(Safe(()=>runtime._isAvailable.ToString())).Append(" | query=").Append(definition!=null?M29Il2CppDump.Describe(definition._shopAvailableQuery):"-").Append('\n');
                var provider=Safe(()=>runtime._dataProvider?.TryCast<ShopRuntimeAddressablesDataProvider>());
                var cats=provider?._categories;
                for(int k=0;cats!=null&&k<cats.Count;k++)
                {
                    var category=cats[k];var description=Safe(()=>category?.DescriptionAsset?.TryCast<ShopItemCategoryDescription>());
                    if(description==null)continue;
                    shopOf[M29Il2CppDump.Norm(Safe(()=>description._assetGuid))]=(definition?.name??"?")+" ["+tag+"]";
                }
            }
            // Rule sets, categories and schedules above the tables that hold styles.
            var links=Resources.FindObjectsOfTypeAll<ShopCategorySourceLink>();
            var schedules=Resources.FindObjectsOfTypeAll<ShopScheduleLink>();
            sb.Append("\n== chains of the tables with styles\n");
            foreach(var table in hitTables)
            {
                sb.Append("TABLE ").Append(table.name).Append('\n');
                foreach(var link in links)
                {
                    if(link==null||link.WasCollected||Safe(()=>link._linkedSource)?.Pointer!=table.Pointer)continue;
                    string setGuid=M29Il2CppDump.Norm(Safe(()=>link._owningRuleSet?.AssetGUID));
                    ruleSets.TryGetValue(setGuid,out var set);
                    sb.Append("  link ").Append(link.name).Append(" sort=").Append(Safe(()=>link._sourceSortIndex.ToString())).Append(" -> ruleSet ")
                      .Append(set!=null?set.name:"? ("+setGuid+")").Append('\n');
                    if(set==null)continue;
                    sb.Append("    restock=").Append(Safe(()=>set._randomItemRestockFrequency.ToString())).Append(" countLimit=")
                      .Append(M29Il2CppDump.Describe(M29Il2CppDump.FieldValue(set,"_countLimit"),3,300)).Append(" handling=").Append(Safe(()=>set._countLimitHandling.ToString())).Append('\n');
                    sb.Append("    allItemsAvailable=").Append(M29Il2CppDump.Describe(set._allItemsAvailableCondition)).Append('\n');
                    string catGuid=M29Il2CppDump.Norm(Safe(()=>set._categoryLink?.AssetGUID));
                    categories.TryGetValue(catGuid,out var cat);
                    sb.Append("    category ").Append(cat!=null?cat.name:"? ("+catGuid+")");
                    if(cat!=null)sb.Append(" «").Append(Safe(()=>cat._displayName?.GetLocalizedString())).Append("» analytics=").Append(Safe(()=>cat._analyticsName))
                        .Append(" ui=").Append(Safe(()=>cat._uiDisplayType.ToString())).Append(" shop=").Append(shopOf.TryGetValue(catGuid,out var shop)?shop:"?")
                        .Append("\n    categoryCondition=").Append(M29Il2CppDump.Describe(cat._condition));
                    sb.Append('\n');
                    foreach(var schedule in schedules)
                    {
                        if(schedule==null||schedule.WasCollected)continue;
                        string linked=M29Il2CppDump.Norm(Safe(()=>schedule._linkedAsset?.AssetGUID));
                        if(linked.Length==0||linked!=setGuid&&linked!=catGuid)continue;
                        sb.Append("    schedule ").Append(schedule.name).Append(" -> ").Append(names.TryGetValue(linked,out var n)?n:linked).Append(": ")
                          .Append(M29Il2CppDump.Describe(schedule._schedule)).Append('\n');
                    }
                }
            }
            sb.Append("\n== all schedule links\n");
            foreach(var schedule in schedules)
            {
                if(schedule==null||schedule.WasCollected)continue;
                string linked=M29Il2CppDump.Norm(Safe(()=>schedule._linkedAsset?.AssetGUID));
                sb.Append("SCHEDULE | ").Append(schedule.name).Append(" -> ").Append(names.TryGetValue(linked,out var n)?n:linked).Append(" | ")
                  .Append(M29Il2CppDump.Describe(schedule._schedule,4,600)).Append('\n');
            }
            // End-game rewards kiosk.
            sb.Append("\n== end-game rewards\n");
            foreach(var definition in Resources.FindObjectsOfTypeAll<EndGameRewardsDefinition>())
            {
                if(definition==null||definition.WasCollected)continue;
                var entries=definition._costsAndRewards;int n=entries?.Length??0;
                sb.Append("TRACK | ").Append(definition.name).Append(" | entries=").Append(n).Append(" | repeatAtEnd=").Append(Safe(()=>definition._repeatTrackAtEnd.ToString())).Append('\n');
                var total=new Dictionary<string,int>(StringComparer.Ordinal);
                for(int i=0;i<n;i++)
                {
                    var entry=entries[i];if(entry==null)continue;
                    string costs=IdentCosts(entry,total);
                    var rewards=entry.Rewards;var line=new StringBuilder();Style hit=null;
                    for(int k=0;rewards!=null&&k<rewards.Length;k++)
                    {
                        var reward=rewards[k];if(reward==null)continue;
                        string guid=Safe(()=>reward.Item?.AssetGuid);
                        var style=Match(guid,null);if(style!=null)hit=style;
                        line.Append(k==0?"":", ").Append(style!=null?style.Name:(Safe(()=>reward.Item?.UnderlyingLoadedAsset?.name) is string loaded&&loaded.Length>0?loaded:"ref:"+M29Il2CppDump.Norm(guid)))
                            .Append(" x").Append(Safe(()=>reward.Count.ToString())).Append(" (").Append(Safe(()=>reward.GadgetType.ToString())).Append(')');
                    }
                    sb.Append(hit!=null?"  KIOSK* #":"  #").Append(i+1).Append('/').Append(n).Append(" id=").Append(Safe(()=>entry.UniqueId))
                      .Append(" | cost=").Append(costs).Append(" | total=").Append(Totals(total)).Append(" | rewards=").Append(line).Append('\n');
                    if(hit!=null)hit.Sources.Add("киоск наград · "+definition.name+" #"+(i+1)+"/"+n+" · взнос "+costs+" · всего с начала трека "+Totals(total));
                }
            }
            foreach(var kiosk in Resources.FindObjectsOfTypeAll<RewardsKiosk>())
            {
                if(kiosk==null||kiosk.WasCollected)continue;
                sb.Append("KIOSK | ").Append(kiosk.name).Append(" | scene=").Append(Safe(()=>kiosk.gameObject.scene.name)).Append(" | unique=")
                  .Append(Safe(()=>kiosk._uniqueRewards?.name)).Append(" | repeat=").Append(Safe(()=>kiosk._repeatRewards?.name)).Append(" | lastActive=")
                  .Append(Safe(()=>kiosk._lastActiveState.ToString())).Append('\n');
                sb.Append("    accepted=").Append(M29Il2CppDump.Describe(M29Il2CppDump.FieldValue(kiosk,"_acceptedTypes"),3,1500)).Append('\n');
                sb.Append("    active=").Append(M29Il2CppDump.Describe(kiosk._activeStateCompositeQuery)).Append('\n');
            }
            sb.Append("\n== currencies\n");
            foreach(var currency in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Economy.CurrencyDefinition>())
            {
                if(currency==null||currency.WasCollected)continue;
                sb.Append("CURRENCY | ").Append(currency.name).Append(" | «").Append(Safe(()=>currency._displayName?.GetLocalizedString())).Append("» | id=")
                  .Append(Safe(()=>currency._identifier)).Append(" | persistence=").Append(Safe(()=>currency._persistenceId.ToString(CultureInfo.InvariantCulture)))
                  .Append(" | available=").Append(Safe(()=>currency.IsAvailable.ToString())).Append('\n');
                sb.Append("    condition=").Append(M29Il2CppDump.Describe(currency._availableCondition)).Append('\n');
            }
            sb.Append("\n== summary\n");
            foreach(var style in styles.OrderBy(s=>Array.IndexOf(Hidden,s.Name)<0?1:0).ThenBy(s=>s.Name,StringComparer.Ordinal))
                sb.Append(Array.IndexOf(Hidden,style.Name)>=0?"HIDDEN ":"STYLE  ").Append(style.Name).Append(" «").Append(style.Display).Append("» owned=").Append(style.Owned).Append(" | ")
                  .Append(style.Sources.Count>0?string.Join(" ; ",style.Sources):"нет в загруженных таблицах магазина и треках киоска").Append('\n');
        }
        finally{M29Il2CppDump.GuidName=null;}
        return sb.ToString();
    }
    private static string Cost(Il2CppMonomiPark.SlimeRancher.Economy.PurchaseCost cost)
    {
        if(cost==null)return "-";
        var parts=new List<string>();
        int listed=0;
        try
        {
            var currencies=cost._currencyCosts;
            for(int i=0;currencies!=null&&i<currencies.Count;i++)
            {
                var c=currencies[i];if(c==null)continue;listed++;
                var currency=c._currency;
                string title=currency!=null?Safe(()=>currency._displayName?.GetLocalizedString()):null;
                parts.Add(c._amount+" "+(currency!=null?currency.name:"?")+(string.IsNullOrEmpty(title)?"":" «"+title+"»"));
            }
        }
        catch(Exception e){parts.Add("currency !"+e.GetType().Name);}
        // NewbuckCost mirrors the newbucks entry of the list; shown only when the list is empty.
        int newbucks=0;try{newbucks=cost.NewbuckCost;}catch{}
        if(newbucks>0&&listed==0)parts.Add(newbucks+" ньюбаксов");
        try{var idents=cost.IdentCosts;for(int i=0;idents!=null&&i<idents.Count;i++){var c=idents[i];if(c!=null)parts.Add(c.Amount+" "+(c.IdentType!=null?c.IdentType.name:"?"));}}
        catch(Exception e){parts.Add("ident !"+e.GetType().Name);}
        try{var components=cost.ComponentCosts;for(int i=0;components!=null&&i<components.Count;i++){var c=components[i];if(c!=null)parts.Add("компонент "+c.name);}}
        catch(Exception e){parts.Add("component !"+e.GetType().Name);}
        return parts.Count==0?"бесплатно":string.Join(" + ",parts);
    }
    private static string IdentCosts(EndGameCostRewardEntry entry,Dictionary<string,int> total)
    {
        var parts=new List<string>();
        try
        {
            var costs=entry.Costs;
            for(int i=0;costs!=null&&i<costs.Length;i++)
            {
                var c=costs[i];if(c==null)continue;
                string name=c.IdentType!=null?c.IdentType.name:"?";int amount=c.Amount;
                parts.Add(amount+" "+name);
                total[name]=(total.TryGetValue(name,out int t)?t:0)+amount;
            }
        }
        catch(Exception e){return "!"+e.GetType().Name+": "+M29Il2CppDump.Describe(M29Il2CppDump.FieldValue(entry,"Costs"),4,800);}
        return parts.Count==0?"-":string.Join(" + ",parts);
    }
    private static string Totals(Dictionary<string,int> total)=>total.Count==0?"-":string.Join(" + ",total.Select(p=>p.Value+" "+p.Key));
    private static string OneLine(string text)=>string.IsNullOrEmpty(text)?"":text.Replace('\n',' ').Replace('\r',' ');
    private static T Safe<T>(Func<T> read){try{return read();}catch{return default;}}
}

// Task 5: per atlas layer, why its points are (not) in the navigator list and on the compass.
internal static class M29LayerAudit
{
    internal static string Report(ToolkitMod app)
    {
        var map=app.Map;var compass=app.Compass;
        var sb=new StringBuilder(8192);
        var group=SystemContext.Instance?.SceneLoader?.CurrentSceneGroup;
        var motor=app.Player?.Controller?.Motor;
        Vector3 origin=motor!=null?motor.TransientPosition:map.PlayerPosition;
        // M29 task 4: the compass range from navigation.cfg.
        float range=app.Navigation?.Settings.CompassRange??M27CompassPolicy.Range,range2=range*range;
        var listed=new HashSet<MapTools.Point>(map.Filter());
        var anchors=compass?.AnchorKeys.ToList()??new List<string>();
        sb.Append("group=").Append(group!=null?group.name:"-").Append(" player=").Append(origin.ToString("F0")).Append(" visible=").Append(string.Join(",",map.Visible))
          .Append(" hideOpened=").Append(map.HideOpened).Append(" actualOnly=").Append(map.SourcesActualOnly).Append(" search=\"").Append(map.Search).Append('"')
          .Append(" list=").Append(listed.Count).Append(" points=").Append(map.Points.Count).Append('\n');
        sb.Append("compass enabled=").Append(compass?.Enabled).Append(" range=").Append(range.ToString("0",CultureInfo.InvariantCulture)).Append(" anchors=").Append(anchors.Count)
          .Append(" layersInRange=").Append(compass?.LayersInRange).Append(" starved=").Append(compass?.StarvedLayers)
          .Append(" | ").Append(compass?.Status).Append('\n');
        sb.Append("minimap show=").Append(map.ShowMiniMap).Append(" points=").Append(map.MiniMapPoints.Count).Append(" layers=")
          .Append(map.MiniMapPoints.Select(p=>p.Category).Distinct(StringComparer.Ordinal).Count()).Append(" sourcesRequested=").Append(map.SourcesRequested)
          .Append(" | ").Append(app.Navigation?.Settings.Describe()).Append('\n');
        sb.Append("layer | on | total | ids | dup | noGroup | sameGroup | inRange | opened | eligible | listed | ifOn | onCompass | onMiniMap | groups\n");
        var layers=MapTools.Categories.Concat(new[]{CustomWaypointTools.Category}).Distinct().ToList();
        foreach(var layer in layers)
        {
            var points=map.Points.Where(p=>p!=null&&p.Category==layer).ToList();
            int total=points.Count,ids=points.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count();
            int distinct=points.Select(Spot).Distinct(StringComparer.Ordinal).Count();
            int noGroup=0,same=0,inRange=0,opened=0,eligible=0,listedCount=0,ifOn=0;
            bool on=map.Visible.Contains(layer);
            foreach(var p in points)
            {
                if(p.Group==null&&string.IsNullOrEmpty(p.GroupName))noGroup++;
                bool isOpened=Safe(()=>map.Opened(p));if(isOpened)opened++;
                bool sameGroup=group!=null&&CompassMarkerTools.SameGroup(p,group);
                float dx=p.Position.x-origin.x,dz=p.Position.z-origin.z;bool near=dx*dx+dz*dz<=range2;
                if(sameGroup){same++;if(near)inRange++;}
                if(sameGroup&&near&&!isOpened)eligible++;
                if(listed.Contains(p))listedCount++;
                if((!map.SourcesActualOnly||!p.SourceObservation||p.LiveSource)&&(!map.HideOpened||!isOpened))ifOn++;
            }
            string prefix="pt|"+layer+"|";
            // Own markers use M28CompassIconPolicy.CustomKey ("cw|").
            int onCompass=layer==CustomWaypointTools.Category?anchors.Count(k=>k.StartsWith(M28CompassIconPolicy.CustomKey(""),StringComparison.Ordinal)):anchors.Count(k=>k.StartsWith(prefix,StringComparison.Ordinal));
            int onMiniMap=map.MiniMapPoints.Count(p=>p.Category==layer);
            string groups=string.Join(",",points.GroupBy(p=>p.GroupName??"-").OrderByDescending(g=>g.Count()).Take(6).Select(g=>g.Key+":"+g.Count()));
            sb.Append(layer).Append(" | ").Append(on?1:0).Append(" | ").Append(total).Append(" | ").Append(ids).Append(" | ").Append(total-distinct)
              .Append(" | ").Append(noGroup).Append(" | ").Append(same).Append(" | ").Append(inRange).Append(" | ").Append(opened).Append(" | ").Append(eligible)
              .Append(" | ").Append(listedCount).Append(" | ").Append(ifOn).Append(" | ").Append(onCompass).Append(" | ").Append(onMiniMap).Append(" | ").Append(groups).Append('\n');
        }
        // Same physical point listed under several scene groups (test groups repeat the zone scenes).
        sb.Append("duplicates (same id and spot in several groups):\n");
        foreach(var dup in map.Points.Where(p=>p!=null).GroupBy(p=>p.Category+"|"+Spot(p),StringComparer.Ordinal).Where(g=>g.Count()>1)
            .GroupBy(g=>g.First().Category+" @ "+string.Join("+",g.Select(p=>p.GroupName??"-").OrderBy(n=>n,StringComparer.Ordinal)),StringComparer.Ordinal)
            .OrderByDescending(g=>g.Count()).Take(12))
            sb.Append("  ").Append(dup.Key).Append(" x").Append(dup.Count()).Append('\n');
        sb.Append("compass anchors:\n");
        foreach(var key in anchors)sb.Append("  ").Append(key).Append('\n');
        return sb.ToString();
    }
    private static string Spot(MapTools.Point p)=>p.Id+"@"+Mathf.RoundToInt(p.Position.x)+","+Mathf.RoundToInt(p.Position.z);
    private static T Safe<T>(Func<T> read){try{return read();}catch{return default;}}
}
