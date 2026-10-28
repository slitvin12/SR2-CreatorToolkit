using CreatorToolkit;
using P = CreatorToolkit.RadiantCatalogPolicy;

internal static class RadiantCatalogRegression
{
    // Runs the real production pure policy. No Unity/native calls or save access.
    // StaticWiring below is intentionally separate and is NOT behavioral proof.
    internal static void Run(Action<bool,string> check,string root)
    {
        void Check(bool value,string name)=>check(value,"radiant / "+name);
        string[] tokens={"Pink","Cotton","Tabby","Phosphor","Angler","Rock","Batty","Flutter","Ringtail","Boom","Honey","Puddle","Crystal","Hunter","Fire","Saber","Tangle","Dervish","Yolky","Sloomber","Twin","Hyper"};
        string[] russian={"Розовый","Хлопковый","Мяуровый","Фосфорный","Удильщик","Скалистый","Нетопырёк","Порхающий","Кольцехвостый","Бум","Медовый","Лужица","Кристальный","Охотник","Огненный","Саблезубый","Вьюнок","Дервиш","Слайм-желток","Дремотный","Сиамский","Гиперслайм"};
        Check(P.Ordered.Count==22,"pure exact requested species count");
        Check(P.Ordered.Select(s=>s.Token).Distinct(StringComparer.Ordinal).Count()==22,"pure species tokens unique");
        Check(P.Ordered.Select(s=>s.Key).Distinct(StringComparer.Ordinal).Count()==22,"pure variant keys unique");
        var allKeys=tokens.SelectMany(t=>new[]{P.CatalogKey("SlimeDefinition."+t,false),P.CatalogKey("SlimeDefinition."+t,true)}).ToArray();
        Check(allKeys.Distinct(StringComparer.Ordinal).Count()==44,"pure all ordinary and radiant species keys disjoint");
        Check(P.CatalogKey(null,false)==null&&P.CatalogKey(null,true)==null,"pure missing reference remains missing in both variants");
        Check(!tokens.Contains("Rad")&&!tokens.Contains("Mosaic")&&!P.TryGet("rad",out _)&&!P.TryGet("CreatorToolkit.VisualPreview.rad",out _),"pure radiant contract is not radioactive visual prototype");
        for(int i=0;i<tokens.Length;i++)
        {
            string token=tokens[i],reference="SlimeDefinition."+token;
            var species=P.Ordered[i];
            Check(species.Token==token&&species.Russian==russian[i],"pure ordered species and Russian name "+i+" "+token);
            Check(species.Reference==reference,"pure exact native reference "+token);
            Check(species.Key==reference+"|radiant"&&P.CatalogKey(reference,true)==species.Key,"pure stable variant key "+token);
            Check(P.CatalogKey(reference,false)==reference&&P.CatalogKey(reference,false)!=species.Key,"pure ordinary key never aliases variant "+token);
            Check(P.TryGet(token,out var found)&&found==species,"pure exact token resolves requested species "+token);
            Check(!P.TryGet(token.ToLowerInvariant(),out var invalid)&&invalid==default(P.Species),"pure case-mismatched token fails closed "+token);
            string search=species.Search("ЛОКАЛЬНОЕ ИМЯ");
            Check(new[]{"лучист","лучистый","radiant","shimmering","glowing"}.All(search.Contains),"pure Russian and English radiant search aliases "+token);
            Check(search.Contains(token.ToLowerInvariant())&&search.Contains(russian[i].ToLowerInvariant())&&search.Contains(reference.ToLowerInvariant())&&search.Contains("локальное имя")&&search==search.ToLowerInvariant(),"pure search retains exact species and localized provenance "+token);
            Check(species.Search(null).Contains(token.ToLowerInvariant())&&species.Reference==reference&&species.Key==reference+"|radiant","pure missing localization cannot change identity "+token);
            Check(!P.TryGet(russian[i],out _)&&!P.TryGet(reference,out _)&&species.Search("Rock SlimeDefinition.Rock").Contains("rock")&&P.TryGet(token,out var unchanged)&&unchanged==species,"pure display or reference text is not a token binding "+token);
        }
        string[] invalidTokens={null,""," ","Pink "," Pink","PinkLargo","RadiantPink","PinkRadiant","SlimeDefinition.Pink","Pink|radiant","SlimeDefinition.Pinkish","Мяуровый"};
        foreach(string invalid in invalidTokens)
            Check(!P.TryGet(invalid,out var absent)&&absent==default(P.Species),"pure exact lookup rejects null empty padded substring or display identity ["+invalid+"]");
        // Independent Boolean oracle covers every support gate, including invalid counts.
        foreach(int matches in new[]{-1,0,1,2})
        foreach(bool exact in new[]{false,true})
        foreach(bool largo in new[]{false,true})
        foreach(bool appearance in new[]{false,true})
        foreach(bool prefab in new[]{false,true})
        foreach(int components in new[]{-1,0,1,2})
        {
            bool expected=matches==1&&exact&&!largo&&appearance&&prefab&&components==1;
            string reason=P.Availability(matches,exact,largo,appearance,prefab,components);
            Check(expected?reason==null:!string.IsNullOrWhiteSpace(reason),"pure support truth table "+matches+"/"+exact+"/"+largo+"/"+appearance+"/"+prefab+"/"+components);
        }
        var reasons=new[]{
            P.Availability(0,true,false,true,true,1),P.Availability(2,true,false,true,true,1),
            P.Availability(1,false,false,true,true,1),P.Availability(1,true,true,true,true,1),
            P.Availability(1,true,false,false,true,1),P.Availability(1,true,false,true,false,1),
            P.Availability(1,true,false,true,true,0),P.Availability(1,true,false,true,true,2)};
        for(int i=0;i<reasons.Length;i++)
            Check(!string.IsNullOrWhiteSpace(reasons[i])&&reasons.Where((_,j)=>j!=i).All(r=>r!=reasons[i]),"pure distinct missing ambiguity identity largo appearance prefab component reason "+i);
        StaticWiring(Check,root);
    }

