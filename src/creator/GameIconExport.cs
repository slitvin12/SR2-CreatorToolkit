using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppMonomiPark.SlimeRancher.Player.Component;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityLocale = UnityEngine.Localization.Locale;

namespace CreatorToolkit;

// Owner request 2026-10-31: «вытащить хорошие иконки из файлов игры … в папку и
// подписать». Diagnostic command `icons-export` (cmd.txt) writes every native
// icon sprite the game has loaded as PNG to UserData/CreatorToolkit/GameIcons/<folder>/
// named «<русское имя> [<English name>].png» plus icons.tsv / pods.tsv / index.html.
// Read-only: one GPU blit + readback per sprite, a few dozen per frame.
internal sealed class GameIconExport
{
    private sealed class Job
    {
        internal string Folder,Id,Ru,En,Source,Note="",RuPrefix,EnPrefix;
        internal LocalizedString Name;
        internal CapsuleDatabase.LocalizedKey Key;
        internal Sprite Sprite;
        internal Texture Texture;
        internal Rect Pixels;
    }
    private sealed class Row { internal string Folder,File,Id,Ru,En,Sprite,Size,Source,Note; }

    private const int PerFrame=20,MaxSide=512,SpriteCap=9000;
    private static readonly Regex Tags=new("<[^>]+>",RegexOptions.Compiled);
    private readonly ToolkitMod _app;
    private readonly Queue<Job> _queue=new();
    private readonly List<Row> _rows=new();
    private readonly HashSet<string> _files=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen=new(StringComparer.Ordinal);
    private readonly List<string> _log=new();
    private readonly Dictionary<string,(string Ru,string En)> _bySprite=new(StringComparer.Ordinal);
    private UnityLocale _ru,_en;
    private string _root,_currentCode;
    private int _failed,_skipped,_total;
    private float _started;
    internal string Status {get;private set;}="не запускался";

    internal GameIconExport(ToolkitMod app){_app=app;}

    internal void Start(bool all)
    {
        if(_queue.Count>0){_app.Notify("Экспорт иконок уже идёт: осталось "+_queue.Count);return;}
        _rows.Clear();_files.Clear();_seen.Clear();_log.Clear();_bySprite.Clear();_failed=_skipped=0;
        _root=Path.Combine(_app.DataDirectory,"GameIcons");
        try{if(Directory.Exists(_root))Directory.Delete(_root,true);}catch(Exception e){_log.Add("clean: "+e.Message);}
        Directory.CreateDirectory(_root);
        _ru=FindLocale("ru");_en=FindLocale("en");
        try{_currentCode=Il2Cpp.SystemContext.Instance?.LocalizationDirector?.GetCurrentLocaleCode();}catch{_currentCode=null;}
        _log.Add("locales ru="+(_ru!=null?_ru.Identifier.Code:"-")+" en="+(_en!=null?_en.Identifier.Code:"-")+" current="+(_currentCode??"-"));
        Section("identifiable types",CollectTypes);
        Section("radiant",CollectRadiant);
        Section("upgrade components",CollectUpgrades);
        Section("pod rewards",CollectRewards);
        Section("map markers",CollectMapMarkers);
        Section("pedia",CollectPedia);
        Section("fandom atlas",CollectFandom);
        Section("sprites",()=>CollectSprites(all));
        Section("pods",WritePods);
        _total=_queue.Count;_started=Time.unscaledTime;
        Status="экспорт: 0/"+_total;
        _app.Notify("Экспорт иконок игры: "+_total+" спрайтов в очереди → "+_root);
    }

    private void Section(string label,Action collect)
    {
        int before=_queue.Count;
        try{collect();}catch(Exception e){_log.Add(label+" FAILED: "+e.Message);_app.LoggerInstance.Warning("[icons] "+label+": "+e);}
        _log.Add(label+": +"+(_queue.Count-before));
    }

    internal void Tick()
    {
        if(_queue.Count==0)return;
        for(int n=0;n<PerFrame&&_queue.Count>0;n++)
        {
            var job=_queue.Dequeue();
            try{Export(job);}
            catch(Exception e){_failed++;if(_failed<=30)_log.Add("FAIL "+job.Folder+"/"+job.Id+": "+e.Message);}
        }
        Status="экспорт: "+(_total-_queue.Count)+"/"+_total;
        if(_queue.Count==0)Finish();
    }

