using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.HDPipeline;
using UnityEditor.IMGUI.Controls;
using UnityEditor.ShortcutManagement;
using static UnityEditorInternal.EditMode;
using System.Collections.Generic;
using System.Linq.Expressions;
using System;

namespace UnityEditor.Experimental.Rendering.HDPipeline
{
    [CustomEditor(typeof(DecalProjectorComponent))]
    [CanEditMultipleObjects]
    public partial class DecalProjectorComponentEditor : Editor
    {
        MaterialEditor m_MaterialEditor = null;
        SerializedProperty m_MaterialProperty;
        SerializedProperty m_DrawDistanceProperty;
        SerializedProperty m_FadeScaleProperty;
        SerializedProperty m_UVScaleProperty;
        SerializedProperty m_UVBiasProperty;
        SerializedProperty m_AffectsTransparencyProperty;
        SerializedProperty m_Size;
        SerializedProperty m_FadeFactor;
        
        int layerMask => (target as Component).gameObject.layer;
        bool layerMaskHasMultipleValue
        {
            get
            {
                if (targets.Length < 2)
                    return false;
                int layerMask = (targets[0] as Component).gameObject.layer;
                for (int index = 0; index < targets.Length; ++index)
                {
                    if ((targets[index] as Component).gameObject.layer != layerMask)
                        return true;
                }
                return false;
            }
        }

        bool showAffectTransparency => DecalSystem.IsHDRenderPipelineDecal((target as DecalProjectorComponent).m_Material.shader.name);

        bool showAffectTransparencyHaveMultipleDifferentValue
        {
            get
            {
                if (targets.Length < 2)
                    return false;
                bool show = DecalSystem.IsHDRenderPipelineDecal((targets[0] as DecalProjectorComponent).m_Material.shader.name);
                for (int index = 0; index < targets.Length; ++index)
                {
                    if (DecalSystem.IsHDRenderPipelineDecal((targets[0] as DecalProjectorComponent).m_Material.shader.name) ^ show)
                        return true;
                }
                return false;
            }
        }

        static HierarchicalBox s_Handle;
        static HierarchicalBox handle
        {
            get
            {
                if (s_Handle == null || s_Handle.Equals(null))
                {
                    s_Handle = new HierarchicalBox(k_GizmoColorBase, k_BaseHandlesColor);
                    s_Handle.monoHandle = false;
                }
                return s_Handle;
            }
        }

    const SceneViewEditMode k_EditShapeWithoutPreservingUV = (SceneViewEditMode)90;
        const SceneViewEditMode k_EditShapePreservingUV = (SceneViewEditMode)91;
        const SceneViewEditMode k_EditUV = (SceneViewEditMode)92;
        static readonly SceneViewEditMode[] k_EditVolumeModes = new SceneViewEditMode[]
        {
            k_EditShapeWithoutPreservingUV,
            k_EditShapePreservingUV
        };
        static readonly SceneViewEditMode[] k_EditPivotModes = new SceneViewEditMode[]
        {
            k_EditUV
        };
        static SceneViewEditMode s_CurrentEditMode;
        static bool s_ModeSwitched;

        static GUIContent[] k_EditVolumeLabels = null;
        static GUIContent[] editVolumeLabels => k_EditVolumeLabels ?? (k_EditVolumeLabels = new GUIContent[]
        {
            EditorGUIUtility.TrIconContent("d_ScaleTool", k_EditShapeWithoutPreservingUVTooltip),
            EditorGUIUtility.TrIconContent("d_RectTool", k_EditShapePreservingUVTooltip)
        });
        static GUIContent[] k_EditPivotLabels = null;
        static GUIContent[] editPivotLabels => k_EditPivotLabels ?? (k_EditPivotLabels = new GUIContent[]
        {
            EditorGUIUtility.TrIconContent("d_MoveTool", k_EditUVTooltip)
        });

        static Editor s_Owner;
                
        private void OnEnable()
        {
            s_Owner = this;
            
            // Create an instance of the MaterialEditor
            UpdateMaterialEditor();
            foreach (var decalProjector in targets)
            {
                (decalProjector as DecalProjectorComponent).OnMaterialChange += UpdateMaterialEditor;
            }

            // Fetch serialized properties
            m_MaterialProperty = serializedObject.FindProperty("m_Material");
            m_DrawDistanceProperty = serializedObject.FindProperty("m_DrawDistance");
            m_FadeScaleProperty = serializedObject.FindProperty("m_FadeScale");
            m_UVScaleProperty = serializedObject.FindProperty("m_UVScale");
            m_UVBiasProperty = serializedObject.FindProperty("m_UVBias");
            m_AffectsTransparencyProperty = serializedObject.FindProperty("m_AffectsTransparency");
            m_Size = serializedObject.FindProperty("m_Size");
            m_FadeFactor = serializedObject.FindProperty("m_FadeFactor");
        }
        