    private static void StaticWiring(Action<bool,string> Check,string root)
    {
        // These inspect source structure only: no native rollback, stale-session,
        // pointer hit-test, rendering, appearance persistence or spawning is executed.
        string Compact(string text)=>string.Concat(text.Where(c=>!char.IsWhiteSpace(c)));
        string Section(string text,string begin,string end)
        {
            int start=text.IndexOf(begin,StringComparison.Ordinal);
            if(start<0)return "";
            int finish=text.IndexOf(end,start+begin.Length,StringComparison.Ordinal);
            return finish<0?"":text.Substring(start,finish-start);
        }
        string actors=Compact(File.ReadAllText(Path.Combine(root,"src","creator","ActorTools.cs")));
        string ui=Compact(File.ReadAllText(Path.Combine(root,"src","creator","ToolkitUi.cs")));
        string resolve=Section(actors,"privatestaticSlimeDefinitionResolveRadiantBase(","privatestaticvoidAddRadiantEntries(");
        Check(resolve.Contains("TryGetValue(species.Reference,outregistered)")&&resolve.Contains("registered.Pointer==definition.Pointer")&&resolve.Contains("string.Equals(definition.ReferenceId,species.Reference,StringComparison.Ordinal)")&&resolve.Contains("string.Equals(definition.name,species.Token,StringComparison.Ordinal)"),"static wiring exact native registration and species identity gate");
        string preflight=Section(actors,"privateSlimeDefinitionRequireRadiantEntry(","privatevoidSpawnRadiant(");
        Check(preflight.Contains("!Catalog.Contains(entry)")&&preflight.Contains("entry.IsPrototype")&&preflight.Contains("TryGet(entry.RadiantToken")&&preflight.Contains("entry.Type.Pointer!=definition.Pointer")&&preflight.Contains("entry.CatalogKey,species.Key,StringComparison.Ordinal"),"static wiring stale entry and exact variant preflight present not behavior tested");
        string spawn=Section(actors,"privatevoidSpawnRadiant(","privateboolRollbackRadiantActor(");
        Check(spawn.IndexOf("RequireRadiantEntry(entry)",StringComparison.Ordinal)>=0&&spawn.IndexOf("RequireRadiantEntry(entry)",StringComparison.Ordinal)<spawn.IndexOf("InstantiateActorModel(",StringComparison.Ordinal),"static wiring variant revalidation precedes actor mutation");
        Check(spawn.Contains("radiant.SetRadiant(definition);")&&spawn.Contains("varapplied=radiant.RadiantBaseType;")&&spawn.Contains("!radiant.IsRadiant||applied==null||applied.Pointer!=definition.Pointer")&&spawn.IndexOf("pending.Add(",StringComparison.Ordinal)>spawn.IndexOf("radiant.SetRadiant(",StringComparison.Ordinal),"static wiring SetRadiant and exact readback precede batch publication");
        Check(spawn.IndexOf("try{",StringComparison.Ordinal)<spawn.IndexOf("radiant.SetRadiant(",StringComparison.Ordinal)&&spawn.Contains("catch(ExceptioncreationError)")&&spawn.Contains("RollbackRadiantActor(ctx,currentModel,currentObject,errors)")&&spawn.Contains("for(inti=pending.Count-1;i>=0;i--)")&&spawn.Contains("RollbackRadiantActor(ctx,actor.Model,actor.Object,errors)")&&spawn.Contains("_owned.Add(currentObject)")&&spawn.Contains("_owned.Add(actor.Object)"),"static wiring catch covers batch rollback and failed object cleanup retry tracking not execution");
        string store=Section(actors,"internalboolCanStore(","internalvoidRequestAmmo(");
        Check(store.Contains("if(entry?.IsRadiant==true)returnfalse;")&&store.IndexOf("IsRadiant",StringComparison.Ordinal)<store.IndexOf("PlayerState",StringComparison.Ordinal),"static wiring radiant ammo refusal precedes native ammo access");
        string fixture=Section(actors,"internalGameObjectSpawnAtForTest(","privateCurrencyDefinitionNewbucks(");
        Check(fixture.Contains("_app.Audit.GuardStressCopy();")&&fixture.Contains("entry.IsPrototype||entry.IsRadiant||")&&fixture.IndexOf("GuardStressCopy",StringComparison.Ordinal)<fixture.IndexOf("InstantiateActorModel",StringComparison.Ordinal),"static wiring test spawn retains audit guard and rejects radiant variants");
        Check(actors.Contains("internalSpriteIcon=>IsRadiant?RadiantIcon:Type?.Icon;")&&actors.Contains("Family=\"Лучистые\"")&&actors.Contains("Search=species.Search(name)"),"static wiring variant entry icon family and search consume explicit contract");
        string preview=Section(ui,"privatevoidSpritePreview(","privatevoidDrawSprite(");
        string variant=Section(preview,"if(entry.IsRadiant)","if(entry.IsPrototype)");
        Check(variant.Contains("varradiantIcon=entry.Icon;")&&variant.Contains("radiantIcon.WasCollected")&&variant.Contains("radiantIcon.texture==null")&&variant.Contains("return;")&&!variant.Contains("Type?.Icon")&&!variant.Contains("Prototype"),"static wiring missing radiant art never falls back to ordinary or prototype art");
        string catalog=Section(ui,"privatevoidCatalog(","privatevoidSpawnPage(");
        Check(catalog.Contains("selection?.CatalogKey")&&catalog.Contains("string.Equals(e.CatalogKey,selectedKey,StringComparison.Ordinal)"),"static wiring refreshed catalog selection rebinds by exact variant key not name or position");
        Check(catalog.Contains("Rectb=new(0,y,viewport.width-52,row-4);")&&catalog.Contains("Button(b,GUIContent.none")&&catalog.Contains("HoverOffset(b,hovered,")&&catalog.Contains("SpritePreview(HoverChildRect(b,")&&catalog.Contains("HoverText(HoverChildRect(b,"),"static wiring catalog face icon label share hover while original row hitbox stays fixed");
        Check(ui.Contains("new[]{\"Все\",\"Лучистые\"}")&&ui.Contains("e.Family==family")&&ui.Contains("_filterGeneration!=_app.Actors.Generation"),"static wiring radiant family filter and catalog cache generation retained");
        string draw=Section(ui,"privatevoidDrawSprite(","privatevoidCatalog(");
        Check(draw.Contains("UiThemePolicy.TryFitSprite(")&&draw.Contains("GUI.DrawTexture(")&&!draw.Contains("GUI.DrawTextureWithTexCoords("),"static wiring UI3 contain fit and safe clipped DrawTexture retained");
        string button=Section(ui,"privateboolButton(Rectrect,GUIContentcontent", "privatefloatHoverOffset(");
        Check(button.Contains("returnPressed(rect);")&&button.Contains("HoverChildRect(visual,"),"static wiring button visuals transform separately from unchanged pointer rectangle");
    }
}
