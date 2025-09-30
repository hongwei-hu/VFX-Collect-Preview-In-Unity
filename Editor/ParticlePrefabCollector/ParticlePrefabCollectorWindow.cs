using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.ParticlePrefabCollector
{
    /// <summary>
    /// 扫描所有带有粒子系统组件（包括子对象）的Prefab，生成唯一Prefab列表，便于快速查找和预览粒子特效。
    /// </summary>
    public class ParticlePrefabCollectorWindow : EditorWindow
    {
        private class Entry
        {
            public string PrefabPath;
            public string PrefabName;
            public GameObject PrefabAsset;
        }

        private readonly List<Entry> _entries = new();
        private Vector2 _scroll;
        private string _search = "";
        private int _currentPage;
        private int _pageSize = 40;
        private int _totalPages = 1;
        private bool _isScanning;
        private float _scanProgress;
        private List<string> _pendingGuids = new();
        private int _scanCursor;

        private enum SortColumn
        {
            PrefabName,
            Path
        }

        private SortColumn _sortColumn = SortColumn.PrefabName;
        private bool _sortAscending = true;

        private bool _previewMode;
        private bool _isPreviewing;
        private readonly HashSet<string> _previewSelected = new();
        private int _previewPage;
        private const int PreviewPageSize = 100;
        private bool _previewSelectAll;
        private bool _previewSelectPartial;
        private SceneAsset _lastSceneAsset;

        [MenuItem("Tools/Particle/Prefab Collector")]
        public static void Open()
        {
            var w = GetWindow<ParticlePrefabCollectorWindow>("Particle Prefab Collector");
            w.minSize = new Vector2(800, 500);
            w.Focus();
        }

        [System.Serializable]
        public class ParticlePrefabScanResult : ScriptableObject
        {
            public List<string> prefabPaths = new();
            public List<string> scanFolders = new();
            public System.DateTime ScanTime;
        }

        private const string ScanResultAssetPath =
            "Assets/Editor/ParticlePrefabCollector/ParticlePrefabScanResult.asset";

        private ParticlePrefabScanResult _scanResultSo;
        private string _scanTimeStr = "";
        private List<string> _scanFolders = new(); // 当前配置的路径
        private bool _foldersDirty;
        private Vector2 _folderScroll;
        private List<DefaultAsset> _folderAssets = new(); // 用于ObjectField缓存
        private const float FolderFieldHeight = 22f;

        private void OnEnable()
        {
            LoadOrCreateScanResult();
            if (_scanResultSo)
            {
                _scanFolders = new List<string>(_scanResultSo.scanFolders);
            }

            // 同步ObjectField缓存
            _folderAssets = _scanFolders.Select(path =>
                !string.IsNullOrEmpty(path) ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(path) : null).ToList();
        }

        private void LoadOrCreateScanResult()
        {
            _scanResultSo = AssetDatabase.LoadAssetAtPath<ParticlePrefabScanResult>(ScanResultAssetPath);
            if (_scanResultSo == null)
            {
                _scanResultSo = CreateInstance<ParticlePrefabScanResult>();
                _scanResultSo.scanFolders.Add("Assets");
                AssetDatabase.CreateAsset(_scanResultSo, ScanResultAssetPath);
                AssetDatabase.SaveAssets();
                _scanFolders = new List<string>(_scanResultSo.scanFolders);
                StartScan();
            }
            else
            {
                _scanFolders = new List<string>(_scanResultSo.scanFolders);
                LoadScanResultToEntries();
            }
        }

        private void LoadScanResultToEntries()
        {
            _entries.Clear();
            foreach (var path in _scanResultSo.prefabPaths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!go) continue;
                _entries.Add(new Entry { PrefabPath = path, PrefabAsset = go });
            }

            _scanTimeStr = _scanResultSo.ScanTime.ToString("yyyy-MM-dd HH:mm:ss");
            _isScanning = false;
            _scanProgress = 1f;
        }

        private void SaveScanResultFromEntries()
        {
            if (_scanResultSo == null) return;
            _scanResultSo.prefabPaths = _entries.Select(e => e.PrefabPath).ToList();
            _scanResultSo.ScanTime = System.DateTime.Now;
            _scanResultSo.scanFolders = new List<string>(_scanFolders); // 保存路径
            EditorUtility.SetDirty(_scanResultSo);
            AssetDatabase.SaveAssets();
            _scanTimeStr = _scanResultSo.ScanTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void OnGUI()
        {
            DrawFolderSection(); // 新增：路径配置
            DrawToolbar();
            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Prefab 粒子效果列表", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(_scanTimeStr))
            {
                GUILayout.Label($"上次扫描时间：{_scanTimeStr}", EditorStyles.miniLabel);
            }

            DrawPreviewHeader();
            DrawEntries(GetScrollHeight());
            GUILayout.EndVertical();
            DrawPagingBar();
            DrawScanProgressBar();
        }

        private void DrawFolderSection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("扫描路径 (仅扫描以下文件夹下的Prefab)", EditorStyles.boldLabel);
            _folderScroll = GUILayout.BeginScrollView(_folderScroll, GUILayout.Height(GetFolderSectionHeight()));
            int removeIdx = -1;
            for (int i = 0; i < _scanFolders.Count; i++)
            {
                GUILayout.BeginHorizontal();
                var oldAsset = i < _folderAssets.Count ? _folderAssets[i] : null;
                var newAsset = (DefaultAsset)EditorGUILayout.ObjectField(oldAsset, typeof(DefaultAsset), false);
                string newPath = newAsset ? AssetDatabase.GetAssetPath(newAsset) : "";
                if (newPath != _scanFolders[i])
                {
                    _scanFolders[i] = newPath;
                    if (i < _folderAssets.Count) _folderAssets[i] = newAsset;
                    else _folderAssets.Add(newAsset);
                    _foldersDirty = true;
                }

                if (GUILayout.Button("-", GUILayout.Width(24))) removeIdx = i;
                GUILayout.EndHorizontal();
            }

            if (removeIdx >= 0 && _scanFolders.Count > 1)
            {
                _scanFolders.RemoveAt(removeIdx);
                if (removeIdx < _folderAssets.Count) _folderAssets.RemoveAt(removeIdx);
                _foldersDirty = true;
            }

            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", GUILayout.Width(28)))
            {
                _scanFolders.Add("Assets");
                _folderAssets.Add(AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets"));
                _foldersDirty = true;
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            GUILayout.Label(_foldersDirty ? "* 未保存的路径更改" : "路径已保存", EditorStyles.miniLabel, GUILayout.Width(140));
            GUILayout.FlexibleSpace();
            GUI.enabled = _foldersDirty;
            if (GUILayout.Button("保存路径设置", GUILayout.Width(120)))
            {
                if (_scanResultSo)
                {
                    _scanResultSo.scanFolders = new List<string>(_scanFolders);
                    EditorUtility.SetDirty(_scanResultSo);
                    AssetDatabase.SaveAssets();
                    _foldersDirty = false;
                }
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        
        private float GetFolderSectionHeight()
        {
            var allFolderHeight = 0f;
            if (_scanFolders != null)
            {
                allFolderHeight = FolderFieldHeight * Mathf.Min(_scanFolders.Count, 5);
            }
            
            return allFolderHeight + 20f;
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUI.enabled = !_isScanning;
            var iconRect = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18));
            var icon = EditorGUIUtility.IconContent("Search Icon");
            GUI.Label(iconRect, icon);
            _search = GUILayout.TextField(_search, GUILayout.Width(200));
            if (GUILayout.Button("重新收集", GUILayout.Width(90)))
            {
                if(EditorUtility.DisplayDialog($"准备重新收集",
                       "重新收集将扫描所有配置路径下的Prefab，需要等待一段时间，是否继续？", "继续", "取消"))
                {
                    StartScan();
                }
            }
            // 只有有选中项时才能预览
            int selectedCount = _previewSelected.Count;
            if (!_isPreviewing)
            {
                GUI.enabled = selectedCount > 0;
                if (GUILayout.Button("预览", GUILayout.Width(90)))
                {
                    _isPreviewing = true;
                    _previewPage = 0;
                    ShowPreviewScene();
                }
                
                GUI.enabled = true;

                if (selectedCount > 0)
                {
                    GUILayout.Label("当前已选择" + selectedCount + "个Prefab", GUILayout.Width(140));
                }
            }
            else
            {
                if (GUILayout.Button("结束预览", GUILayout.Width(90)))
                {
                    _isPreviewing = false;
                    _previewSelected.Clear();
                    ParticlePrefabPreviewSceneHelper.ClosePreviewScene();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"共 {_entries.Count} 个Prefab", GUILayout.Width(140));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawPreviewHeader()
        {
            if (_isPreviewing)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("上一页", GUILayout.Width(70)))
                {
                    if (_previewPage > 0) _previewPage--;
                    ShowPreviewScene();
                }

                if (GUILayout.Button("下一页", GUILayout.Width(70)))
                {
                    int maxPage = Mathf.Max(0, Mathf.CeilToInt(_previewSelected.Count / (float)PreviewPageSize) - 1);
                    if (_previewPage < maxPage) _previewPage++;
                    ShowPreviewScene();
                }

                if (GUILayout.Button("重新排列", GUILayout.Width(90)))
                {
                    ShowPreviewScene();
                }

                GUILayout.Label($"第 {_previewPage + 1} 页", GUILayout.Width(90));
                GUILayout.Space(20);
                // 一键播放按钮
                if (GUILayout.Button("一键播放", GUILayout.Width(80)))
                {
                    ParticlePrefabPreviewSceneHelper.PlayAllParticles();
                }

                GUILayout.EndHorizontal();
            }
        }
        
        private void DrawEntries(float scrollHeight)
        {
            // 分页和筛选
            var filtered = string.IsNullOrEmpty(_search)
                ? _entries
                : _entries.Where(e => e.PrefabPath.ToLower().Contains(_search.ToLower())).ToList();
            // 排序
            if (_sortColumn == SortColumn.PrefabName)
                filtered = _sortAscending ? filtered.OrderBy(e => Path.GetFileNameWithoutExtension(e.PrefabPath)).ToList() : filtered.OrderByDescending(e => Path.GetFileNameWithoutExtension(e.PrefabPath)).ToList();
            else
                filtered = _sortAscending ? filtered.OrderBy(e => e.PrefabPath).ToList() : filtered.OrderByDescending(e => e.PrefabPath).ToList();
            _totalPages = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)_pageSize));
            if (_currentPage >= _totalPages) _currentPage = _totalPages - 1;
            int start = _currentPage * _pageSize;
            int end = Mathf.Min(start + _pageSize, filtered.Count);

            // --- 新增表头 ---
            GUILayout.BeginHorizontal();
            float selectToggleWidth = 22f; // 选择toggle宽度
            float prefabWidth = 180f;
            float pathWidth = EditorGUIUtility.currentViewWidth - prefabWidth - selectToggleWidth - 60f;
            // 三态全选控件
            UpdatePreviewSelectState(filtered);
            GUILayout.Space(5f);
            var selectToggleLength = 18f;
            var toggleRect = GUILayoutUtility.GetRect(selectToggleLength, selectToggleLength, GUILayout.Width(selectToggleLength));
            string icon;
            if (_previewSelectPartial)
                icon = "-";
            else if (_previewSelectAll)
                icon = "✓";
            else
                icon = "";
            var style = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 8,
            };
            if (GUI.Button(toggleRect, icon, style))
            {
                if (_previewSelectPartial || !_previewSelectAll)
                {
                    foreach (var e in filtered) _previewSelected.Add(e.PrefabPath);
                }
                else
                {
                    foreach (var e in filtered) _previewSelected.Remove(e.PrefabPath);
                }
            }
            DrawSortLabel("Prefab", SortColumn.PrefabName, prefabWidth);
            DrawSortLabel("路径", SortColumn.Path, pathWidth);
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUILayout.Height(scrollHeight));
            for (int i = start; i < end; i++)
            {
                var e = filtered[i];
                GUILayout.BeginHorizontal(GUI.skin.box);
                bool sel = _previewSelected.Contains(e.PrefabPath);
                bool newSel = GUILayout.Toggle(sel, GUIContent.none, GUILayout.Width(selectToggleWidth));
                if (newSel != sel)
                {
                    if (newSel)
                        _previewSelected.Add(e.PrefabPath);
                    else
                        _previewSelected.Remove(e.PrefabPath);
                }
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(e.PrefabAsset, typeof(GameObject), false, GUILayout.Width(prefabWidth));
                EditorGUI.EndDisabledGroup();
                GUILayout.Label(e.PrefabPath, GUILayout.Width(pathWidth));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void UpdatePreviewSelectState(List<Entry> filtered)
        {
            int count = filtered.Count;
            int sel = filtered.Count(e => _previewSelected.Contains(e.PrefabPath));
            _previewSelectAll = sel == count && count > 0;
            _previewSelectPartial = sel > 0 && sel < count;
        }

        private void DrawSortLabel(string label, SortColumn column, float width)
        {
            string icon = _sortColumn == column ? (_sortAscending ? " ▲" : " ▼") : "";
            var style = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 22,
                padding = new RectOffset(4, 4, 0, 0)
            };
            var rect = GUILayoutUtility.GetRect(new GUIContent(label + icon), style, GUILayout.Width(width));
            bool isHover = rect.Contains(Event.current.mousePosition);
            if (isHover)
            {
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }

            if (GUI.Button(rect, label + icon, style))
            {
                if (_sortColumn == column)
                    _sortAscending = !_sortAscending;
                else
                {
                    _sortColumn = column;
                    _sortAscending = true;
                }
            }
        }

        private void DrawPagingBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("每页:", GUILayout.Width(40));
            _pageSize = EditorGUILayout.IntField(_pageSize, GUILayout.Width(50));
            if (_pageSize < 5) _pageSize = 5;
            GUILayout.Space(20);
            GUI.enabled = _currentPage > 0;
            if (GUILayout.Button("上一页", GUILayout.Width(70))) _currentPage--;
            GUI.enabled = _currentPage < _totalPages - 1;
            if (GUILayout.Button("下一页", GUILayout.Width(70))) _currentPage++;
            GUI.enabled = true;
            GUILayout.Label($"第 {_currentPage + 1} / {_totalPages} 页", GUILayout.Width(120));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawScanProgressBar()
        {
            if (_isScanning)
            {
                GUILayout.BeginHorizontal();
                var r = GUILayoutUtility.GetRect(200, 18, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(r, _scanProgress, $"扫描中: {_scanCursor}/{_pendingGuids.Count}");
                GUILayout.EndHorizontal();
            }
        }

        private void StartScan()
        {
            StopScan();
            _entries.Clear();
            var guids = new List<string>();
            foreach (var folder in _scanFolders)
            {
                if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                {
                    guids.AddRange(AssetDatabase.FindAssets("t:Prefab", new[] { folder }));
                }
            }

            _pendingGuids = guids.Distinct().ToList();
            _scanCursor = 0;
            _isScanning = true;
            _scanProgress = 0f;
            EditorApplication.update += UpdateScan;
            _currentPage = 0;
        }

        private void StopScan()
        {
            _isScanning = false;
            EditorApplication.update -= UpdateScan;
            _pendingGuids.Clear();
            _scanCursor = 0;
            _scanProgress = 0f;
        }

        private void UpdateScan()
        {
            if (!_isScanning) return;
            int perFrame = 5;
            int processed = 0;
            var prefabSet = new HashSet<string>(_entries.Select(e => e.PrefabPath));
            while (_scanCursor < _pendingGuids.Count && processed < perFrame)
            {
                var guid = _pendingGuids[_scanCursor++];
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (prefabSet.Contains(path)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!go) continue;
                if (go.GetComponentInChildren<ParticleSystem>(true))
                {
                    prefabSet.Add(path);
                    _entries.Add(new Entry { PrefabPath = path, PrefabAsset = go });
                }

                processed++;
            }

            _scanProgress = _pendingGuids.Count == 0 ? 1f : _scanCursor / (float)_pendingGuids.Count;
            if (_scanCursor >= _pendingGuids.Count)
            {
                StopScan();
                SaveScanResultFromEntries();
            }

            Repaint();
        }

        private void ShowPreviewScene()
        {
            var all = _previewSelected.ToList();
            int maxPage = Mathf.Max(0, Mathf.CeilToInt(all.Count / (float)PreviewPageSize) - 1);
            if (_previewPage > maxPage) _previewPage = maxPage;
            int start = _previewPage * PreviewPageSize;
            int end = Mathf.Min(start + PreviewPageSize, all.Count);
            var paths = all.GetRange(start, end - start);
            var prefabs = paths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(go => go).ToList();
            ParticlePrefabPreviewSceneHelper.OpenPreviewScene();
            ParticlePrefabPreviewSceneHelper.SpawnPrefabs(prefabs);
            // 新增：选中所有实例化对象
            var spawned = ParticlePrefabPreviewSceneHelper.GetSpawnedPrefabs();
            if (spawned is { Count: > 0 })
            {
                Selection.objects = spawned.Where(go => go).ToArray<Object>();
            }
        }

        private float GetScrollHeight()
        {
            var paging = 36f;
            var progress = 26f;
            var top = 170f + GetFolderSectionHeight();
            var h = position.height - top - paging - progress;
            return Mathf.Max(80f, h);
        }

        private void OnDisable()
        {
            if (_isPreviewing)
            {
                _isPreviewing = false;
                _previewSelected.Clear();
                ParticlePrefabPreviewSceneHelper.ClosePreviewScene();
            }
        }
    }
}