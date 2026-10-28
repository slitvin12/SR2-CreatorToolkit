using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CreatorToolkit;

// Explicit visual previews, NOT hidden native Rad/Mosaic implementations.
// The registered actor remains a valid native Pink model: saves never contain unknown IDs.
internal sealed class PrototypeTools
{
    private readonly List<UnityEngine.Object> _assets=new();
    internal int Applied {get;private set;}
    internal int Faceted {get;private set;}
    private static readonly Color[] Glass={new(.18f,.78f,.88f),new(.70f,.38f,.88f),new(.96f,.66f,.28f),new(.35f,.77f,.57f),new(.89f,.42f,.61f),new(.43f,.57f,.90f)};
    private Material Colored(Material source,Color color,string suffix)
    {
        var material=new Material(source);material.name="CreatorToolkit_Prototype_"+suffix;_assets.Add(material);
        void Set(string name,Color value){if(material.HasProperty(name))material.SetColor(name,value);}
        Set("_TopColor",Color.Lerp(color,Color.white,.35f));Set("_MiddleColor",color);Set("_BottomColor",color*.58f);
        Set("_Color",color);Set("_BaseColor",color);Set("_EmissionColor",color*.2f);
        if(material.HasProperty("_Gloss"))material.SetFloat("_Gloss",.8f);
        if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.8f);
        return material;
    }
    internal void Apply(GameObject go,string kind)
    {
        if(string.IsNullOrEmpty(kind))return;
        int changed=0;
        foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            var originals=renderer.sharedMaterials;if(originals==null)continue;
            bool body=false;Material bodySource=null;
            for(int i=0;i<originals.Length;i++)
                if(originals[i]!=null&&originals[i].HasProperty("_TopColor")&&originals[i].HasProperty("_MiddleColor")){body=true;bodySource=originals[i];break;}
            if(!body)continue;
            var materials=new Il2CppReferenceArray<Material>(originals.Length);
            for(int i=0;i<originals.Length;i++)
                materials[i]=originals[i]!=null&&originals[i].HasProperty("_TopColor")?Colored(originals[i],kind=="rad"?new Color(.24f,.88f,.12f):Glass[i%Glass.Length],kind):originals[i];
            renderer.sharedMaterials=materials;
            renderer.SetPropertyBlock(null);
            if(kind=="mosaic")
            {
                var skin=renderer.TryCast<SkinnedMeshRenderer>();var filter=renderer.GetComponent<MeshFilter>();
                Mesh original=skin!=null?skin.sharedMesh:filter?.sharedMesh;
                if(original!=null&&original.subMeshCount==1)
                {
                    Mesh mesh;bool baked=false;
                    if(original.isReadable)mesh=UnityEngine.Object.Instantiate(original);
                    else if(skin!=null){mesh=new Mesh();skin.BakeMesh(mesh);baked=true;}
                    else continue;
                    mesh.name="CreatorToolkit_MosaicPreviewMesh";_assets.Add(mesh);
                    var triangles=mesh.triangles;var groups=new List<int>[Glass.Length];
                    for(int i=0;i<groups.Length;i++)groups[i]=new List<int>();
                    for(int i=0;i+2<triangles.Length;i+=3)
                    {
                        int g=(i/3*17+i/21)%groups.Length;
                        groups[g].Add(triangles[i]);groups[g].Add(triangles[i+1]);groups[g].Add(triangles[i+2]);
                    }
                    mesh.subMeshCount=groups.Length;
                    var palette=new Il2CppReferenceArray<Material>(groups.Length);
                    for(int i=0;i<groups.Length;i++){mesh.SetTriangles(new Il2CppStructArray<int>(groups[i].ToArray()),i);palette[i]=Colored(bodySource,Glass[i],"mosaic_"+i);}
                    if(baked)
                    {
                        var visual=new GameObject("CreatorToolkit_MosaicFacets");visual.transform.SetParent(renderer.transform,false);
                        visual.AddComponent<MeshFilter>().sharedMesh=mesh;
                        visual.AddComponent<MeshRenderer>().sharedMaterials=palette;
                        renderer.enabled=false;
                    }
                    else
                    {
                        if(skin!=null)skin.sharedMesh=mesh;else filter.sharedMesh=mesh;
                        renderer.sharedMaterials=palette;
                    }
                    Faceted++;
                }
            }
            changed++;
        }
        if(changed==0)throw new InvalidOperationException("Не найден материал тела для визуального прототипа.");
        // Prevent a later native appearance callback from replacing our private materials.
        foreach(var appearance in go.GetComponentsInChildren<Il2Cpp.SlimeAppearanceApplicator>(true))appearance.enabled=false;
        go.name="CreatorToolkit_"+kind+"_VISUAL_PROTOTYPE_PinkBase";
        Applied++;
    }
    internal void Clear()
    {
        foreach(var asset in _assets)if(asset!=null)UnityEngine.Object.Destroy(asset);
        _assets.Clear();Applied=Faceted=0;
    }
}