        private void OnDisable()
        {
            foreach (var decalProjector in targets)
            {
                (decalProjector as DecalProjectorComponent).OnMaterialChange -= UpdateMaterialEditor;
            }
            s_Owner = null;
        }

        private void OnDestroy() =>
            DestroyImmediate(m_MaterialEditor);

        public void UpdateMaterialEditor()
        {
            // Update material editor with the new material
            UnityEngine.Object[] materials = new UnityEngine.Object[targets.Length];
            for (int index = 0; index < targets.Length; ++index)
            {
                materials[index] = (targets[index] as DecalProjectorComponent).m_Material;
            }
            m_MaterialEditor = (MaterialEditor)CreateEditor(materials);
        }

        void OnSceneGUI()
        {
            //called on each targets
            DrawHandles();
        }

        void DrawHandles()
        {
            //Note: each target need to be handled individually to allow multi edition
            DecalProjectorComponent decalProjector = target as DecalProjectorComponent;

            if (editMode == k_EditShapePreservingUV || editMode == k_EditShapeWithoutPreservingUV)
            {
                using (new Handles.DrawingScope(Color.white, Matrix4x4.TRS(decalProjector.transform.position, decalProjector.transform.rotation, Vector3.one)))
                {
                    handle.center = decalProjector.m_Offset;
                    handle.size = decalProjector.m_Size;

                    Vector3 boundsSizePreviousOS = handle.size;
                    Vector3 boundsMinPreviousOS = handle.size * -0.5f + handle.center;

                    EditorGUI.BeginChangeCheck();
                    handle.DrawHandle();
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Adjust decal transform if handle changed.
                        Undo.RecordObject(decalProjector, "Decal Projector Change");

                        decalProjector.m_Size = handle.size;
                        decalProjector.m_Offset = handle.center;

                        Vector3 boundsSizeCurrentOS = handle.size;
                        Vector3 boundsMinCurrentOS = handle.size * -0.5f + handle.center;

                        if (editMode == k_EditShapePreservingUV)
                        {
                            // Treat decal projector bounds as a crop tool, rather than a scale tool.
                            // Compute a new uv scale and bias terms to pin decal projection pixels in world space, irrespective of projector bounds.
                            decalProjector.m_UVScale.x *= Mathf.Max(1e-5f, boundsSizeCurrentOS.x) / Mathf.Max(1e-5f, boundsSizePreviousOS.x);
                            decalProjector.m_UVScale.y *= Mathf.Max(1e-5f, boundsSizeCurrentOS.y) / Mathf.Max(1e-5f, boundsSizePreviousOS.y);

                            decalProjector.m_UVBias.x += (boundsMinCurrentOS.x - boundsMinPreviousOS.x) / Mathf.Max(1e-5f, boundsSizeCurrentOS.x) * decalProjector.m_UVScale.x;
                            decalProjector.m_UVBias.y += (boundsMinCurrentOS.y - boundsMinPreviousOS.y) / Mathf.Max(1e-5f, boundsSizeCurrentOS.y) * decalProjector.m_UVScale.y;
                        }

                        if (PrefabUtility.IsPartOfNonAssetPrefabInstance(decalProjector))
                        {
                            PrefabUtility.RecordPrefabInstancePropertyModifications(decalProjector);
                        }
                    }

                    // Automatically recenter our transform component if necessary.
                    // In order to correctly handle world-space snapping, we only perform this recentering when the user is no longer interacting with the gizmo.
                    if ((GUIUtility.hotControl == 0) && (decalProjector.m_Offset != Vector3.zero))
                    {
                        // Both the DecalProjectorComponent, and the transform will be modified.
                        // The undo system will automatically group all RecordObject() calls here into a single action.
                        Undo.RecordObject(decalProjector, "Decal Projector Change");

                        // Re-center the transform to the center of the decal projector bounds,
                        // while maintaining the world-space coordinates of the decal projector boundings vertices.
                        decalProjector.transform.Translate(decalProjector.m_Offset, Space.Self);

                        decalProjector.m_Offset = Vector3.zero;
                        if (PrefabUtility.IsPartOfNonAssetPrefabInstance(decalProjector))
                        {
                            PrefabUtility.RecordPrefabInstancePropertyModifications(decalProjector);
                        }
                    }
                }
            }

