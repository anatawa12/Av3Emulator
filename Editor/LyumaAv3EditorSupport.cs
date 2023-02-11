/*
Copyright (c) 2023 anatawa12 and contributors
Copyright (c) 2020-2022 Lyuma <xn.lyuma@gmail.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE. */

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Compilation;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

[InitializeOnLoadAttribute]
public static class LyumaAv3EditorSupport
{
    private static readonly Dictionary<VRCAvatarDescriptor.AnimLayerType, string> AnimLayerToDefaultGuid =
        new Dictionary<VRCAvatarDescriptor.AnimLayerType, string>
        {
            { VRCAvatarDescriptor.AnimLayerType.TPose, "00121b5812372b74f9012473856d8acf" },
            { VRCAvatarDescriptor.AnimLayerType.IKPose, "a9b90a833b3486e4b82834c9d1f7c4ee" },
            { VRCAvatarDescriptor.AnimLayerType.Base, "4e4e1a372a526074884b7311d6fc686b" },
            { VRCAvatarDescriptor.AnimLayerType.Sitting, "1268460c14f873240981bf15aa88b21a" },
            { VRCAvatarDescriptor.AnimLayerType.Additive, "573a1373059632b4d820876efe2d277f" },
            { VRCAvatarDescriptor.AnimLayerType.FX, "d40be620cf6c698439a2f0a5144919fe" },
            { VRCAvatarDescriptor.AnimLayerType.Action, "3e479eeb9db24704a828bffb15406520" },
            { VRCAvatarDescriptor.AnimLayerType.Gesture, "404d228aeae421f4590305bc4cdaba16" },
        };

    private static readonly Dictionary<VRCAvatarDescriptor.AnimLayerType, string> AnimLayerToDefaultMaskGuid =
        new Dictionary<VRCAvatarDescriptor.AnimLayerType, string>
        {
            { VRCAvatarDescriptor.AnimLayerType.TPose, "4311d7fbde38cdb469f4dad32f9b6c0a" }, // LyumaMusclesOnly
            { VRCAvatarDescriptor.AnimLayerType.IKPose, "4311d7fbde38cdb469f4dad32f9b6c0a" }, // LyumaMusclesOnly
            { VRCAvatarDescriptor.AnimLayerType.Base, null }, // LyumaFullMask
            { VRCAvatarDescriptor.AnimLayerType.Sitting, null }, // LyumaFullMask
            { VRCAvatarDescriptor.AnimLayerType.Additive, null }, // LyumaFullMask
            { VRCAvatarDescriptor.AnimLayerType.FX, "7681aa70297d4ba488f0182e6d7814de" }, // LyumaEmptyMask // TODO
            { VRCAvatarDescriptor.AnimLayerType.Action, null }, // vrc_MusclesOnly
            { VRCAvatarDescriptor.AnimLayerType.Gesture, "f7acc5fa2ae16d248ae589aa705c389d" }, // LyumaHandsOnly
        };

    static void InitDefaults() {
        foreach (var kv in AnimLayerToDefaultGuid)
        {
            RuntimeAnimatorController ac = null;
            if (kv.Value != null)
            {
                ac =
                    AssetDatabase.LoadAssetAtPath<AnimatorController>(
                        AssetDatabase.GUIDToAssetPath(kv.Value));
                if (ac == null)
                    Debug.LogWarning("Failed to resolve animator controller for " + kv.Key);
            }
            LyumaAv3Runtime.animLayerToDefaultController[kv.Key] = ac;
        }
        foreach (var kv in AnimLayerToDefaultMaskGuid)
        {
            AvatarMask mask = null;
            if (kv.Value != null)
            {
                mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
                    AssetDatabase.GUIDToAssetPath(kv.Value));

                if (mask == null)
                {
                    Debug.LogWarning("Failed to resolve avatar mask " + kv.Value + " for " + kv.Key);
                    mask = new AvatarMask();
                }
            }
            LyumaAv3Runtime.animLayerToDefaultAvaMask[kv.Key] = mask;
        }

