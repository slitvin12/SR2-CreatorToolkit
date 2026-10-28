using System;
using UnityEngine;
namespace CreatorToolkit;
internal sealed partial class ToolkitUi
{
    private bool _refineryPage,_refineryDropdown;
    private string _refinerySelection="";
    private Vector2 _refineryScroll;
    private void RefineryPage(Rect area)
    {
        var tools=_app.Refinery;
        GUI.Label(new Rect(0,0,area.width-230,38),"Ресурсы Преобразователя",_heading);
        if(Button(new Rect(area.width-210,0,210,40),"Назад к гаджетам",_button)){_refineryPage=false;_refineryDropdown=false;return;}
        GUI.Label(new Rect(0,56,area.width,58),"Настоящее хранилище Лаборатории. Каждое пополнение требует подтверждения и сохраняется игрой.",_muted);
        float half=(area.width-16)/2;
        bool originalEnabled=GUI.enabled;
        GUI.enabled=originalEnabled&&tools.Limit>0&&tools.Rows.Count>0;
        if(Button(new Rect(0,126,half,48),"+100 ко всем ресурсам…",_accentButton))_app.Queue("refinery all +100 request",()=>tools.Request(false));
        if(Button(new Rect(half+16,126,half,48),"Заполнить все до "+tools.Limit+"…",_goldButton))_app.Queue("refinery fill request",()=>tools.Request(true));
        var selected=tools.Rows.Find(r=>r.Reference==_refinerySelection);
        GUI.Label(new Rect(0,196,area.width,32),"Точечное пополнение · выберите ресурс",_tableHeading);
        string label=selected==null?"Выберите ресурс ▾":selected.Name+" · "+selected.Count+" / "+tools.Limit+" ▾";
        if(Button(new Rect(0,238,area.width,44),label,_button))_refineryDropdown=!_refineryDropdown;
        if(_refineryDropdown)
        {
            GUI.Box(new Rect(0,292,area.width,218),GUIContent.none,_card);
            _refineryScroll=BeginScroll(68,new Rect(10,302,area.width-20,198),_refineryScroll,tools.Rows.Count*48,48);
            try
            {
                for(int i=0;i<tools.Rows.Count;i++)
                {
                    var row=tools.Rows[i];float y=i*48-_refineryScroll.y;
                    if(y+44<=0||y>=198)continue;
                    if(Button(new Rect(0,y,area.width-76,42),row.Name+" · "+row.Count,_refinerySelection==row.Reference?_selectedButton:_button))
                    {_refinerySelection=row.Reference;_refineryDropdown=false;}
                }
            }
            finally{EndGroup();}
        }
        else
        {
            bool enabled=GUI.enabled;GUI.enabled=enabled&&selected!=null;
            string reference=selected?.Reference;
            if(Button(new Rect(0,302,half,46),"+100 выбранному…",_accentButton))_app.Queue("refinery selected +100 request",()=>tools.Request(false,reference));
            if(Button(new Rect(half+16,302,half,46),"Выбранный до "+tools.Limit+"…",_button))_app.Queue("refinery selected fill request",()=>tools.Request(true,reference));
            GUI.enabled=originalEnabled;
            if(Button(new Rect(0,374,260,42),"Обновить количества",_button))_app.Queue("refinery refresh",tools.Refresh);
            GUI.Label(new Rect(0,438,area.width,70),"Не уменьшает запасы выше лимита. Не добавляет слаймов, гаджеты или неизвестные ID. Нативные ограничения ёмкости сохранены.",_muted);
        }
        GUI.enabled=originalEnabled;
        GUI.Label(new Rect(0,528,area.width,66),tools.Status,_muted);
    }
}
