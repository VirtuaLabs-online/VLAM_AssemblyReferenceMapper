using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

public class AssemblyReferenceMapper : EditorWindow
{
    private Vector2 _scrollPosition;
    private Vector2 _mappingsScrollPos;

    private readonly List<string> _packageFolders = new();
    private Dictionary<string, NamespaceMapping> _namespaceMap = new();

    private bool _isScanning;
    private string _scanProgress;
    private bool _showMappings;
    private string _filterText;

    private const string MAPPINGS_FILE = "Assets/VirtuaLabs/Resources/AssemblyNamespaceIndex.json";
    
    private Queue<string> _asmdefQueue;
    private Queue<string> _csQueue;

    private string _currentAsmdef;
    private string _currentCs;

    private int _totalAsmdefs;
    private int _processedAsmdefs;

    private int _totalCs;
    private int _processedCs;


    [MenuItem("Tools/VirtuaLabs/Assembly Reference Mapper")]
    public static void ShowWindow()
    {
        var window = GetWindow<AssemblyReferenceMapper>("Assembly Mapper");
        window.minSize = new Vector2(650, 420);
    }

    private void OnEnable() => LoadExistingMappings();

    #region GUI

    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        EditorGUILayout.LabelField("Assembly Reference Mapper", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Builds a global namespace → assembly ownership index",
            EditorStyles.miniLabel);

        DrawUILine(Color.gray);