        LyumaAv3Emulator.EmptyController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                AssetDatabase.GUIDToAssetPath("6017b05a9fb50634f99c9632977a33dd"));

        LyumaAv3Runtime.updateSelectionDelegate = (obj) => {
            if (obj == null && LyumaAv3Emulator.emulatorInstance != null) {
                // Debug.Log("Resetting selected object: " + LyumaAv3Emulator.emulatorInstance);
                obj = LyumaAv3Emulator.emulatorInstance.gameObject;
            }
            // Debug.Log("Setting selected object: " + go);
            Selection.SetActiveObjectWithContext(obj, obj);
            // Highlighter.Highlight("Inspector", "Animator To Debug");
        };

        LyumaAv3Runtime.updateSceneLayersDelegate = (layers) => {
            if (Tools.visibleLayers == layers) {
                return;
            }
            // Debug.Log("Setting selected layers: " + layers);
            Tools.visibleLayers = layers;
            Camera[] cameras = new Camera[255];
            Camera.GetAllCameras(cameras);
            foreach (Camera c in cameras) {
                if (c != null && c.targetTexture == null && c.GetComponentInParent<LyumaAv3Runtime>() == null && c.gameObject.activeInHierarchy && c.isActiveAndEnabled) {
                    c.cullingMask = layers;
                }
            }
            // Highlighter.Highlight("Inspector", "Animator To Debug");
        };

        LyumaAv3Runtime.addRuntimeDelegate = (runtime) => {
            MoveComponentToTop(runtime);
        };

        // Currently PhysBone and ContactManager cause exceptions if scripts reload during Play mode.
        // This applies a workaround: disable the objects before compile; call RuntimeInit to recreate them after.
        LyumaAv3Runtime.ApplyOnEnableWorkaroundDelegate = () => {
            CompilationPipeline.assemblyCompilationStarted -= WorkaroundDestroyManagersBeforeCompile;
            CompilationPipeline.assemblyCompilationStarted += WorkaroundDestroyManagersBeforeCompile;
            GameObject gotmp = GameObject.Find("/TempReloadDontDestroy");
            if (gotmp != null) {
                GameObject.DestroyImmediate(gotmp);
                var avatarDynamicsSetup = typeof(VRCExpressionsMenuEditor).Assembly.GetType("VRC.SDK3.Avatars.AvatarDynamicsSetup");
                if (avatarDynamicsSetup != null) {
                    var RuntimeInit = avatarDynamicsSetup.GetMethod("RuntimeInit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (RuntimeInit != null) {
                        Debug.Log("Caling avatarDynamicsSetup.RuntimeInit(): " + RuntimeInit);
                        RuntimeInit.Invoke(null, new object[0]);
                    }
                }
                Debug.Log("DONE workaround");
            }
        };

        LyumaAv3Osc.GetEditorViewportDelegate = () => {
            try {
                Rect ret = UnityEditor.SceneView.currentDrawingSceneView.position;
                // Gizmos are relative to the active window in terms of x and y.
                ret.x = 1.0f;
                ret.y = 1.0f;
                ret.height -= 7.0f;
                return ret;
            } catch {
                Vector2 gvsize = Handles.GetMainGameViewSize();
                return new Rect(0, -18, gvsize.x, gvsize.y);
            }
        };
        LyumaAv3Osc.DrawDebugRectDelegate = (Rect pos, Color col, Color outlineCol) => {
            // Debug.Log("Debug raw rect " + pos);
            Color origColor = GUI.color;
            GUI.color = col;
            UnityEditor.Handles.BeginGUI();
            UnityEditor.Handles.DrawSolidRectangleWithOutline(pos, col, outlineCol);
            UnityEditor.Handles.EndGUI();
            GUI.color = origColor;
        };
        LyumaAv3Osc.DrawDebugTextDelegate = (Rect pos, Color backgroundCol, Color outlineCol, Color textCol, string str, TextAnchor alignment) => {
            // Debug.Log("Debug raw text " + str + " at " + pos);
            Color origColor = GUI.color;
            GUI.color = backgroundCol;
            var view = UnityEditor.SceneView.currentDrawingSceneView;
            // Vector2 size = GUI.skin.label.CalcSize(new GUIContent(str));
            // Rect pos = new Rect(location.x, location.y, size.x, size.y);
            UnityEditor.Handles.BeginGUI();
            UnityEditor.Handles.DrawSolidRectangleWithOutline(pos, backgroundCol, outlineCol);
            GUI.color = textCol.r + textCol.b + textCol.g > 0.5f ? new Color(0,0,0,textCol.a * 0.5f) : new Color(1,1,1,textCol.a * 0.5f);//new Color(1.0f, 1.0f, 1.0f, textCol.a * 0.25f);
            var style = new GUIStyle();
            style.fontStyle = FontStyle.Bold;
            style.alignment = alignment;
            style.normal.textColor = GUI.color;
            pos.y += 1;
            GUI.Label(pos, str, style);
            pos.x += 1;
            GUI.Label(pos, str, style);
            pos.y -= 1;
            GUI.Label(pos, str, style);
            pos.x -= 1;
            GUI.Label(pos, str, style);
            pos.x += 0.5f;
            pos.y += 0.5f;
            GUI.color = textCol;
            style.normal.textColor = GUI.color;
            GUI.Label(pos, str, style);
            UnityEditor.Handles.EndGUI();
            GUI.color = origColor;
        };
    }

    public static void OnPlayModeStateChange(UnityEditor.PlayModeStateChange pmsc) {
        // We don't want any of our callbacks causing trouble outside of play mode.
        if (pmsc != UnityEditor.PlayModeStateChange.EnteredPlayMode) {
            CompilationPipeline.assemblyCompilationStarted -= WorkaroundDestroyManagersBeforeCompile;
        }
    }

    private static void WorkaroundDestroyManagersBeforeCompile(string obj) {
        Debug.Log("Compile Started");
        GameObject gotmp = new GameObject("TempReloadDontDestroy");
        Object.DontDestroyOnLoad(gotmp);
        GameObject go;
        go = GameObject.Find("/TriggerManager");
        if (go != null) {
            Object.DestroyImmediate(go);
        }
        go = GameObject.Find("/PhysBoneManager");
        if (go != null) {
            Object.DestroyImmediate(go);
        }
    }

    static void MoveComponentToTop(Component c) {
        GameObject go = c.gameObject;
        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++) {
            if (components[i].GetType().Name.Contains("PipelineSaver")) {
                return;
            }
        }
        try {
            if (PrefabUtility.IsPartOfAnyPrefab(go)) {
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
        } catch (System.Exception) {}
        int moveUpCalls = components.Length - 2;
        if (!PrefabUtility.IsPartOfAnyPrefab(go.GetComponents<Component>()[1])) {
            for (int i = 0; i < moveUpCalls; i++) {
                UnityEditorInternal.ComponentUtility.MoveComponentUp(c);
            }
        }
    }

    // register an event handler when the class is initialized
    static LyumaAv3EditorSupport()
    {
        InitDefaults();
        EditorApplication.playModeStateChanged += OnPlayModeStateChange;
        LyumaAv3Runtime.InvokeOnPreProcessAvatar = (obj) =>
        {
            Debug.Log("Invoking OnPreprocessAvatar for " + obj, obj);
            VRCBuildPipelineCallbacks.OnPreprocessAvatar(obj);
        };
    }

    [MenuItem("Tools/Enable Avatars 3.0 Emulator")]
    public static void EnableAv3Testing() {
        GameObject go = GameObject.Find("/Avatars 3.0 Emulator Control");
        if (go != null) {
            go.SetActive(true);
        } else {
            go = new GameObject("Avatars 3.0 Emulator Control");
        }
        Selection.SetActiveObjectWithContext(go, go);
        go.GetOrAddComponent<LyumaAv3Emulator>();
        go.GetOrAddComponent<LyumaAv3Osc>();
        EditorGUIUtility.PingObject(go);
    }
}
