using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CreatorToolkit;

// Read-only extra map layers. Positions (x,y,z) are baked offline from the
// installed game's scene-group/scene data (DiscoverableResources, PuzzleSlotLockable,
// PuzzleSlot, PlortDepositor, radiant-only DirectedSlimeSpawner) and gated by
// catalog.hash like MapIndex.tsv. Fandom wiki descriptions are attached as hints by
// nearest neighbour. Rows with Exact=0 are wiki picture estimates (no game object).
internal static class FandomMapMarkers
{
    internal const string GatherCategory="Ресурсы: ценные";
    internal const string RareCategory="Редкое";
    internal const int AtlasColumns=6,AtlasRows=4,CellSize=64;
    internal const string SourceNote="Источник: интерактивная карта Fandom-вики. Координаты пересчитаны с картинки, точность ≈5–10 м; высота неизвестна.";
    internal const string ExactNote="Координаты X/Y/Z взяты из данных игры (точно до метра).";

    internal sealed class Entry
    {
        internal string Id,Layer,Kind,Group,Description,Refs="",StateKey="",FilterKind="";
        internal float X,Y,Z;
        internal int Icon;
        internal bool Exact;
    }
    private static List<Entry> _entries;
    private static Dictionary<string,string> _hints;
    private static Dictionary<string,int> _kindIcons,_idIcons;
    internal static string Catalog {get;private set;}="";
    private static Texture2D _atlas;
    internal static string Status {get;private set;}="не загружено";

    internal static bool IsFandom(string category)=>category==GatherCategory||category==RareCategory;

    internal static IReadOnlyList<Entry> Entries{get{Load();return _entries;}}
    internal static IReadOnlyDictionary<string,string> Hints{get{Load();return _hints;}}

    private static void Load()
    {
        if(_entries!=null)return;
        _entries=new List<Entry>();_hints=new Dictionary<string,string>(StringComparer.Ordinal);
        _kindIcons=new Dictionary<string,int>(StringComparer.Ordinal);_idIcons=new Dictionary<string,int>(StringComparer.Ordinal);
        try
        {
            using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CreatorToolkit.FandomMarkers.tsv");
            if(stream==null){Status="встроенный файл меток вики не найден";return;}
            using var reader=new StreamReader(stream);
            string line;
            while((line=reader.ReadLine())!=null)
            {
                if(line.StartsWith("#catalog=",StringComparison.Ordinal)){Catalog=line.Substring(9).Split(' ')[0].Trim();continue;}
                if(line.Length==0||line[0]=='#')continue;
                var c=line.Split('\t');
                if(c[0]=="H"&&c.Length>=3){_hints[c[1]]=c[2];continue;}
                if(c[0]!="M"||c.Length<10)continue;
                if(!float.TryParse(c[5],NumberStyles.Float,CultureInfo.InvariantCulture,out float x)||
                   !float.TryParse(c[6],NumberStyles.Float,CultureInfo.InvariantCulture,out float y)||
                   !float.TryParse(c[7],NumberStyles.Float,CultureInfo.InvariantCulture,out float z)||
                   !int.TryParse(c[8],NumberStyles.Integer,CultureInfo.InvariantCulture,out int icon))continue;
                var entry=new Entry{Id=c[1],Layer=c[2],Kind=c[3],Group=c[4],X=x,Y=y,Z=z,Icon=icon,Description=c[9],
                    Refs=c.Length>10?c[10]:"",StateKey=c.Length>11?c[11]:"",FilterKind=c.Length>12&&c[12].Length>0?c[12]:c[3],Exact=c.Length>13&&c[13]=="1"};
                _entries.Add(entry);_kindIcons[entry.FilterKind]=icon;_idIcons[entry.Id]=icon;
            }
            Status="доп. метки: "+_entries.Count+", подсказки вики: "+_hints.Count;
        }
        catch(Exception e){Status="ошибка чтения меток вики: "+e.Message;}
    }

    internal static int IconFor(MapTools.Point point)
    {
        if(point==null||!IsFandom(point.Category))return -1;
        Load();
        return point.Id!=null&&_idIcons.TryGetValue(point.Id,out int icon)?icon:-1;
    }
    internal static int CategoryIcon(string category)
    {
        Load();
        string kind=category==GatherCategory?"Странный алмаз":category==RareCategory?"Врата плортов":null;
        return kind!=null&&_kindIcons.TryGetValue(kind,out int icon)?icon:-1;
    }

    // One process-wide texture, created once (not per reload / not per repaint).
    // 64 px wiki cells drawn at 14-32 px need a mip chain to stay as smooth as
    // on the wiki; cells are 64 px aligned, so box mips down to 1 px per cell
    // (far below any marker size) never mix neighbouring cells.
    internal static Texture2D Atlas
    {
        get
        {
            if(_atlas!=null&&!_atlas.WasCollected)return _atlas;
            _atlas=null;
            try
            {
                using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CreatorToolkit.FandomAtlas.rgba");
                if(stream==null)return null;
                int w=AtlasColumns*CellSize,h=AtlasRows*CellSize;
                using var reader=new BinaryReader(stream);byte[] rgba=reader.ReadBytes(w*h*4);
                if(rgba.Length!=w*h*4)return null;
                var texture=new Texture2D(w,h,TextureFormat.RGBA32,true);
                texture.name="CreatorToolkit_FandomMapAtlas";texture.hideFlags=HideFlags.HideAndDontSave;
                texture.filterMode=FilterMode.Trilinear;texture.wrapMode=TextureWrapMode.Clamp;
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {int i=(y*w+x)*4;texture.SetPixel(x,h-1-y,new Color(rgba[i]/255f,rgba[i+1]/255f,rgba[i+2]/255f,rgba[i+3]/255f));}
                texture.Apply(true,true);
                _atlas=texture;
            }
            catch{_atlas=null;}
            return _atlas;
        }
    }
}
