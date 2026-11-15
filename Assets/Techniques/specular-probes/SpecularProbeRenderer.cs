// From zulubo/SpecularProbes (MIT, Copyright (c) 2020 Zach Tsiakalis-Brown), SpecularProbeRenderer.cs.
// Changed for this build: Render() is split so RenderTo(path) can bake several probe versions to chosen files,
// and FindObjectsOfType is replaced by FindObjectsByType (the old call is obsolete in Unity 6). Same steps:
// draw a bright sphere at every SpecularProbeLight in range, bake the probe, remove the spheres.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[ExecuteInEditMode]
[RequireComponent(typeof(ReflectionProbe))]
public class SpecularProbeRenderer : MonoBehaviour
{

#if UNITY_EDITOR
    [Tooltip("Will only draw specular highlights for lights in this radius")]
    public float radius = 100;

    private ReflectionProbe probe;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, 1, 1, 0.25f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }

    private void Start()
    {
        if (Application.isPlaying) Destroy(this);
    }

#if UNITY_2019_2_OR_NEWER
    // Unity added bakeCompleted action in 2019.2
    private void OnEnable()
    {
        UnityEditor.Lightmapping.bakeCompleted += OnBakeCompleted;
#if BAKERY_INCLUDED
        ftRenderLightmap.OnFinishedReflectionProbes += OnFinishedReflectionProbes;
#endif
    }

    private void OnDisable()
    {
        UnityEditor.Lightmapping.bakeCompleted -= OnBakeCompleted;
#if BAKERY_INCLUDED
        ftRenderLightmap.OnFinishedReflectionProbes -= OnFinishedReflectionProbes;
#endif
    }

    void OnBakeCompleted()
    {
        Debug.Log("Baking Specular Highlights");
        Render();
    }

    public void OnFinishedReflectionProbes(object sender, System.EventArgs e)
    {
        OnBakeCompleted();
    }
#endif

    [ContextMenu("Render")]
    public void Render()
    {
        probe = GetComponent<ReflectionProbe>();
        RenderTo(UnityEditor.AssetDatabase.GetAssetPath(probe.bakedTexture));
    }

    public bool RenderTo(string path)
    {
        probe = GetComponent<ReflectionProbe>();

        SpecularProbeLight[] allLights = FindObjectsByType<SpecularProbeLight>(FindObjectsSortMode.None);
        List<SpecularProbeLight> closeLights = new List<SpecularProbeLight>();

        for (int i = 0; i < allLights.Length; i++)
        {
            //find lights within radius of probe
            if ((allLights[i].transform.position - transform.position).sqrMagnitude < radius * radius)
            {
                closeLights.Add(allLights[i]);
                // create specular highlight sphere
                allLights[i].Draw();
            }
        }
        //render probe
        bool ok = UnityEditor.Lightmapping.BakeReflectionProbe(probe, path);

        // remove all created lights, cleaning up scene
        for (int i = 0; i < closeLights.Count; i++)
        {
            closeLights[i].Hide();
        }
        return ok;
    }

    [ContextMenu("Render All")]
    public void RenderAll()
    {
        foreach (SpecularProbeRenderer r in FindObjectsByType<SpecularProbeRenderer>(FindObjectsSortMode.None))
        {
            r.Render();
        }
    }

#endif
}
