using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor.ParticlePrefabCollector
{
    /// <summary>
    /// 用于粒子Prefab批量预览的临时场景管理工具。
    /// </summary>
    public static class ParticlePrefabPreviewSceneHelper
    {
        private static Scene _previewScene;
        private static readonly List<GameObject> SpawnedPrefabs = new();
        private static bool _sceneGuiRegistered;
        private static GUIStyle _labelStyle;
        private static Texture2D _labelBackgroundTexture;
        private const int PrefabsPerRow = 10;
        private const float PreviewSpacing = 10f;
        private const float BoundarySize = 10f;

        public static void OpenPreviewScene()
        {
            if (_previewScene.IsValid() && _previewScene.isLoaded) return;
            _previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(_previewScene);
            EnsureSceneGuiHook();
        }

        public static void ClosePreviewScene()
        {
            if (_previewScene.IsValid() && _previewScene.isLoaded)
            {
                EditorSceneManager.CloseScene(_previewScene, true);
                _previewScene = default;
            }

            SpawnedPrefabs.Clear();
            RemoveSceneGuiHook();
        }

        private static void ClearPreviewObjects()
        {
            foreach (var go in SpawnedPrefabs)
            {
                if (go) Object.DestroyImmediate(go);
            }

            SpawnedPrefabs.Clear();
        }

        public static void SpawnPrefabs(List<GameObject> prefabs)
        {
            ClearPreviewObjects();
            if (!_previewScene.IsValid() || !_previewScene.isLoaded) return;
            int count = prefabs.Count;
            const int perRow = PrefabsPerRow;
            float spacing = PreviewSpacing;
            for (int i = 0; i < count; i++)
            {
                var prefab = prefabs[i];
                if (!prefab) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _previewScene);
                int row = i / perRow;
                int col = i % perRow;
                go.transform.position = new Vector3(col * spacing, 0, -row * spacing);
                SpawnedPrefabs.Add(go);
            }

            // 自动聚焦到所有对象中心
            if (SpawnedPrefabs.Count > 0)
            {
                // 计算排布宽度和深度
                int rowCount = (count + perRow - 1) / perRow;
                int colCount = count > perRow ? perRow : count;
                float centerX = (colCount - 1) * spacing / 2f;
                float centerZ = -(rowCount - 1) * spacing / 2f;
                var center = new Vector3(centerX, 0, centerZ);
                var view = SceneView.lastActiveSceneView;
                if (view)
                {
                    view.pivot = center;
                    var bounds = new Bounds(SpawnedPrefabs[0].transform.position, Vector3.one);
                    for (int i = 1; i < SpawnedPrefabs.Count; i++)
                    {
                        bounds.Encapsulate(SpawnedPrefabs[i].transform.position);
                    }

                    bounds.Expand(new Vector3(PreviewSpacing, PreviewSpacing, PreviewSpacing));
                    view.Frame(bounds, false);
                    view.Repaint();
                }
            }

            EnsureSceneGuiHook();
        }

        public static void PlayAllParticles()
        {
            foreach (var go in SpawnedPrefabs)
            {
                if (!go) continue;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }
            }
        }

        public static List<GameObject> GetSpawnedPrefabs()
        {
            return SpawnedPrefabs;
        }

        private static void EnsureSceneGuiHook()
        {
            if (_sceneGuiRegistered)
            {
                return;
            }

            SceneView.duringSceneGui += OnSceneGui;
            _sceneGuiRegistered = true;
        }

        private static void RemoveSceneGuiHook()
        {
            if (!_sceneGuiRegistered)
            {
                return;
            }

            SceneView.duringSceneGui -= OnSceneGui;
            _sceneGuiRegistered = false;
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (!_previewScene.IsValid() || !_previewScene.isLoaded)
            {
                return;
            }

            if (SpawnedPrefabs.Count == 0)
            {
                return;
            }

            var previousColor = Handles.color;
            var previousZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            foreach (var go in SpawnedPrefabs.Where(g => g))
            {
                DrawPreviewElements(go);
            }

            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        private static void DrawPreviewElements(GameObject go)
        {
            DrawBoundary(go);
            DrawLabel(go);
        }

        private static void DrawBoundary(GameObject go)
        {
            var center = go.transform.position;
            const float halfSize = BoundarySize * 0.5f;
            var corners = new[]
            {
                center + new Vector3(-halfSize, 0f, -halfSize),
                center + new Vector3(halfSize, 0f, -halfSize),
                center + new Vector3(halfSize, 0f, halfSize),
                center + new Vector3(-halfSize, 0f, halfSize)
            };
            var fillColor = new Color(0f, 0.75f, 1f, 0.05f);
            var outlineColor = new Color(0f, 0.75f, 1f, 0.6f);
            Handles.DrawSolidRectangleWithOutline(corners, fillColor, outlineColor);
        }

        private static void DrawLabel(GameObject go)
        {
            var forward = go.transform.forward;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            var basePosition = go.transform.position;
            var groundY = basePosition.y;
            var groundPosition = new Vector3(basePosition.x, groundY, basePosition.z);
            var labelOffset = Vector3.back * 0.25f * PreviewSpacing + Vector3.up * 0.25f;
            var labelPosition = groundPosition + labelOffset;

            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            Handles.DrawLine(groundPosition, labelPosition);
            Handles.Label(labelPosition, go.name, GetLabelStyle());
        }

        private static GUIStyle GetLabelStyle()
        {
            if (_labelStyle != null)
            {
                return _labelStyle;
            }

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 8,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 2, 2),
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.9f, 1f) : new Color(0.1f, 0.2f, 0.6f),
                    background = GetLabelBackgroundTexture()
                }
            };

            _labelStyle = style;
            return _labelStyle;
        }

        private static Texture2D GetLabelBackgroundTexture()
        {
            if (_labelBackgroundTexture)
            {
                return _labelBackgroundTexture;
            }

            var tex = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "ParticlePreviewLabelBg"
            };
            var color = EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.6f) : new Color(1f, 1f, 1f, 0.7f);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            _labelBackgroundTexture = tex;
            return _labelBackgroundTexture;
        }
    }
}