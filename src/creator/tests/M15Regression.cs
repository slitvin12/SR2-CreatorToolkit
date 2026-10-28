using System;
using System.IO;
using CreatorToolkit;

internal static class M15Regression
{
    internal static void Run(Action<bool,string> report,string root)
    {
        void Check(bool v,string name)=>report(v,"m15 / "+name);
        Check(NativeIconPolicy.SameWeatherMetadata(new IntPtr(17),new IntPtr(17)),"zone patterns with identical native metadata share the same icon owner");
        Check(!NativeIconPolicy.SameWeatherMetadata(new IntPtr(17),new IntPtr(18)),"different metadata pointers never share weather art by name");
        Check(!NativeIconPolicy.SameWeatherMetadata(IntPtr.Zero,IntPtr.Zero),"two absent weather owners are not an identity match");
        var motion=new UiThemePolicy.SliderAnimation();
        UiThemePolicy.StepSlider(ref motion,.25f,false,false,0);
        Check(motion.Initialized&&motion.Value==.25f,"slider starts at current value without jumping from zero");
        UiThemePolicy.StepSlider(ref motion,1,true,true,0);
        Check(motion.Value==.25f,"zero elapsed event cannot advance drag animation twice");
        float before=motion.Value;
        UiThemePolicy.StepSlider(ref motion,1,true,true,.016f);
        Check(motion.Value>before&&motion.Value<1,"drag moves smoothly and monotonically toward pointer target");
        before=motion.Value;
        UiThemePolicy.StepSlider(ref motion,0,true,true,.016f);
        Check(motion.Value<before&&motion.Value>0,"rapid drag reversal is continuous without overshoot");
        var a=new UiThemePolicy.SliderAnimation();
        var b=new UiThemePolicy.SliderAnimation();
        UiThemePolicy.StepSlider(ref a,0,false,false,0);b=a;
        for(int i=0;i<10;i++)UiThemePolicy.StepSlider(ref a,1,false,false,.01f);
        UiThemePolicy.StepSlider(ref b,1,false,false,.1f);
        Check(Math.Abs(a.Value-b.Value)<.00001f,"drag smoothing is elapsed-time based rather than repaint-count based");
        for(int i=0;i<100;i++)UiThemePolicy.StepSlider(ref a,1,false,false,.016f);
        Check(a.Value==1,"drag settles at exact endpoint without infinite tiny updates");
        Check(UiThemePolicy.SliderPointerValue(10,10,200)==0&&UiThemePolicy.SliderPointerValue(210,10,200)==1,"pointer mapping reaches both exact endpoints");
        Check(UiThemePolicy.SliderPointerValue(110,10,200)==.5f,"pointer mapping matches visible rail midpoint");
        Check(UiThemePolicy.SliderPointerValue(-100,10,200)==0&&UiThemePolicy.SliderPointerValue(1000,10,200)==1,"drag pointer clamps outside rail without invalid values");
        Check(UiThemePolicy.SliderPointerValue(float.NaN,10,200)==0&&UiThemePolicy.SliderPointerValue(20,10,0)==0,"invalid pointer and empty rail stay finite");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f})
        {
            before=motion.Value;
            UiThemePolicy.StepSlider(ref motion,1,false,false,invalid);
            Check(motion.Value==before,"invalid clock does not advance slider: "+invalid);
        }
        Check(UiThemePolicy.SliderCanContinue(true,true,true,true),"enabled focused held clipped drag remains active");
        Check(!UiThemePolicy.SliderCanContinue(false,true,true,true),"disabled slider releases drag");
        Check(!UiThemePolicy.SliderCanContinue(true,false,true,true),"focus loss releases drag without reading pointer target");
        Check(!UiThemePolicy.SliderCanContinue(true,true,false,true),"button release ends drag");
        Check(!UiThemePolicy.SliderCanContinue(true,true,true,false),"ancestor clip exit ends drag");
        for(int i=0;i<1000;i++)UiThemePolicy.StepSlider(ref motion,.5f,true,true,.016f);
        long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)UiThemePolicy.StepSlider(ref motion,(i%101)/100f,(i&1)==0,(i&3)==0,.016f);
        bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
        Check(bytes==0,"10000 warmed pure slider animation updates allocate zero managed bytes");
        string probe=File.ReadAllText(Path.Combine(root,"src","creator","M15SliderProbe.cs"));
        Check(probe.Contains("_stage==6")&&probe.Contains("moving drag with changing cached value labels"),"native UI allocation probe exercises continuous pointer movement, not only stationary hold");
        Check(probe.Contains("ui.DrawAllocatedBytesForTest==_drawBytes")&&probe.Contains("ui.PlayerPageAllocatedBytesForTest==_pageBytes"),"live zero-allocation assertions measure complete Player page and full toolkit Draw scopes");
        Check(probe.Contains("stationary final pointer has been consumed before release"),"native release probe drains queued moving samples before asserting stable committed value");
        string ui=File.ReadAllText(Path.Combine(root,"src","creator","ToolkitUi.cs"));
        Check(ui.Contains("_fillWhite??=Texture2D.whiteTexture")&&ui.Contains("_emptyContent??=GUIContent.none"),"builtin GUI native wrappers are strongly cached instead of recreated after managed GC");
        Check(ui.Contains("NativePaintCacheBudget=1024")&&ui.Contains("_nativePaintTextures.Clear()"),"native paint texture wrappers have a bounded session-invalidated cache");
        Check(probe.Contains("GC.Collect();GC.WaitForPendingFinalizers();"),"live zero-allocation probe tests retained GUI roots after managed collection outside OnGUI");
        string slider=ui.Split("private float Slider(int id,")[1].Split("internal int QuantityForTest")[0];
        Check(slider.Contains("ref var motion=ref _sliderMotion[id]")&&slider.Contains("motion.Frame!=Time.frameCount"),"production slider uses fixed struct slots and a once-per-frame animation gate");
        Check(slider.Contains("PointerWithinGroups()")&&slider.Contains("SliderCanContinue")&&slider.Contains("SliderPointerValue"),"production drag uses tested focus clip and pointer policies");
        Check(!slider.Contains("new string")&&!slider.Contains("new List")&&!slider.Contains("=>")&&!slider.Contains("ToString"),"slider adapter has no collection string formatting or closure hot-path work");
        string player=ui.Split("private void PlayerPage(Rect area)")[1].Split("private Vector2 BeginScroll")[0];
        Check(!player.Contains("ToString(")&&!player.Contains("float[]")&&!player.Contains("=>"),"player page no longer creates speed arrays formatted values or captured closures per event");
        Check(!player.Contains("st.GetCurrHealth")&&!player.Contains("st.GetCurrEnergy"),"player bars consume update-thread scalar snapshots, not ephemeral native state wrappers");
        Check(player.Contains("SpeedReadouts[")&&player.Contains("JumpReadouts[")&&player.Contains("_healthReadout"),"player UI reuses precomputed value labels and update-thread stats labels");
    }
}
