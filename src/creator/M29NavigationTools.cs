using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace CreatorToolkit;

// M29 task 4: minimap and compass settings kept in UserData\CreatorToolkit\navigation.cfg.
// The atlas button, the navigation page and the test relay change the live tools; their state
// is read twice a second and written (debounced, atomically) once it changed, so every path is
// saved without extra wiring. The shown minimap range follows the chosen one smoothly.
internal sealed class NavigationTools
{
    private const float CaptureSeconds=.5f,SaveDelay=1f;
    private readonly ToolkitMod _app;
    private float _nextCapture,_saveAt=-1f,_lastTick=-1f;
    private string _saved="";
    internal M29NavigationSettings Settings{get;private set;}=new();
    // Range drawn by the minimap and composed by the terrain layer (metres from the centre to the marker rim).
    internal float Range{get;private set;}=M29NavigationSettings.DefaultRange;
    internal bool Zooming=>Range!=Settings.Range;
    internal string FilePath=>Path.Combine(_app.DataDirectory??"",M29NavigationSettings.FileName);
    internal string Status{get;private set;}="Навигация: настройки ещё не загружены";
    internal int Saves{get;private set;}
    internal int Applied{get;private set;}
    internal int Rejected{get;private set;}
    internal string LastError{get;private set;}="";
    internal bool Pending=>_saveAt>=0;
    internal NavigationTools(ToolkitMod app)=>_app=app;

    internal void Load()
    {
        string path=FilePath;int applied=0,rejected=0;
        try
        {
            if(File.Exists(path))
            {
                Settings=M29NavigationSettings.Parse(File.ReadAllText(path),out applied,out rejected);
                _saved=Settings.Serialize();
                Status="Навигация: загружено из "+M29NavigationSettings.FileName+" · ключей "+applied+(rejected>0?" · пропущено "+rejected:"");
            }
            else{Settings=new M29NavigationSettings();_saved="";Status="Навигация: настройки по умолчанию (файл появится после первого изменения)";}
            LastError="";
        }
        catch(Exception e)
        {
            Settings=new M29NavigationSettings();_saved="";LastError=e.Message;
            Status="Навигация: файл не прочитан ("+e.Message+") · настройки по умолчанию";
            _app.LoggerInstance.Warning("Navigation settings read failed: "+e.Message);
        }
        Applied=applied;Rejected=rejected;_saveAt=-1f;
        Range=Settings.Range;
        Apply();
        _app.LoggerInstance.Msg("Navigation settings: "+Settings.Describe()+" (applied "+applied+", rejected "+rejected+")");
    }

    // Minimap mode and compass toggle into the live tools (load, page, relay).
    private void Apply()
    {
        var map=_app.Map;
        if(map!=null)map.ShowMiniMap=Settings.Mode!=0;
        _app.MiniMapTerrain?.SetEnabled(Settings.Mode==1);
        var compass=_app.Compass;
        if(compass!=null&&compass.Wanted!=Settings.Compass)compass.SetEnabled(Settings.Compass);
        Refresh();
    }
    private void Refresh(){_app.Map?.RefreshMiniMapSoon();_app.Compass?.RefreshSoon();}

    internal void Tick()
    {
        float now=Time.unscaledTime;
        float dt=_lastTick<0?0f:now-_lastTick;_lastTick=now;
        if(Range!=Settings.Range)Range=M29MiniMapZoom.Step(Range,Settings.Range,dt);
        if(now>=_nextCapture){_nextCapture=now+CaptureSeconds;Capture();}
        if(_saveAt>=0&&now>=_saveAt){_saveAt=-1f;Save();}
    }
    // The atlas button and the radar page toggle the tools directly: adopt their state.
    private void Capture()
    {
        var map=_app.Map;
        if(map==null)return;
        var terrain=_app.MiniMapTerrain;
        int mode=!map.ShowMiniMap?0:terrain!=null&&terrain.Enabled?1:2;
        bool compass=_app.Compass?.Wanted??Settings.Compass;
        if(mode==Settings.Mode&&compass==Settings.Compass)return;
        Settings.Mode=mode;Settings.Compass=compass;Changed();
    }
    // Saved one second after the first unsaved change.
    private void Changed(){if(_saveAt<0)_saveAt=Time.unscaledTime+SaveDelay;}
    private void Save()
    {
        string text=Settings.Serialize();
        if(text==_saved)return;
        string path=FilePath,temp=path+".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)??".");
            File.WriteAllText(temp,text);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            _saved=text;Saves++;LastError="";
            Status="Навигация: сохранено в "+M29NavigationSettings.FileName+" · "+DateTime.Now.ToString("HH:mm:ss",CultureInfo.InvariantCulture);
        }
        catch(Exception e)
        {
            LastError=e.Message;Status="Навигация: не сохранено ("+e.Message+")";
            _app.LoggerInstance.Warning("Navigation settings save failed: "+e.Message);
            try{if(File.Exists(temp))File.Delete(temp);}catch{}
        }
    }
    // Quit: the last change is written at once.
    internal void Flush()
    {
        try{Capture();_saveAt=-1f;Save();}
        catch(Exception e){_app.LoggerInstance.Warning("Navigation settings flush failed: "+e.Message);}
    }

    // ---- changes from the navigation page and the relay ----
    internal void SetMode(int mode){Settings.Mode=Math.Clamp(mode,0,M29NavigationSettings.Modes.Length-1);Apply();Changed();}
    internal void SetCompass(bool on){Settings.Compass=on;Apply();Changed();}
    internal void SetRange(float metres)
    {
        float range=M29NavigationSettings.Whole(metres);
        if(range==Settings.Range)return;
        Settings.Range=range;_app.Map?.RefreshMiniMapSoon();Changed();
    }
    internal void ZoomIn()=>SetRange(M29NavigationSettings.ZoomIn(Settings.Range));
    internal void ZoomOut()=>SetRange(M29NavigationSettings.ZoomOut(Settings.Range));
    // Size, corner, transparency, orientation, compass range, marker limit, phantom drones.
    internal void Edit(Action<M29NavigationSettings> change,bool refresh=true)
    {
        if(change==null)return;
        string before=Settings.Serialize();
        change(Settings);
        Settings.Transparency=M29NavigationSettings.ClampTransparency(Settings.Transparency);
        Settings.CompassRange=M29NavigationSettings.ClampCompassRange(Settings.CompassRange);
        Settings.CompassMarkers=M29NavigationSettings.ClampMarkers(Settings.CompassMarkers);
        Settings.Range=M29NavigationSettings.Whole(Settings.Range);
        if(Settings.Serialize()==before)return;
        if(refresh)Refresh();else _app.Map?.RefreshMiniMapSoon();
        Changed();
    }
    // Relay: one key=value pair, same keys as the file.
    internal bool Set(string key,string value)
    {
        if(!Settings.Set(key,value))return false;
        string k=(key??"").Trim().ToLowerInvariant();
        if(k=="minimap"||k=="compass")Apply();else Refresh();
        Changed();
        return true;
    }
    internal void ResetLook(){Settings.ResetLook();Refresh();Changed();}
    internal string State()=>"file="+FilePath+" exists="+File.Exists(FilePath)+" saves="+Saves+" pending="+Pending+
        " shown="+Range.ToString("0.0",CultureInfo.InvariantCulture)+" | "+Settings.Describe()+" | "+Status+
        (LastError.Length>0?" | error="+LastError:"");
}