        DrawFolderSection();
        DrawScanSection();
        DrawMappingsSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawFolderSection()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Folders to Scan", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Add folders that contain .asmdef files. "
          + "Namespaces declared in these assemblies will be indexed.",
            MessageType.Info);

        var dropArea = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
        GUI.Box(
            dropArea,
            "Drag & drop folders here\n(Assets/… or Packages/…)",
            EditorStyles.helpBox);

        HandleDragAndDrop(dropArea);

        EditorGUILayout.Space(5);

        if (_packageFolders.Count == 0)
        {
            EditorGUILayout.HelpBox("No folders added.", MessageType.Warning);
        }
        else
        {
            for (int i = _packageFolders.Count - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(_packageFolders[i], EditorStyles.miniLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    _packageFolders.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Add Folder…", GUILayout.Height(28)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
            if (!string.IsNullOrEmpty(path))
                AddPackageFolder(path);
        }

        if (GUILayout.Button("Add All Packages", GUILayout.Height(28)))
            AddAllPackagesFolders();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawScanSection()
    {
        EditorGUILayout.Space(15);
        DrawUILine(Color.gray);

        EditorGUILayout.LabelField("Scan", EditorStyles.boldLabel);

        if (!string.IsNullOrEmpty(_scanProgress))
            EditorGUILayout.HelpBox(_scanProgress, MessageType.Info);

        GUI.enabled = !_isScanning && _packageFolders.Count > 0;
        if (GUILayout.Button("Scan & Build Namespace Index", GUILayout.Height(36)))
            ScanAllFolders();
        GUI.enabled = true;
    }

    private void DrawMappingsSection()
    {
        EditorGUILayout.Space(15);
        DrawUILine(Color.gray);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            $"Namespace Index ({_namespaceMap.Count})",
            EditorStyles.boldLabel);

        if (_namespaceMap.Count > 0)
        {
            if (GUILayout.Button("Export", GUILayout.Width(70)))
                ExportMappings();

            if (GUILayout.Button("Clear", GUILayout.Width(70)))
            {
                if (EditorUtility.DisplayDialog(
                    "Clear Index",
                    "Delete all learned namespace mappings?",
                    "Clear",
                    "Cancel"))
                {
                    _namespaceMap.Clear();
                    SaveMappings();
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        if (_namespaceMap.Count == 0)
            return;

        _filterText = EditorGUILayout.TextField("Filter", _filterText);
        _showMappings = EditorGUILayout.Foldout(_showMappings, "View Mappings", true);

        if (!_showMappings)
            return;

        _mappingsScrollPos = EditorGUILayout.BeginScrollView(
            _mappingsScrollPos,
            GUILayout.Height(240));

        foreach (var kvp in FilteredMappings())
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(kvp.Key, GUILayout.Width(260));
            EditorGUILayout.LabelField("→", GUILayout.Width(18));
            EditorGUILayout.LabelField(
                $"{kvp.Value.assemblyName} ({kvp.Value.source})",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    #endregion

    #region Scanning

    private void ScanAllFolders()
    {
        if (_isScanning)
            return;

        _isScanning = true;
        _scanProgress = "Preparing scan…";

        _asmdefQueue = new Queue<string>();
        _csQueue = new Queue<string>();

        foreach (var folder in _packageFolders)
        {
            if (!Directory.Exists(folder))
                continue;

            foreach (var asmdef in Directory.GetFiles(
                         folder, "*.asmdef", SearchOption.AllDirectories))
            {
                _asmdefQueue.Enqueue(NormalizeUnityPath(asmdef));
            }
        }

        _totalAsmdefs = _asmdefQueue.Count;
        _processedAsmdefs = 0;
        _processedCs = 0;

        EditorApplication.update += ScanStep;
    }

    private void ScanStep()
    {
        try
        {
            // Process next asmdef
            if (_csQueue.Count == 0 && _asmdefQueue.Count > 0)
            {
                _currentAsmdef = _asmdefQueue.Dequeue();
                _processedAsmdefs++;

                var asmdef = JsonConvert.DeserializeObject<AssemblyDefinition>(
                    File.ReadAllText(_currentAsmdef));

                if (string.IsNullOrEmpty(asmdef?.name))
                    return;

                string root = _currentAsmdef.StartsWith("Packages/")
                    ? "Packages"
                    : "Assets";

                string dir = NormalizeUnityPath(Path.GetDirectoryName(_currentAsmdef));
                var csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);

                _totalCs += csFiles.Length;

                foreach (var cs in csFiles)
                    _csQueue.Enqueue($"{asmdef.name}|{root}|{_currentAsmdef}|{cs}");

                UpdateProgressBar();
                return;
            }

            // Process next C# file
            if (_csQueue.Count > 0)
            {
                var data = _csQueue.Dequeue().Split('|');

                string assembly = data[0];
                string source = data[1];
                string asmdefPath = data[2];
                string csFile = data[3];

                _currentCs = csFile;
                _processedCs++;

                foreach (var ns in ExtractNamespaces(csFile))
                {
                    RegisterNamespace(ns, assembly, asmdefPath, source);
                }

                UpdateProgressBar();
                return;
            }

            foreach (var key in _namespaceMap.Keys.ToList())
            {
                _namespaceMap[key].asmdefPath =
                    NormalizeUnityPath(_namespaceMap[key].asmdefPath);
            }
            
            FinishScan();
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            FinishScan();
        }
    }

    private void UpdateProgressBar()
    {
        float progress = _totalCs == 0
            ? 0f
            : (float)_processedCs / _totalCs;

        bool cancel = EditorUtility.DisplayCancelableProgressBar(
            "Assembly Reference Mapper",
            $"Scanning:\n{_currentAsmdef}\n{_currentCs}",
            progress);

        _scanProgress =
            $"Asmdefs: {_processedAsmdefs}/{_totalAsmdefs}\n" +
            $"Files: {_processedCs}/{_totalCs}";

        Repaint();

        if (cancel)
            FinishScan();
    }

    private void FinishScan()
    {
        EditorUtility.ClearProgressBar();
        EditorApplication.update -= ScanStep;

        _isScanning = false;
        _scanProgress =
            $"Scan complete. Total namespaces: {_namespaceMap.Count}";

        SaveMappings();
        Repaint();
    }


    private void ScanFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var asmdef in Directory.GetFiles(
                     folder, "*.asmdef", SearchOption.AllDirectories))
        {
            ProcessAsmdef(asmdef);
        }
    }
    
    private static string NormalizeUnityPath(string path)
    {
        return path.Replace("\\", "/");
    }

    private void ProcessAsmdef(string asmdefPath)
    {
        try
        {
            var asmdef = JsonConvert.DeserializeObject<AssemblyDefinition>(
                File.ReadAllText(asmdefPath));

            if (string.IsNullOrEmpty(asmdef?.name))
                return;

            string root = asmdefPath.StartsWith("Packages/")
                ? "Packages"
                : "Assets";

            string dir = Path.GetDirectoryName(asmdefPath);
            var csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);

            foreach (var cs in csFiles)
            {
                foreach (var ns in ExtractNamespaces(cs))
                {
                    RegisterNamespace(
                        ns,
                        asmdef.name,
                        asmdefPath,
                        root);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed processing {asmdefPath}: {e.Message}");
        }
    }

    #endregion

    #region Namespace Ownership

    private void RegisterNamespace(
        string ns,
        string assembly,
        string asmdefPath,
        string source)
    {
        if (string.IsNullOrEmpty(ns))
            return;
        
        asmdefPath = NormalizeUnityPath(asmdefPath);
        source = NormalizeUnityPath(source);

        var mapping = new NamespaceMapping
        {
            assemblyName = assembly,
            asmdefPath = asmdefPath,
            source = source
        };

        if (!_namespaceMap.TryGetValue(ns, out var existing))
        {
            _namespaceMap[ns] = mapping;
            return;
        }

        if (existing.source == "Packages" && source == "Assets")
        {
            _namespaceMap[ns] = mapping;
            return;
        }

        if (ns.Length > existing.namespaceLength)
        {
            _namespaceMap[ns] = mapping;
        }
    }

    #endregion

    #region Helpers

    private static HashSet<string> ExtractNamespaces(string csFile)
    {
        var result = new HashSet<string>();

        var matches = System.Text.RegularExpressions.Regex.Matches(
            File.ReadAllText(csFile),
            @"namespace\s+([\w\.]+)");

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var full = m.Groups[1].Value;
            var parts = full.Split('.');
            string current = "";

            foreach (var p in parts)
            {
                current = string.IsNullOrEmpty(current) ? p : $"{current}.{p}";
                result.Add(current);
            }
        }

        return result;
    }

    private IEnumerable<KeyValuePair<string, NamespaceMapping>> FilteredMappings()
    {
        return _namespaceMap
            .Where(k =>
                string.IsNullOrEmpty(_filterText)
                || k.Key.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
                || k.Value.assemblyName.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k.Key);
    }

    private void HandleDragAndDrop(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition))
            return;

        if (e.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            e.Use();
        }
        else if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (AssetDatabase.IsValidFolder(path))
                    AddPackageFolder(path);
            }
            e.Use();
        }
    }

    private void AddPackageFolder(string path)
    {
        if (path.StartsWith(Application.dataPath))
            path = "Assets" + path[Application.dataPath.Length..];

        if (!_packageFolders.Contains(path))
            _packageFolders.Add(path);
    }

    private void AddAllPackagesFolders()
    {
        if (!Directory.Exists("Packages"))
            return;

        foreach (var dir in Directory.GetDirectories("Packages"))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("com.unity."))
                AddPackageFolder(dir.Replace("\\", "/"));
        }
    }

    #endregion

    #region Persistence

    private void SaveMappings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MAPPINGS_FILE));

        File.WriteAllText(
            MAPPINGS_FILE,
            JsonConvert.SerializeObject(
                new MappingsWrapper { mappings = _namespaceMap },
                Formatting.Indented));

        AssetDatabase.Refresh();
    }

    private void LoadExistingMappings()
    {
        if (!File.Exists(MAPPINGS_FILE))
            return;

        var wrapper = JsonConvert.DeserializeObject<MappingsWrapper>(
            File.ReadAllText(MAPPINGS_FILE));

        _namespaceMap = wrapper?.mappings ?? new();
    }

    private void ExportMappings()
    {
        string path = EditorUtility.SaveFilePanel(
            "Export Namespace Index",
            "",
            "AssemblyNamespaceIndex.json",
            "json");

        if (string.IsNullOrEmpty(path))
            return;

        File.WriteAllText(
            path,
            JsonConvert.SerializeObject(
                new MappingsWrapper { mappings = _namespaceMap },
                Formatting.Indented));
    }

    #endregion

    #region UI Utils

    private static void DrawUILine(Color color, int thickness = 1, int padding = 8)
    {
        var r = EditorGUILayout.GetControlRect(false, padding + thickness);
        r.height = thickness;
        r.y += padding * 0.5f;
        EditorGUI.DrawRect(r, color);
    }

    #endregion

    #region Models

    [Serializable]
    private class AssemblyDefinition
    {
        public string name;
    }

    [Serializable]
    private class NamespaceMapping
    {
        public string assemblyName;
        public string asmdefPath;
        public string source;

        [JsonIgnore]
        public int namespaceLength => assemblyName?.Length ?? 0;
    }

    [Serializable]
    private class MappingsWrapper
    {
        public Dictionary<string, NamespaceMapping> mappings;
    }

    #endregion
}
