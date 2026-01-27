/*
 * -----------------------------------------------------------------------------
 * 名称: VR_Exact_Mesh_Occlusion_Culler (UI Final Polish)
 * 架构师: Chief Software Architect
 * 版本: 3.6.0 (UX Final)
 * 日期: 2026-01-22
 * -----------------------------------------------------------------------------
 * [UI 修正]
 * 1. 样式: 修正 "Clear List" 按钮宽度 (60 -> 80)，防止文字被截断。
 * 2. 布局: "立即执行" 按钮现在停靠在窗口底部 (Docked Bottom)，不再随列表滚动，
 * 确保在处理长列表时按钮始终可见，无需反复滚动。
 * -----------------------------------------------------------------------------
 */

using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class VRMeshOptimizerTool : EditorWindow
{
    // =========================================================
    // 0. 版本元数据
    // =========================================================
    private const string TOOL_VERSION = "3.6.0 (UX Final)";
    private const string BUILD_DATE = "2026.01.22";
    private const string ARCH_INFO = "Arch: Inverse-Raycast | Docked Layout";

    // =========================================================
    // 1. 数据定义
    // =========================================================

    [SerializeField] private List<Transform> observerPoints = new List<Transform>();
    [SerializeField] private List<GameObject> targetObjects = new List<GameObject>();

    [Header("Precision Settings")]
    [Tooltip("表面偏移 (Bias)：防止射线打中面片自身 (推荐 0.02)")]
    [SerializeField] private float surfaceBias = 0.02f;

    [Tooltip("高精度模式：同时检测中心点和三个顶点 (推荐开启，杜绝漏删)")]
    [SerializeField] private bool checkVertices = true;

    [Tooltip("拓扑保护：保留可见面的邻接面，防止接缝")]
    [SerializeField] private bool performDilation = true;

    private SerializedObject serializedObj;
    private Vector2 scrollPos;

    // UI 样式
    private GUIStyle dropZoneStyle;
    private GUIStyle footerStyle;
    private GUIStyle headerButtonStyle;

    // Debug 缓存
    private static List<Vector3> debugVisiblePoints = new List<Vector3>();

    [MenuItem("Tools/VR Mesh Optimizer (v3.6 Final)")]
    public static void ShowWindow()
    {
        var win = GetWindow<VRMeshOptimizerTool>("VR Mesh Optimizer Pro");
        win.minSize = new Vector2(450, 750);
    }

    private void OnEnable()
    {
        serializedObj = new SerializedObject(this);
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // =========================================================
    // 2. 交互层 (UI Layer)
    // =========================================================

    private void InitStyles()
    {
        if (dropZoneStyle == null)
        {
            dropZoneStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                richText = true,
                normal = {
                    textColor = new Color(0.9f, 0.95f, 1f), 
                    // 蓝色背景
                    background = MakeTex(2, 2, new Color(0.2f, 0.35f, 0.6f, 0.6f))
                }
            };
        }
        if (footerStyle == null)
        {
            footerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };
        }
        if (headerButtonStyle == null)
        {
            headerButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                // [UI 修正 1] 增加宽度，防止 "Clear List" 显示不全
                fixedWidth = 80
            };
        }
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i) pix[i] = col;
        Texture2D result = new Texture2D(width, height); result.SetPixels(pix); result.Apply(); return result;
    }

    private void OnGUI()
    {
        InitStyles();
        serializedObj.Update();

        // --- 顶部标题区 ---
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        // GUILayout.Label($"VR Mesh Optimizer {TOOL_VERSION}", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Help", EditorStyles.toolbarButton))
        {
            EditorUtility.DisplayDialog("指南", "1. 拖入观察点。\n2. 拖入目标模型。\n3. 点击底部按钮执行。", "OK");
        }
        GUILayout.EndHorizontal();

        // --- 开始滚动区域 (内容区) ---
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.HelpBox("此工具用来清除看不到的Mesh网格", MessageType.Info);
        GUILayout.Space(10);

        // --- 1. 观察点配置 ---
        DrawSectionHeader("1. 观察点 (Observation Points)", () =>
        {
            observerPoints.Clear();
            Debug.Log("观察点列表已清空");
        });

        Color originalBgColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.4f, 0.7f, 0.9f, 1f); // 自定义背景
        DrawDropArea(GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true)), ">>> 拖入 Camera / Transform <<<", (obj) =>
        {
            Transform t = obj.GetComponent<Transform>();
            if (t && !observerPoints.Contains(t)) { observerPoints.Add(t); return true; }
            return false;
        });
        GUI.backgroundColor = originalBgColor;
        EditorGUILayout.PropertyField(serializedObj.FindProperty("observerPoints"), true);


        GUILayout.Space(20);

        // --- 2. 目标模型配置 ---
        DrawSectionHeader("2. 目标模型 (Target Meshes)", () =>
        {
            targetObjects.Clear();
            Debug.Log("目标模型列表已清空");
        });

        GUI.backgroundColor = new Color(0.4f, 0.7f, 0.9f, 1f); // 自定义背景
        DrawDropArea(GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true)), ">>> 拖入要清除 Mesh 的 GameObject <<<\n<size=10>(必须包含MeshFiter, SharedMesh, MeshCollider)</size>", (obj) =>
        {
            if (targetObjects.Contains(obj)) return false;

            // 组件完整性检查
            MeshFilter mf = obj.GetComponent<MeshFilter>();
            MeshCollider mc = obj.GetComponent<MeshCollider>();
            if (mf == null || mf.sharedMesh == null || mc == null)
            {
                Debug.LogError($"[拒绝] '{obj.name}' 缺少 MeshFilter/SharedMesh 或 MeshCollider。");
                return false;
            }
            targetObjects.Add(obj);
            return true;
        });
        GUI.backgroundColor = originalBgColor;
        EditorGUILayout.PropertyField(serializedObj.FindProperty("targetObjects"), true);

        GUILayout.Space(20);

        // --- 3. 参数配置 ---
        GUILayout.Label("3. 内核参数 (Kernel Params)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.PropertyField(serializedObj.FindProperty("checkVertices"), new GUIContent("多重采样"));
        EditorGUILayout.PropertyField(serializedObj.FindProperty("surfaceBias"), new GUIContent("表面偏移 (Bias)"));
        EditorGUILayout.PropertyField(serializedObj.FindProperty("performDilation"), new GUIContent("邻接膨胀"));
        EditorGUILayout.EndVertical();

        GUILayout.Space(20); // 内容区底部留白

        // --- 结束滚动区域 ---
        EditorGUILayout.EndScrollView();

        // =========================================================
        // [UI 修正 2] 底部停靠区域 (Docked Bottom)
        // 将执行按钮移出 ScrollView，确保它永远固定在窗口最下方
        // =========================================================

        // 绘制分割线
        GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));

        GUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins); // 增加一点边距
        GUILayout.Space(5);

        GUI.backgroundColor = new Color(0.1f, 0.9f, 0.1f, 1f); // 绿色背景
        if (GUILayout.Button(">>> 立即执行优化 (Execute) <<<", GUILayout.Height(50)))
        {
            if (CheckValidation()) EditorApplication.delayCall += ExecuteExactOptimization;
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(5);
        DrawFooter(); // 版本信息也固定在底部
        GUILayout.EndVertical();

        serializedObj.ApplyModifiedProperties();
    }

    private void DrawSectionHeader(string title, System.Action onClear)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(title, EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        // 这里的 headerButtonStyle 已经在 InitStyles 中修正了宽度
        if (GUILayout.Button("Clear List", headerButtonStyle))
        {
            if (EditorUtility.DisplayDialog("确认", "确定要清空该列表吗？", "清空", "取消"))
            {
                onClear?.Invoke();
                serializedObj.Update();
            }
        }
        GUILayout.EndHorizontal();
    }

    private void DrawFooter()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("System: Ready", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"Ver: {TOOL_VERSION} | {BUILD_DATE}", footerStyle);
        GUILayout.EndHorizontal();
    }

    // --- 以下为核心逻辑，保持不变 (Validation / Drop / Algorithm) ---

    private void DrawDropArea(Rect dropArea, string text, System.Func<GameObject, bool> onDrop)
    {
        GUI.Box(dropArea, text, dropZoneStyle);
        Event evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition)) return;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                int rejected = 0;
                foreach (Object obj in DragAndDrop.objectReferences)
                    if (obj is GameObject go && !onDrop(go)) rejected++;
                if (rejected > 0) Debug.LogWarning($"{rejected} 个物体被拒绝添加 (查看Console详情)。");
                serializedObj.Update();
            }
            Event.current.Use();
        }
    }

    private bool CheckValidation()
    {
        if (observerPoints.Count == 0 || targetObjects.Count == 0)
        {
            EditorUtility.DisplayDialog("参数校验失败", "列表不能为空。", "OK");
            return false;
        }
        return true;
    }

    private void ExecuteExactOptimization()
    {
        debugVisiblePoints.Clear();
        string scenePath = SceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(scenePath))
        {
            EditorUtility.DisplayDialog("错误", "请先保存场景。", "OK");
            return;
        }

        string sceneDir = Path.GetDirectoryName(scenePath).Replace("\\", "/");
        string folderName = $"OptimizedMeshes_{System.DateTime.Now:yyyyMMdd_HHmmss}";
        string fullSavePath = EnsureFolderExists(sceneDir, folderName);
        int processedCount = 0;

        foreach (var targetObj in targetObjects)
        {
            if (targetObj == null) continue;
            MeshFilter mf = targetObj.GetComponent<MeshFilter>();
            MeshCollider mc = targetObj.GetComponent<MeshCollider>();
            if (!mf || !mc) continue;

            Transform trans = targetObj.transform;
            Mesh mesh = mf.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int totalTriangles = triangles.Length / 3;
            HashSet<int> keptTriangles = new HashSet<int>();

            try
            {
                for (int i = 0; i < totalTriangles; i++)
                {
                    int idx0 = triangles[i * 3];
                    int idx1 = triangles[i * 3 + 1];
                    int idx2 = triangles[i * 3 + 2];
                    Vector3 v0 = trans.TransformPoint(vertices[idx0]);
                    Vector3 v1 = trans.TransformPoint(vertices[idx1]);
                    Vector3 v2 = trans.TransformPoint(vertices[idx2]);
                    Vector3 center = (v0 + v1 + v2) / 3f;
                    Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                    bool isVisible = false;

                    foreach (Transform obs in observerPoints)
                    {
                        if (!obs) continue;
                        Vector3 viewDir = (center - obs.position).normalized;
                        if (Vector3.Dot(viewDir, normal) > 0) continue; // Backface

                        if (IsPointVisible(obs.position, center)) { isVisible = true; break; }
                        if (checkVertices)
                        {
                            if (IsPointVisible(obs.position, v0)) { isVisible = true; break; }
                            if (IsPointVisible(obs.position, v1)) { isVisible = true; break; }
                            if (IsPointVisible(obs.position, v2)) { isVisible = true; break; }
                        }
                    }
                    if (isVisible) keptTriangles.Add(i);
                    if (i % 200 == 0) EditorUtility.DisplayProgressBar($"Processing {targetObj.name}", $"{i}/{totalTriangles}", (float)i / totalTriangles);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            if (performDilation) keptTriangles = DilateTriangleSelection(mesh, keptTriangles);

            if (keptTriangles.Count == 0)
            {
                Debug.LogWarning($"[Result] {targetObj.name} 结果为空。");
                continue;
            }

            Mesh newMesh = RebuildMeshFast(mesh, keptTriangles);
            string safeName = SanitizeFileName(targetObj.name);
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath($"{fullSavePath}/{safeName}_Optimized.asset");
            newMesh.name = Path.GetFileNameWithoutExtension(uniquePath);
            AssetDatabase.CreateAsset(newMesh, uniquePath);
            mf.sharedMesh = newMesh;
            mc.sharedMesh = newMesh;

            Debug.Log($"<color=green>[SUCCESS]</color> {targetObj.name} Saved | Reduced: {(1 - (float)keptTriangles.Count / totalTriangles) * 100:F1}%");
            processedCount++;
        }

        if (processedCount > 0)
        {
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); SceneView.RepaintAll();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(fullSavePath));
        }
    }

    private string SanitizeFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
        return fileName.Replace('\\', '_').Replace('/', '_').Trim();
    }

    private bool IsPointVisible(Vector3 eye, Vector3 target)
    {
        Vector3 dir = target - eye;
        float dist = dir.magnitude;
        Vector3 biased = target - (dir.normalized * surfaceBias);
        if (Vector3.Distance(eye, biased) > dist) biased = target;
        if (Physics.Linecast(eye, biased, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
        if (debugVisiblePoints.Count < 2000) debugVisiblePoints.Add(target);
        return true;
    }

    private HashSet<int> DilateTriangleSelection(Mesh mesh, HashSet<int> indices)
    {
        HashSet<int> dilated = new HashSet<int>(indices);
        int[] tris = mesh.triangles;
        Dictionary<int, List<int>> map = new Dictionary<int, List<int>>();
        for (int i = 0; i < tris.Length / 3; i++)
        {
            int b = i * 3; AddMap(map, tris[b], i); AddMap(map, tris[b + 1], i); AddMap(map, tris[b + 2], i);
        }
        foreach (int idx in indices)
        {
            int b = idx * 3;
            if (b + 2 < tris.Length) { UnionMap(dilated, map, tris[b]); UnionMap(dilated, map, tris[b + 1]); UnionMap(dilated, map, tris[b + 2]); }
        }
        return dilated;
    }
    private void AddMap(Dictionary<int, List<int>> map, int v, int t) { if (!map.TryGetValue(v, out var l)) { l = new List<int>(); map[v] = l; } l.Add(t); }
    private void UnionMap(HashSet<int> set, Dictionary<int, List<int>> map, int v) { if (map.TryGetValue(v, out var l)) foreach (var t in l) set.Add(t); }

    private Mesh RebuildMeshFast(Mesh src, HashSet<int> indices)
    {
        Mesh m = new Mesh();
        m.vertices = src.vertices; m.uv = src.uv; m.normals = src.normals; m.tangents = src.tangents; m.subMeshCount = src.subMeshCount;
        m.boneWeights = src.boneWeights; m.bindposes = src.bindposes;
        int ctr = 0;
        for (int i = 0; i < src.subMeshCount; i++)
        {
            int[] sTris = src.GetTriangles(i); List<int> nTris = new List<int>();
            for (int k = 0; k < sTris.Length; k += 3)
            {
                if (indices.Contains(ctr)) { nTris.Add(sTris[k]); nTris.Add(sTris[k + 1]); nTris.Add(sTris[k + 2]); }
                ctr++;
            }
            m.SetTriangles(nTris, i);
        }
        m.RecalculateBounds(); m.Optimize();
        return m;
    }

    private string EnsureFolderExists(string p, string n)
    {
        string f = $"{p}/{n}"; if (AssetDatabase.IsValidFolder(f)) return f;
        return AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder(p, n));
    }

    private void OnSceneGUI(SceneView sv)
    {
        if (observerPoints.Count > 0) { Handles.color = Color.cyan; foreach (var t in observerPoints) if (t) Handles.SphereHandleCap(0, t.position, Quaternion.identity, 0.15f, EventType.Repaint); }
        if (debugVisiblePoints.Count > 0) { Handles.color = new Color(0, 1, 0, 0.5f); foreach (var p in debugVisiblePoints) Handles.DotHandleCap(0, p, Quaternion.identity, 0.01f, EventType.Repaint); }
    }
}