            //[TODO: add editable pivot. Uncomment this when ready]
            //else if (editMode == k_EditUV)
            //{
            //    //here should be handles code to manipulate the pivot without changing the UV
            //}
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void UpdateProjectedResult(DecalProjectorComponent decalProjector, GizmoType gizmoType)
        {
            // Smoothly update the decal image projected
            // Note: due to change projection from -Y to Z and inside Y/Z manipulation
            // the only way to respect former behavior is to separate z axis from the other.
            // Z mapping of offset should not be used anymore or must be fixed.
            Vector3 offsetWithoutZ = decalProjector.offset;
            offsetWithoutZ.y = 0;
            Matrix4x4 sizeOffset = Matrix4x4.Translate(offsetWithoutZ) * Matrix4x4.Scale(decalProjector.size);
            DecalSystem.instance.UpdateCachedData(
                decalProjector.position + decalProjector.transform.forward * decalProjector.m_Offset.z,
                decalProjector.rotation,
                sizeOffset,
                decalProjector.m_DrawDistance,
                decalProjector.m_FadeScale,
                decalProjector.uvScaleBias,
                decalProjector.m_AffectsTransparency,
                decalProjector.Handle,
                decalProjector.gameObject.layer,
                decalProjector.m_FadeFactor);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void DrawGizmosSelected(DecalProjectorComponent decalProjector, GizmoType gizmoType)
        {
            //draw them scale independent
            using (new Handles.DrawingScope(Color.white, Matrix4x4.TRS(decalProjector.transform.position, decalProjector.transform.rotation, Vector3.one)))
            {
                handle.center = decalProjector.m_Offset;
                handle.size = decalProjector.m_Size;
                handle.DrawHull(editMode == k_EditShapePreservingUV || editMode == k_EditShapeWithoutPreservingUV);

                int controlID = GUIUtility.GetControlID(handle.GetHashCode(), FocusType.Passive);
                Quaternion arrowRotation = Quaternion.LookRotation(Vector3.down, Vector3.right);
                float arrowSize = decalProjector.m_Size.z * 0.25f;
                Vector3 pivot = decalProjector.m_Offset;
                Vector3 projectedPivot = pivot + decalProjector.m_Size.z * 0.5f * Vector3.back;
                Handles.ArrowHandleCap(controlID, projectedPivot, Quaternion.identity, arrowSize, EventType.Repaint);

                //[TODO: add editable pivot. Uncomment this when ready]
                //draw pivot
                //Handles.SphereHandleCap(controlID, pivot, Quaternion.identity, 0.02f, EventType.Repaint);
                //Color c = Color.white;
                //c.a = 0.2f;
                //Handles.color = c;
                //Handles.DrawLine(projectedPivot, projectedPivot + decalProjector.m_Size.x * 0.5f * Vector3.right);
                //Handles.DrawLine(projectedPivot, projectedPivot + decalProjector.m_Size.y * 0.5f * Vector3.up);
                //Handles.DrawLine(projectedPivot, projectedPivot + decalProjector.m_Size.z * 0.5f * Vector3.forward);

                //draw UV
                Color face = Color.green;
                face.a = 0.1f;
                Vector2 size = new Vector2(
                    (decalProjector.m_UVScale.x > 100000 || decalProjector.m_UVScale.x < -100000 ? 0f : 1f / decalProjector.m_UVScale.x) * decalProjector.m_Size.x,
                    (decalProjector.m_UVScale.x > 100000 || decalProjector.m_UVScale.x < -100000 ? 0f : 1f / decalProjector.m_UVScale.y) * decalProjector.m_Size.y
                    );
                Vector2 start = (Vector2)projectedPivot - new Vector2(decalProjector.m_UVBias.x * size.x, decalProjector.m_UVBias.y * size.y);
                using (new Handles.DrawingScope(face, Matrix4x4.TRS(decalProjector.transform.position - decalProjector.transform.rotation * (decalProjector.m_Size * 0.5f + decalProjector.m_Offset.z * Vector3.back), decalProjector.transform.rotation, Vector3.one)))
                {
                    Handles.DrawSolidRectangleWithOutline(new Rect(start, size), face, Color.white);
                }
            }
        }

        Bounds GetBoundsGetter()
        {
            var bounds = new Bounds();
            var decalTransform = ((Component)target).transform;
            bounds.Encapsulate(decalTransform.position);
            return bounds;
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DoInspectorToolbar(k_EditVolumeModes, editVolumeLabels, GetBoundsGetter, this);

            //[TODO: add editable pivot. Uncomment this when ready]
            //DoInspectorToolbar(k_EditPivotModes, editPivotLabels, GetBoundsGetter, this);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(m_Size, k_SizeContent);
            EditorGUILayout.PropertyField(m_MaterialProperty, k_MaterialContent);
            EditorGUILayout.PropertyField(m_DrawDistanceProperty, k_DistanceContent);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = m_FadeScaleProperty.hasMultipleDifferentValues;
            float fadeDistancePercent = m_FadeScaleProperty.floatValue * 100f;
            fadeDistancePercent = EditorGUILayout.Slider(k_FadeScaleContent, fadeDistancePercent, 0f, 100f);
            if (EditorGUI.EndChangeCheck())
                m_FadeScaleProperty.floatValue = fadeDistancePercent * 0.01f;
            EditorGUI.showMixedValue = false;

            EditorGUILayout.PropertyField(m_UVScaleProperty, k_UVScaleContent);
            EditorGUILayout.PropertyField(m_UVBiasProperty, k_UVBiasContent);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = m_FadeFactor.hasMultipleDifferentValues;
            float fadePercent = m_FadeFactor.floatValue * 100f;
            fadePercent = EditorGUILayout.Slider(k_FadeFactorContent, fadePercent, 0f, 100f);
            if (EditorGUI.EndChangeCheck())
                m_FadeFactor.floatValue = fadePercent * 0.01f;
            EditorGUI.showMixedValue = false;

            // only display the affects transparent property if material is HDRP/decal
            if (showAffectTransparencyHaveMultipleDifferentValue)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.LabelField(EditorGUIUtility.TrTextContent("Multiple material type in selection"));
            }
            else if (showAffectTransparency)
                EditorGUILayout.PropertyField(m_AffectsTransparencyProperty, k_AffectTransparentContent);

            if (EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();

            if (layerMaskHasMultipleValue || layerMask != (target as Component).gameObject.layer)
            {
                foreach (var decalProjector in targets)
                {
                    (decalProjector as DecalProjectorComponent).OnValidate();
                }
            }

            if (m_MaterialEditor != null)
            {
                // Draw the material's foldout and the material shader field
                // Required to call m_MaterialEditor.OnInspectorGUI ();
                m_MaterialEditor.DrawHeader();

                // We need to prevent the user to edit default decal materials
                bool isDefaultMaterial = false;
                var hdrp = GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset;
                if (hdrp != null)
                {
                    foreach(var decalProjector in targets)
                    {
                        isDefaultMaterial |= (decalProjector as DecalProjectorComponent).m_Material == hdrp.GetDefaultDecalMaterial();
                    }
                }
                using (new EditorGUI.DisabledGroupScope(isDefaultMaterial))
                {
                    // Draw the material properties
                    // Works only if the foldout of m_MaterialEditor.DrawHeader () is open
                    m_MaterialEditor.OnInspectorGUI();
                }
            }
        }

        [Shortcut("HDRP/Decal: Handle changing size stretching UV", typeof(SceneView), KeyCode.Keypad1, ShortcutModifiers.Action)]
        static void EnterEditModeWithoutPreservingUV(ShortcutArguments args) =>
            ChangeEditMode(k_EditShapeWithoutPreservingUV, (s_Owner as DecalProjectorComponentEditor).GetBoundsGetter(), s_Owner);

        [Shortcut("HDRP/Decal: Handle changing size cropping UV", typeof(SceneView), KeyCode.Keypad2, ShortcutModifiers.Action)]
        static void EnterEditModePreservingUV(ShortcutArguments args) =>
            ChangeEditMode(k_EditShapePreservingUV, (s_Owner as DecalProjectorComponentEditor).GetBoundsGetter(), s_Owner);

        //[TODO: add editable pivot. Uncomment this when ready]
        //[Shortcut("HDRP/Decal: Handle changing pivot position while preserving UV position", typeof(SceneView), KeyCode.Keypad3, ShortcutModifiers.Action)]
        //static void EnterEditModePivotPreservingUV(ShortcutArguments args) =>
        //    ChangeEditMode(k_EditUV, (s_Owner as DecalProjectorComponentEditor).GetBoundsGetter(), s_Owner);

        [Shortcut("HDRP/Decal: Handle swap between cropping and stretching UV", typeof(SceneView), KeyCode.W, ShortcutModifiers.Action)]
        static void SwappingEditUVMode(ShortcutArguments args)
        {
            SceneViewEditMode targetMode = SceneViewEditMode.None;
            switch (editMode)
            {
                case k_EditShapePreservingUV:
                    targetMode = k_EditShapeWithoutPreservingUV;
                    break;
                case k_EditShapeWithoutPreservingUV:
                    targetMode = k_EditShapePreservingUV;
                    break;
            }
            if (targetMode != SceneViewEditMode.None)
                ChangeEditMode(targetMode, (s_Owner as DecalProjectorComponentEditor).GetBoundsGetter(), s_Owner);
        }

        [Shortcut("HDRP/Decal: Stop Editing", typeof(SceneView), KeyCode.Keypad0, ShortcutModifiers.Action)]
        static void ExitEditMode(ShortcutArguments args) => QuitEditMode();
    }
}
