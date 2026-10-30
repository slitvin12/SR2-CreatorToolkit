using System;
using UnityEngine;

namespace CreatorToolkit;

internal sealed partial class ToolkitUi
{
    private bool _phenomenaPage;
    private Vector2 _phenomenaScroll;
    private string _phenomenaSelection="";
    internal void ShowPhenomenaForTest(string selection="")
    {
        ShowTabForTest(5);_phenomenaPage=true;_phenomenaSelection=selection;_app.Phenomena.SetInspect(true);
    }
    private void PhenomenaPage(float width)
    {
        var tools=_app.Phenomena;
        L.Label(new Rect(0,0,width-220,40),"Обсерватория нативных феноменов",_heading);
        if(Button(new Rect(width-208,0,208,40),"Назад к съёмке",_button))
        {_phenomenaPage=false;tools.SetInspect(false);}
        Hint(new Rect(0,55,width,62),
            "Aurora: настоящие суточные окна и состояние. Антигравитация: только существующие локальные объёмы. "+
            "Исследование не создаёт неиспользуемые префабы и не меняет физику мира.",_muted);
        if(Button(new Rect(0,132,260,44),tools.Inspect?"Исследование: ВКЛ":"Исследование: ВЫКЛ",
            tools.Inspect?_selectedButton:_button))_app.Queue("phenomena inspector",()=>tools.SetInspect(!tools.Inspect));
        if(Button(new Rect(274,132,242,44),"Обновить сцену",_button))_app.Queue("phenomena census",tools.Refresh);
        Rect view=new(0,192,width,266);L.Box(view,GUIContent.none,_card);
        _phenomenaScroll=BeginScroll(67,new Rect(10,202,width-20,246),_phenomenaScroll,tools.Rows.Count*72,72);
        try
        {
            for(int i=0;i<tools.Rows.Count;i++)
            {
                var row=tools.Rows[i];float y=i*72-_phenomenaScroll.y;
                if(y+64<0||y>246)continue;
                if(Button(new Rect(0,y,width-72,64),GUIContent.none,
                    row.Id==_phenomenaSelection?_selectedButton:_button))_phenomenaSelection=row.Id;
                var rowText=row.Id==_phenomenaSelection?_selectedRowText:_muted;
                L.Label(new Rect(14,y+3,width*.58f-24,56),row.Kind+"\n"+row.Name,rowText);
                L.Label(new Rect(width*.60f,y+3,width*.32f,56),row.State+(row.CanWait?
                    "\nТвёрдая фаза "+row.StartHour.ToString("F2")+"–"+row.EndHour.ToString("F2")+" ч":""),rowText);
            }
            if(tools.Rows.Count==0)L.Label(new Rect(16,16,width-100,96),tools.Status,_muted);
        }
        finally{EndGroup();}
        var selected=tools.Rows.Find(r=>r.Id==_phenomenaSelection);
        bool enabled=GUI.enabled;
        GUI.enabled=enabled&&selected!=null&&!_app.Map.Busy;
        if(Button(new Rect(0,478,300,44),"Безопасно к месту…",_accentButton))
            _app.Queue("safe native phenomenon travel",()=>tools.RequestTravel(selected));
        GUI.enabled=enabled&&selected?.CanWait==true&&!_app.Map.Busy;
        if(Button(new Rect(316,478,360,44),"Дождаться штатной фазы…",_goldButton))tools.RequestWait(selected);
        GUI.enabled=enabled;
        L.Label(new Rect(0,540,width,50),tools.Status,_muted);
    }
}