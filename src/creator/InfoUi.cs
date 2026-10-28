using System;
using UnityEngine;

namespace CreatorToolkit;

internal sealed partial class ToolkitUi
{
    private bool _infoPage;
    internal void ShowInfoForTest()
    {ShowTabForTest(7);_infoPage=true;}
    internal bool InfoVisibleForTest=>_app.MenuOpen&&_tab==7&&_infoPage;

    private void InfoPage(Rect area)
    {
        BeginGroup(area);
        try
        {
            GUI.Label(new Rect(0,0,area.width-260,40),"Консоль / Инфо",_heading);
            if(Button(new Rect(area.width-242,0,242,44),"Назад к удобствам",_button))
            {_infoPage=false;ClearInteraction();return;}
            float col=(area.width-24)/2,x=col+24;
            GUI.Box(new Rect(0,68,col,210),GUIContent.none,_card);
            GUI.Label(new Rect(20,80,col-40,36),"Creator Toolkit · "+ToolkitMod.Version,_tableHeading);
            GUI.Label(new Rect(20,126,col-40,128),
                "Мир: "+(_app.Map.Busy?"подготовка безопасного перехода":_app.Ready?"загружен":"не загружен")+
                "\nОткрыть меню: Insert · закрыть: Esc"+
                "\nF1 не открывает мод"+
                "\nНативных обработчиков: "+_app.InstalledHooks,_muted);
            GUI.Box(new Rect(x,68,col,210),GUIContent.none,_card);
            GUI.Label(new Rect(x+20,80,col-40,36),"Оформление и ресурсы",_tableHeading);
            GUI.Label(new Rect(x+20,126,col-40,128),
                "Шрифт: "+_fontDisplayName+
                "\nИсточник: "+(FontNativeForTest?"игровой ресурс":_fontSource)+
                "\nКириллица: "+(FontCyrillicForTest?"проверена":"недоступна")+
                "\nИконки вкладок: "+_app.Map.Icons.TabCount+" / 8 · игровые спрайты",_muted);
            GUI.Box(new Rect(0,298,area.width,248),GUIContent.none,_card);
            GUI.Label(new Rect(20,310,area.width-40,36),"Состояние",_tableHeading);
            string status=_app.Audit?.Active==true?_app.Audit.Status:_app.Status;
            GUI.Label(new Rect(20,354,area.width-40,76),status,_wrap);
            string error=string.IsNullOrWhiteSpace(_app.LastError)?"Последних ошибок нет.":"Последняя ошибка: "+_app.LastError;
            GUI.Label(new Rect(20,442,area.width-40,78),error,_muted);
            GUI.Label(new Rect(0,562,area.width,30),
                "Временные эффекты сбрасываются при смене сейва. Полные диагностические сведения — в журнале мода.",_muted);
        }
        finally{EndGroup();}
    }
}