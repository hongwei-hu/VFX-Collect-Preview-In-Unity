using System.Collections.Generic;
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

        public static void OpenPreviewScene()
        {
            if (_previewScene.IsValid() && _previewScene.isLoaded) return;
            _previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(_previewScene);
        }

        public static void ClosePreviewScene()
        {
            if (_previewScene.IsValid() && _previewScene.isLoaded)
            {
                EditorSceneManager.CloseScene(_previewScene, true);
                _previewScene = default;
            }
            SpawnedPrefabs.Clear();
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
            int perRow = 10;
            float spacing = 5f;
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
                SceneView.lastActiveSceneView.pivot = center;
                SceneView.lastActiveSceneView.Repaint();
            }
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
    }
}