    // ---------------- collection ----------------
    private void Add(Job job)
    {
        if(job.Sprite!=null&&!Live(job.Sprite))return;
        string identity=job.Sprite!=null?job.Sprite.Pointer.ToString():job.Texture.Pointer+"@"+job.Pixels;
        if(!_seen.Add(job.Folder+"|"+identity))return;
        _queue.Enqueue(job);
    }

    private void CollectTypes()
    {
        var catalog=new Dictionary<IntPtr,ActorTools.Entry>();
        foreach(var e in _app.Actors.Catalog)
            if(e?.Type!=null&&!e.IsRadiant&&!e.IsPrototype&&!e.Type.WasCollected&&!catalog.ContainsKey(e.Type.Pointer))catalog[e.Type.Pointer]=e;
        var seen=new HashSet<IntPtr>();
        foreach(var type in Resources.FindObjectsOfTypeAll<IdentifiableType>())
        {
            if(type==null||type.WasCollected||!seen.Add(type.Pointer))continue;
            Sprite icon=null,map=null;
            try{icon=type.Icon;}catch{}
            if(!Live(icon))try{icon=type.icon;}catch{}
            try{map=type.MapIcon;}catch{}
            string folder=catalog.TryGetValue(type.Pointer,out var entry)?entry.Category switch
            {
                "Слаймы"=>"01_slimes","Ларго"=>"02_largos","Гордо"=>"03_gordos","Плорты"=>"04_plorts",
                "Еда"=>"05_food","Ресурсы"=>"06_resources",_=>"07_other"
            }:Classify(type);
            string id=type.ReferenceId??type.name;
            LocalizedString name=null;try{name=type.LocalizedName;}catch{}
            string note=entry!=null?entry.Category+" / "+entry.Family:"не в каталоге мода";
            if(Live(icon))Add(new Job{Folder=folder,Id=id,Name=name,Sprite=icon,Source="IdentifiableType.Icon",Note=note});
            if(Live(map)&&(icon==null||map.Pointer!=icon.Pointer))
                Add(new Job{Folder="11_map_icons_of_types",Id=id,Name=name,Sprite=map,Source="IdentifiableType.MapIcon",Note=note});
        }
    }
    private static string Classify(IdentifiableType type)
    {
        try
        {
            if(type.TryCast<GadgetDefinition>()!=null)return "08_gadgets";
            if(type.IsPlort)return "04_plorts";
            var slime=type.TryCast<SlimeDefinition>();
            if(slime!=null)return slime.IsLargo?"02_largos":"01_slimes";
        }
        catch{}
        return "07_other";
    }
    private void CollectRadiant()
    {
        foreach(var e in _app.Actors.Catalog)
        {
            if(e==null||!e.IsRadiant||!Live(e.RadiantIcon))continue;
            LocalizedString name=null;try{name=e.Type?.LocalizedName;}catch{}
            Add(new Job{Folder="01_slimes",Id=e.CatalogKey,Name=name,Sprite=e.RadiantIcon,Source="Radiant icon",
                RuPrefix="Лучистый ",EnPrefix="Radiant ",Ru=name==null?e.Name:null,Note="лучистый вариант"});
        }
    }
    private void CollectUpgrades()
    {
        foreach(var component in Resources.FindObjectsOfTypeAll<UpgradeComponent>())
        {
            if(component==null||component.WasCollected)continue;
            Sprite icon=null;try{icon=component.Icon;}catch{}
            LocalizedString name=null;try{name=component.Title;}catch{}
            Add(new Job{Folder="09_upgrade_components",Id=component.name,Name=name,Sprite=icon,Source="UpgradeComponent.Icon"});
        }
    }
    private void CollectRewards()
    {
        var database=_app.Map?.Capsules?.Database;
        if(database==null)return;
        foreach(var reward in database.Assets.Values)
        {
            var sprite=_app.Map.Icons.Reward(reward.Key);
            if(!Live(sprite)){_log.Add("reward without icon: "+reward.Key+" "+reward.AssetName);continue;}
            Add(new Job{Folder="10_pod_rewards",Id=reward.Key,Key=reward.Title,Sprite=sprite,Source="Capsule reward ("+reward.Kind+")",
                Note=reward.Kind+" "+reward.AssetName});
        }
    }
    private void CollectMapMarkers()
    {
        foreach(var definition in Resources.FindObjectsOfTypeAll<MapMarkerDefinition>())
        {
            if(definition==null||definition.WasCollected)continue;
            Sprite icon=null;try{icon=definition.MapIcon;}catch{}
            LocalizedString name=null;try{name=definition.MapDisplayName;}catch{}
            Add(new Job{Folder="12_map_markers",Id=definition.name,Name=name,Sprite=icon,Source="MapMarkerDefinition.MapIcon"});
        }
    }
    private void CollectPedia()
    {
        foreach(var entry in Resources.FindObjectsOfTypeAll<PediaEntry>())
        {
            if(entry==null||entry.WasCollected)continue;
            Sprite icon=null,small=null;try{icon=entry.Icon;}catch{}try{small=entry.SmallIcon;}catch{}
            LocalizedString name=null;try{name=entry.Title;}catch{}
            string id=entry.name;string suffix="";try{suffix=entry.PersistenceSuffix??"";}catch{}
            if(Live(icon))Add(new Job{Folder="13_pedia",Id=id,Name=name,Sprite=icon,Source="PediaEntry.Icon",Note=suffix});
            if(Live(small)&&(icon==null||small.Pointer!=icon.Pointer))
                Add(new Job{Folder="13_pedia_small",Id=id,Name=name,Sprite=small,Source="PediaEntry.SmallIcon",Note=suffix});
        }
    }
    private void CollectFandom()
    {
        var atlas=FandomMapMarkers.Atlas;
        if(atlas==null)return;
        var labels=new Dictionary<int,SortedSet<string>>();
        void Label(int cell,string text){if(cell<0||string.IsNullOrWhiteSpace(text))return;if(!labels.TryGetValue(cell,out var set))labels[cell]=set=new SortedSet<string>(StringComparer.Ordinal);set.Add(text);}
        foreach(var e in FandomMapMarkers.Entries)Label(e.Icon,e.FilterKind);
        foreach(int cell in new[]{CanonMapIconPolicy.MapNode,CanonMapIconPolicy.ResearchDrone,CanonMapIconPolicy.PhantomDrone,CanonMapIconPolicy.TreasurePod})
            Label(cell,CanonMapIconPolicy.Label(cell));
        int size=FandomMapMarkers.CellSize;
        foreach(var pair in labels)
        {
            int col=pair.Key%FandomMapMarkers.AtlasColumns,row=pair.Key/FandomMapMarkers.AtlasColumns;
            if(row>=FandomMapMarkers.AtlasRows)continue;
            Add(new Job{Folder="14_fandom_map",Id="cell"+pair.Key.ToString("00"),Ru=string.Join(", ",pair.Value),En="",Texture=atlas,
                Pixels=new Rect(col*size,(FandomMapMarkers.AtlasRows-1-row)*size,size,size),Source="Fandom map atlas cell "+pair.Key,
                Note=CanonMapIconPolicy.Source(pair.Key)});
        }
    }
    private void CollectSprites(bool all)
    {
        int count=0;
        foreach(var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if(count>=SpriteCap)break;
            if(!Live(sprite))continue;
            string name=sprite.name??"";
            bool icon=name.StartsWith("icon",StringComparison.OrdinalIgnoreCase)||name.StartsWith("map",StringComparison.OrdinalIgnoreCase);
            if(!icon&&!all)continue;
            var rect=PixelRect(sprite);
            if(rect.width<12||rect.height<12||(!icon&&(rect.width>1024||rect.height>1024)))continue;
            int before=_queue.Count;
            Add(new Job{Folder=icon?"90_sprites_icon":"91_sprites_other",Id=name,Ru="",En="",Sprite=sprite,Source="Sprite "+sprite.texture.name});
            if(_queue.Count>before)count++;
        }
    }
    private void WritePods()
    {
        var database=_app.Map?.Capsules?.Database;
        var lines=new List<string>{"id\tname\tcategory\tzone\tx\ty\tz\trewards"};
        foreach(var point in _app.Map?.Points??new List<MapTools.Point>())
        {
            if(point==null||point.Category==null||!point.Category.StartsWith("Капсул",StringComparison.Ordinal))continue;
            string rewards="";
            if(database!=null&&point.Id!=null&&database.Pods.TryGetValue(point.Id,out var pod))
                rewards=string.Join(";",pod.Rewards.Select(r=>r.Key+"x"+r.Count));
            lines.Add(string.Join("\t",Tsv(point.Id),Tsv(point.Name),Tsv(point.Category),Tsv(point.GroupName),
                F(point.Position.x),F(point.Position.y),F(point.Position.z),rewards));
        }
        if(database!=null)
        {
            lines.Add("");lines.Add("#pod\tshadow\trewards");
            foreach(var pod in database.Pods.Values)lines.Add("#"+pod.Id+"\t"+(pod.ShadowCache?1:0)+"\t"+string.Join(";",pod.Rewards.Select(r=>r.Key+"x"+r.Count)));
        }
        File.WriteAllLines(Path.Combine(_root,"pods.tsv"),lines,new UTF8Encoding(false));
        _log.Add("pods.tsv rows: "+(lines.Count-1));
    }

    // ---------------- export ----------------
    private void Export(Job job)
    {
        Texture texture;Rect pixels;string spriteName;
        if(job.Sprite!=null)
        {
            if(!Live(job.Sprite)){_skipped++;return;}
            texture=job.Sprite.texture;pixels=PixelRect(job.Sprite);spriteName=job.Sprite.name;
        }
        else{texture=job.Texture;pixels=job.Pixels;spriteName=texture!=null?texture.name:"";}
        if(texture==null||texture.WasCollected||pixels.width<1||pixels.height<1){_skipped++;return;}
        string ru=job.Ru??Resolve(job,_ru),en=job.En??Resolve(job,_en);
        if(string.IsNullOrEmpty(ru)&&string.IsNullOrEmpty(en))
        {
            string current=Resolve(job,null);
            if(Loc.IsRussianCode(_currentCode))ru=current;else en=current;
        }
        int w=Mathf.Max(1,Mathf.RoundToInt(pixels.width)),h=Mathf.Max(1,Mathf.RoundToInt(pixels.height));
        float scale=Mathf.Min(1f,(float)MaxSide/Mathf.Max(w,h));
        if(scale<1f){w=Mathf.Max(1,Mathf.RoundToInt(w*scale));h=Mathf.Max(1,Mathf.RoundToInt(h*scale));}
        var uv=new Rect(pixels.x/texture.width,pixels.y/texture.height,pixels.width/texture.width,pixels.height/texture.height);
        byte[] png=Render(texture,uv,w,h);
        string dir=Path.Combine(_root,job.Folder);
        Directory.CreateDirectory(dir);
        string label;
        if(job.Folder.StartsWith("9",StringComparison.Ordinal))
        {
            // Raw sprite dump: keep the asset name, borrow the official names when a labelled export used the same sprite.
            if(string.IsNullOrEmpty(ru)&&string.IsNullOrEmpty(en)&&!string.IsNullOrEmpty(spriteName)&&_bySprite.TryGetValue(spriteName,out var known)){ru=known.Ru;en=known.En;}
            string shown=!string.IsNullOrEmpty(ru)&&!string.IsNullOrEmpty(en)&&ru!=en?ru+" ["+en+"]":!string.IsNullOrEmpty(ru)?ru:en;
            label=FileLabel(null,null,string.IsNullOrEmpty(shown)?spriteName:spriteName+" — "+shown,spriteName);
        }
        else
        {
            label=FileLabel(ru,en,job.Id,spriteName);
            if(!string.IsNullOrEmpty(spriteName)&&(!string.IsNullOrEmpty(ru)||!string.IsNullOrEmpty(en))&&!_bySprite.ContainsKey(spriteName))_bySprite[spriteName]=(ru,en);
        }
        string file=Unique(job.Folder,label)+".png";
        File.WriteAllBytes(Path.Combine(dir,file),png);
        _rows.Add(new Row{Folder=job.Folder,File=file,Id=job.Id??"",Ru=ru??"",En=en??"",Sprite=spriteName??"",Size=w+"x"+h,Source=job.Source??"",Note=job.Note??""});
    }
    private string Resolve(Job job,UnityLocale locale)
    {
        string text=null;
        try
        {
            LocalizedString source=job.Name;
            if(source==null&&job.Key!=null&&!string.IsNullOrEmpty(job.Key.Table)&&(job.Key.Key!=0||!string.IsNullOrEmpty(job.Key.Name)))
            {
                TableReference table=job.Key.Table.StartsWith("GUID:",StringComparison.Ordinal)?TableReference.GuidFromString(job.Key.Table):(TableReference)job.Key.Table;
                source=job.Key.Key!=0?new LocalizedString(table,job.Key.Key):new LocalizedString(table,job.Key.Name);
            }
            if(source==null)return null;
            if(locale==null)text=Clean(source.GetLocalizedString());
            else
            {
                var copy=new LocalizedString(source.TableReference,source.TableEntryReference);
                copy.LocaleOverride=locale;
                text=Clean(copy.GetLocalizedString());
            }
        }
        catch{text=null;}
        if(text==null)return null;
        string prefix=locale==null?(Loc.IsRussianCode(_currentCode)?job.RuPrefix:job.EnPrefix):ReferenceEquals(locale,_ru)?job.RuPrefix:job.EnPrefix;
        if(!string.IsNullOrEmpty(prefix))text=prefix+(ReferenceEquals(locale,_ru)&&text.Length>0?char.ToLowerInvariant(text[0])+text.Substring(1):text);
        return text;
    }
    private static string Clean(string value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.StartsWith("!!")||value.StartsWith("No translation",StringComparison.OrdinalIgnoreCase))return null;
        string text=Tags.Replace(value,"").Replace("\r"," ").Replace("\n"," ").Replace("\t"," ").Trim();
        return text.Length==0?null:text;
    }
    private static UnityLocale FindLocale(string prefix)
    {
        try
        {
            var list=LocalizationSettings.AvailableLocales?.Locales;
            if(list==null)return null;
            UnityLocale loose=null;
            for(int i=0;i<list.Count;i++)
            {
                var locale=list[i];
                string code=locale!=null?locale.Identifier.Code:null;
                if(code==null)continue;
                if(string.Equals(code,prefix,StringComparison.OrdinalIgnoreCase))return locale;
                if(loose==null&&code.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))loose=locale;
            }
            return loose;
        }
        catch{return null;}
    }
    private static Rect PixelRect(Sprite sprite)
    {
        try{return sprite.textureRect;}
        catch{}
        // Tight-packed atlas sprite: bounding box of its mesh UVs inside the atlas page.
        try
        {
            var uv=sprite.uv;var texture=sprite.texture;
            if(uv!=null&&uv.Length>0&&texture!=null)
            {
                float x0=1,y0=1,x1=0,y1=0;
                for(int i=0;i<uv.Length;i++){var p=uv[i];x0=Mathf.Min(x0,p.x);y0=Mathf.Min(y0,p.y);x1=Mathf.Max(x1,p.x);y1=Mathf.Max(y1,p.y);}
                return new Rect(x0*texture.width,y0*texture.height,(x1-x0)*texture.width,(y1-y0)*texture.height);
            }
        }
        catch{}
        try{return sprite.rect;}catch{return new Rect(0,0,0,0);}
    }
    private static byte[] Render(Texture source,Rect uv,int width,int height)
    {
        var previous=RenderTexture.active;
        var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        Texture2D readable=null;
        try
        {
            RenderTexture.active=target;
            GL.Clear(true,true,new Color(0,0,0,0));
            Graphics.Blit(source,target,new Vector2(uv.width,uv.height),new Vector2(uv.x,uv.y));
            RenderTexture.active=target;
            readable=new Texture2D(width,height,TextureFormat.RGBA32,false);
            readable.ReadPixels(new Rect(0,0,width,height),0,0,false);
            readable.Apply(false);
            var png=ImageConversion.EncodeToPNG(readable);
            var bytes=new byte[png.Length];
            IntPtr native=png.Pointer;
            if(native!=IntPtr.Zero&&Marshal.ReadIntPtr(native,3*IntPtr.Size).ToInt64()==png.Length)
                Marshal.Copy(IntPtr.Add(native,4*IntPtr.Size),bytes,0,bytes.Length);
            else for(int i=0;i<bytes.Length;i++)bytes[i]=png[i];
            return bytes;
        }
        finally
        {
            RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(target);
            if(readable!=null)UnityEngine.Object.Destroy(readable);
        }
    }
    private string Unique(string folder,string label)
    {
        string name=label;
        for(int i=2;!_files.Add(folder+"/"+name);i++)name=label+" ("+i+")";
        return name;
    }
    internal static string FileLabel(string ru,string en,string id,string sprite)
    {
        string text=!string.IsNullOrEmpty(ru)&&!string.IsNullOrEmpty(en)&&!string.Equals(ru,en,StringComparison.Ordinal)?ru+" ["+en+"]":
            !string.IsNullOrEmpty(ru)?ru:!string.IsNullOrEmpty(en)?en:!string.IsNullOrEmpty(id)?id:sprite??"icon";
        var sb=new StringBuilder(text.Length);
        foreach(char c in text)sb.Append(c<32||"\\/:*?\"<>|".IndexOf(c)>=0?'_':c);
        string result=sb.ToString().Trim().TrimEnd('.',' ');
        if(result.Length>110)result=result.Substring(0,110).TrimEnd('.',' ');
        return result.Length==0?"icon":result;
    }

    // ---------------- reports ----------------
    private void Finish()
    {
        try
        {
            var ordered=_rows.OrderBy(r=>r.Folder,StringComparer.Ordinal).ThenBy(r=>r.File,StringComparer.OrdinalIgnoreCase).ToList();
            var tsv=new List<string>{"folder\tfile\tid\tru\ten\tsprite\tsize\tsource\tnote"};
            foreach(var r in ordered)tsv.Add(string.Join("\t",Tsv(r.Folder),Tsv(r.File),Tsv(r.Id),Tsv(r.Ru),Tsv(r.En),Tsv(r.Sprite),r.Size,Tsv(r.Source),Tsv(r.Note)));
            File.WriteAllLines(Path.Combine(_root,"icons.tsv"),tsv,new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_root,"index.html"),Html(ordered),new UTF8Encoding(false));
            _log.Add("written "+_rows.Count+" failed "+_failed+" skipped "+_skipped+" seconds "+(Time.unscaledTime-_started).ToString("0.0",CultureInfo.InvariantCulture));
            foreach(var group in ordered.GroupBy(r=>r.Folder))_log.Add("folder "+group.Key+": "+group.Count());
            File.WriteAllLines(Path.Combine(_root,"export.log"),_log,new UTF8Encoding(false));
        }
        catch(Exception e){_app.LoggerInstance.Warning("[icons] report: "+e);}
        Status="готово: "+_rows.Count+" PNG, ошибок "+_failed+", пропущено "+_skipped;
        _app.Notify("Иконки игры: "+Status+" → "+_root);
    }
    private static string Html(List<Row> rows)
    {
        var sb=new StringBuilder();
        sb.Append("<!doctype html><meta charset=utf-8><title>Slime Rancher 2 — иконки игры</title><style>")
          .Append("body{font:13px Segoe UI,Arial,sans-serif;background:#1d2230;color:#e8ecf4;margin:20px}h2{margin:28px 0 8px;color:#ffd36e}")
          .Append(".g{display:flex;flex-wrap:wrap;gap:8px}.c{width:132px;background:#2a3142;border-radius:8px;padding:6px;text-align:center}")
          .Append(".c img{width:96px;height:96px;object-fit:contain;background:repeating-conic-gradient(#323a4d 0 25%,#2a3142 0 50%) 0 0/16px 16px;border-radius:6px}")
          .Append(".r{font-weight:600;word-break:break-word}.e{color:#a9b4c8;word-break:break-word}.i{color:#6f7a90;font-size:11px;word-break:break-all}</style>")
          .Append("<h1>Slime Rancher 2 — иконки из файлов игры</h1><p>Подписи: русское имя, English name, внутренний id. Полный список — icons.tsv.</p>");
        foreach(var group in rows.GroupBy(r=>r.Folder))
        {
            sb.Append("<h2>").Append(Esc(group.Key)).Append(" (").Append(group.Count()).Append(")</h2><div class=g>");
            foreach(var r in group)
                sb.Append("<div class=c><img loading=lazy src=\"").Append(Uri.EscapeDataString(r.Folder)).Append('/').Append(Uri.EscapeDataString(r.File))
                  .Append("\"><div class=r>").Append(Esc(r.Ru)).Append("</div><div class=e>").Append(Esc(r.En)).Append("</div><div class=i>")
                  .Append(Esc(r.Id)).Append(" · ").Append(r.Size).Append("</div></div>");
            sb.Append("</div>");
        }
        return sb.ToString();
    }
    private static string Esc(string s)=>(s??"").Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;");
    private static string Tsv(string s)=>(s??"").Replace("\t"," ").Replace("\r"," ").Replace("\n"," ");
    private static string F(float v)=>v.ToString("0.##",CultureInfo.InvariantCulture);
    private static bool Live(Sprite sprite)=>sprite!=null&&!sprite.WasCollected&&sprite.texture!=null;
}
