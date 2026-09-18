#if UNITY_EDITOR
using MDPro3.Plugins.Features.ReleaseDateSort;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MDPro3.Plugins.EditorTools
{
    /// <summary>
    /// Development helper. Runs through Unity batchmode:
    ///   Unity.exe -batchmode -quit -projectPath &lt;project&gt;
    ///             -executeMethod MDPro3.Plugins.EditorTools.PluginSelfCheck.DumpPrefabs
    ///             -dumpPath &lt;output file&gt;
    /// It only inspects / instantiates UI prefabs and writes a human readable report,
    /// it never changes project files.
    /// </summary>
    public static class PluginSelfCheck
    {
        private const string PopupAsset = "Assets/Prefabs/Popup/PopupSearchOrder.prefab";
        private const string DeckEditorAsset = "Assets/Prefabs/ServantUI/DeckEditorUI.prefab";
        private const string DefaultOutput = "plugins/.selfcheck/prefab-dump.txt";

        public static void DumpPrefabs()
        {
            int exitCode = 0;

            try
            {
                string output = GetArgument("-dumpPath");
                if (string.IsNullOrEmpty(output))
                    output = Path.Combine(Application.dataPath, "..", "..", DefaultOutput);

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));

                var sb = new StringBuilder();
                sb.AppendLine("MDPro3 plugin self check report");
                sb.AppendLine("Unity: " + Application.unityVersion);
                sb.AppendLine("Plugin type assembly: " + typeof(PluginInfo).Assembly.GetName().Name);
                sb.AppendLine();

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                DumpPluginSetup(sb);
                DumpPopupStructure(sb);
                DumpPopupLayout(sb);
                DumpPopupInjection(sb);
                DumpSubtree(DeckEditorAsset, "FilterAndSortArea", sb);

                File.WriteAllText(output, sb.ToString(), new UTF8Encoding(false));
                Debug.Log("[PluginSelfCheck] report written to " + output);
            }
            catch (Exception e)
            {
                exitCode = 1;
                Debug.LogError("[PluginSelfCheck] failed: " + e);
            }
            finally
            {
                if (Application.isBatchMode)
                    EditorApplication.Exit(exitCode);
            }
        }

        private static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static GameObject Instantiate(string assetPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                return null;

            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name + "(instance)";
            return instance;
        }

        private static void DumpPluginSetup(StringBuilder sb)
        {
            Section(sb, "plugin setup (config + feature registry)", PluginInfo.ConfigFolderName + "\\" + PluginInfo.ConfigFileName);

            try
            {
                PluginConfig.Load();
                sb.AppendLine("config file: " + (PluginConfig.Found
                    ? PluginConfig.LoadedPath
                    : "not found, every feature uses its default (enabled)"));
                sb.AppendLine("logFeatureTicks: " + PluginConfig.LogFeatureTicks + ", logEvents: " + PluginConfig.LogEvents);

                foreach (var setting in PluginConfig.Settings)
                {
                    if (setting == null)
                        continue;
                    sb.AppendLine("config entry: " + setting.id + " enabled=" + setting.enabled
                        + (string.IsNullOrEmpty(setting.note) ? string.Empty : "  (" + setting.note + ")"));
                }

                PluginRegistry.Initialize();
                sb.AppendLine("features registered: " + PluginRegistry.Features.Count
                    + ", running: " + PluginRegistry.EnabledFeatures.Count);
                foreach (var feature in PluginRegistry.Features)
                {
                    sb.AppendLine("  " + feature.Id + " - " + feature.DisplayName
                        + " -> " + (PluginRegistry.IsRunning(feature.Id) ? "running" : "not running")
                        + ", " + PluginConfig.DescribeFeature(feature.Id));
                }

                PluginRegistry.Shutdown();
            }
            catch (Exception e)
            {
                sb.AppendLine("!! plugin setup check failed: " + e);
            }

            sb.AppendLine();
        }

        private static void DumpPopupStructure(StringBuilder sb)
        {
            Section(sb, "sort popup structure", PopupAsset);
            var instance = Instantiate(PopupAsset);
            if (instance == null)
            {
                sb.AppendLine("!! could not load prefab");
                return;
            }

            try
            {
                DumpTransform(instance.transform, sb, 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            sb.AppendLine();
        }

        private static void DumpPopupLayout(StringBuilder sb)
        {
            Section(sb, "sort popup layout (rebuilt)", PopupAsset);
            var instance = Instantiate(PopupAsset);
            if (instance == null)
            {
                sb.AppendLine("!! could not load prefab");
                return;
            }

            try
            {
                ForceRebuild(instance);
                var content = instance.transform.Find("Content");
                if (content == null)
                {
                    sb.AppendLine("!! Content not found");
                    return;
                }

                DumpLayoutTree(content, sb, 0, 2);
                var middle = FindFirst(content, "Middle");
                if (middle == null)
                {
                    sb.AppendLine("!! Middle not found");
                    return;
                }

                sb.AppendLine();
                sb.AppendLine("grid rows of Middle:");
                for (int i = 0; i < middle.childCount; i++)
                    sb.Append("  [").Append(i.ToString("D2")).Append("] ").AppendLine(DescribeRect(middle.GetChild(i)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            sb.AppendLine();
        }
        private static void DumpPopupInjection(StringBuilder sb)
        {
            Section(sb, "sort popup after injecting the release date row", PopupAsset);
            var instance = Instantiate(PopupAsset);
            if (instance == null)
            {
                sb.AppendLine("!! could not load prefab");
                return;
            }

            try
            {
                var popup = instance.GetComponent<MDPro3.UI.Popup.PopupSearchOrder>();
                if (popup == null)
                {
                    sb.AppendLine("!! the prefab has no PopupSearchOrder component");
                    return;
                }

                var feature = new ReleaseDateSortFeature();
                bool injected = SearchOrderRowInjector.Inject(feature, popup);
                sb.AppendLine("inject returned: " + injected);
                sb.AppendLine("inject again returned: " + SearchOrderRowInjector.Inject(feature, popup));

                ForceRebuild(instance);

                var sortOrderField = typeof(MDPro3.UI.CardCollectionView).GetField("_SortOrder",
                    BindingFlags.Public | BindingFlags.Static);
                sb.AppendLine("CardCollectionView._SortOrder: " + (sortOrderField == null
                    ? "field not found" : sortOrderField.GetValue(null).ToString()));
                sb.AppendLine("invalid sort order sentinel: " + ReleaseDateSortFeature.InvalidSortOrder.ToString());
                sb.AppendLine("label shown: " + ReleaseDateSortLabels.Label);

                var middle = FindFirst(instance.transform, "Middle");
                if (middle == null)
                {
                    sb.AppendLine("!! Middle element not found");
                    return;
                }

                sb.AppendLine("Middle: " + DescribeRect(middle));
                var layoutElement = middle.GetComponent<LayoutElement>();
                if (layoutElement != null)
                    sb.AppendLine("Middle LayoutElement: ignoreLayout=" + layoutElement.ignoreLayout
                        + " minHeight=" + layoutElement.minHeight
                        + " preferredHeight=" + layoutElement.preferredHeight
                        + " priority=" + layoutElement.layoutPriority);

                foreach (string areaName in new[] { "TitleArea", "FooterButtonArea" })
                {
                    var area = FindFirst(instance.transform, areaName);
                    if (area != null)
                        sb.AppendLine(areaName + ": " + DescribeRect(area));
                }

                sb.AppendLine("grid children of Middle:");
                for (int i = 0; i < middle.childCount; i++)
                    sb.Append("  [").Append(i.ToString("D2")).Append("] ").AppendLine(DescribeRect(middle.GetChild(i)));

                DumpRow(middle.Find(SearchOrderRowInjector.AscRowName), "ascending (oldest first)", sb);
                DumpRow(middle.Find(SearchOrderRowInjector.DescRowName), "descending (newest first)", sb);

                sb.AppendLine();
                sb.AppendLine("navigation of the grid rows (target names):");
                for (int i = 0; i < middle.childCount; i++)
                {
                    var child = middle.GetChild(i);
                    var selectable = child.GetComponent<Selectable>();
                    if (selectable == null)
                        continue;
                    sb.Append("  ").Append(child.name).Append(" -> ").AppendLine(DescribeNavigation(selectable.navigation));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            sb.AppendLine();
        }

        private static void DumpRow(Transform row, string title, StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("row " + title + ": " + (row == null ? "!! missing" : row.name));
            if (row == null)
                return;

            var toggle = row.GetComponent<ReleaseDateSortToggle>();
            if (toggle == null)
            {
                sb.AppendLine("  !! ReleaseDateSortToggle component missing");
                return;
            }

            var gameBehaviour = row.GetComponent<MDPro3.UI.SelectionToggle_SearchOrder>();
            var iconSprite = toggle.GetIconSprite();
            sb.AppendLine("  direction: " + toggle.Direction);
            sb.AppendLine("  icon sprite: " + (iconSprite == null ? "null" : iconSprite.name));
            sb.AppendLine("  game behaviour left on the row: " + (gameBehaviour != null ? gameBehaviour.GetType().Name : "none"));
            sb.AppendLine("  isOn: " + toggle.isOn + ", exclusiveToggle: " + GetField(toggle, "exclusiveToggle"));
            sb.AppendLine("  components: " + DescribeComponents(row.gameObject));
            sb.AppendLine("  " + DescribeRect(row));
            for (int i = 0; i < row.childCount; i++)
                sb.Append("    ").AppendLine(DescribeRect(row.GetChild(i)));
        }

        private static string DescribeNavigation(Navigation navigation)
        {
            return "mode=" + navigation.mode
                + " up=" + NameOf(navigation.selectOnUp)
                + " down=" + NameOf(navigation.selectOnDown)
                + " left=" + NameOf(navigation.selectOnLeft)
                + " right=" + NameOf(navigation.selectOnRight);
        }

        private static string NameOf(Selectable selectable)
        {
            return selectable == null ? "null" : selectable.gameObject.name;
        }

        private static object GetField(object target, string name)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(target);
                type = type.BaseType;
            }
            return null;
        }

        private static void DumpSubtree(string assetPath, string rootName, StringBuilder sb)
        {
            Section(sb, "deck editor " + rootName + " area", assetPath);
            var instance = Instantiate(assetPath);
            if (instance == null)
            {
                sb.AppendLine("!! could not load prefab");
                return;
            }

            try
            {
                var subtree = FindFirst(instance.transform, rootName);
                if (subtree == null)
                {
                    sb.AppendLine("!! subtree not found");
                    return;
                }

                ForceRebuild(instance);
                DumpTransform(subtree, sb, 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            sb.AppendLine();
        }

        private static void Section(StringBuilder sb, string title, string assetPath)
        {
            sb.AppendLine("======================================================================");
            sb.AppendLine(title);
            sb.AppendLine("asset: " + assetPath);
            sb.AppendLine("======================================================================");
        }

        private static void ForceRebuild(GameObject root)
        {
            var editorRoot = new GameObject("layoutRoot", typeof(RectTransform));
            var canvas = editorRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            editorRoot.GetComponent<RectTransform>().sizeDelta = new Vector2(1920f, 1080f);
            root.transform.SetParent(editorRoot.transform, false);

            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            root.transform.SetParent(null, false);
            UnityEngine.Object.DestroyImmediate(editorRoot);
        }

        private static void DumpLayoutTree(Transform t, StringBuilder sb, int depth, int maxDepth)
        {
            string indent = new string(' ', depth * 2);
            sb.Append(indent).Append(DescribeRect(t)).AppendLine();

            if (depth >= maxDepth)
                return;

            for (int i = 0; i < t.childCount; i++)
                DumpLayoutTree(t.GetChild(i), sb, depth + 1, maxDepth);
        }

        private static string DescribeRect(Transform t)
        {
            var go = t.gameObject;
            var sb = new StringBuilder(go.name);
            if (!go.activeSelf)
                sb.Append(" [inactive]");
            if (!go.activeInHierarchy)
                sb.Append(" [inactive-hierarchy]");

            string label = ReadElementLabel(go);
            if (!string.IsNullOrEmpty(label))
                sb.Append(" label=").Append(label);

            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                sb.Append(" pos=").Append(Format(rect.anchoredPosition))
                  .Append(" size=").Append(Format(rect.sizeDelta))
                  .Append(" rect=").Append(Format(rect.rect.size));
            }

            var text = go.GetComponent<TextMeshProUGUI>();
            if (text != null)
                sb.Append(" text=").Append(text.text);

            var image = go.GetComponent<Image>();
            if (image != null)
                sb.Append(" sprite=").Append(image.sprite == null ? "null" : image.sprite.name);

            return sb.ToString();
        }

        private static string DescribeComponents(GameObject go)
        {
            var names = new List<string>();
            foreach (var component in go.GetComponents<Component>())
                names.Add(component == null ? "(missing)" : component.GetType().Name);
            return string.Join(", ", names);
        }

        private static Transform FindFirst(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        private static void DumpTransform(Transform t, StringBuilder sb, int depth)
        {
            string indent = new string(' ', depth * 2);
            var go = t.gameObject;

            sb.Append(indent).Append(go.name);
            if (!go.activeSelf)
                sb.Append("  [inactive-self]");
            if (!go.activeInHierarchy)
                sb.Append("  [inactive-hierarchy]");

            string label = ReadElementLabel(go);
            if (!string.IsNullOrEmpty(label))
                sb.Append("  label=").Append(label);

            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                sb.Append("  rect[pos=").Append(Format(rect.anchoredPosition))
                  .Append(" size=").Append(Format(rect.sizeDelta))
                  .Append(" anchor=").Append(Format(rect.anchorMin)).Append("-").Append(Format(rect.anchorMax))
                  .Append("]");
            }
            sb.AppendLine();

            DumpComponents(go, sb, depth + 1);

            for (int i = 0; i < t.childCount; i++)
                DumpTransform(t.GetChild(i), sb, depth + 1);
        }

        private static void DumpComponents(GameObject go, StringBuilder sb, int depth)
        {
            string indent = new string(' ', depth * 2);
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                {
                    sb.Append(indent).AppendLine("- (missing script)");
                    continue;
                }

                var type = component.GetType();
                string typeName = type.Name;
                sb.Append(indent).Append("- ").Append(typeName);

                var behaviour = component as Behaviour;
                if (behaviour != null && !behaviour.enabled)
                    sb.Append(" (disabled)");

                var text = component as TextMeshProUGUI;
                if (text != null)
                {
                    sb.Append(" text=").Append(text.text)
                      .Append(" size=").Append(text.fontSize)
                      .Append(" align=").Append(text.alignment)
                      .Append(" color=").Append(text.color)
                      .Append(" font=").Append(text.font == null ? "null" : text.font.name);
                }

                var image = component as Image;
                if (image != null)
                {
                    sb.Append(" sprite=").Append(image.sprite == null ? "null" : image.sprite.name)
                      .Append(" color=").Append(image.color)
                      .Append(" type=").Append(image.type)
                      .Append(" raycast=").Append(image.raycastTarget);
                }

                var rawImage = component as RawImage;
                if (rawImage != null)
                    sb.Append(" color=").Append(rawImage.color);

                var selectable = component as Selectable;
                if (selectable != null)
                    sb.Append(" interactable=").Append(selectable.interactable);

                sb.AppendLine();

                if (typeName.StartsWith("PropertyOverrider", StringComparison.Ordinal)
                    || typeName.StartsWith("OverrideProperty", StringComparison.Ordinal))
                    DumpFields(component, sb, depth + 1, 2);
            }
        }

        private static void DumpFields(object target, StringBuilder sb, int depth, int remainingDepth)
        {
            if (target == null || remainingDepth < 0)
                return;

            string indent = new string(' ', depth * 2);
            var type = target.GetType();
            if (IsSimple(type))
            {
                sb.Append(indent).AppendLine(FormatValue(target));
                return;
            }

            while (type != null && type != typeof(object) && type != typeof(MonoBehaviour) && type != typeof(Behaviour))
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    if (field.IsStatic)
                        continue;

                    object value;
                    try { value = field.GetValue(target); }
                    catch { continue; }

                    if (value != null && !IsSimple(value.GetType()))
                    {
                        sb.Append(indent).Append(field.Name).AppendLine(":");
                        DumpFields(value, sb, depth + 1, remainingDepth - 1);
                        continue;
                    }

                    sb.Append(indent).Append(field.Name).Append(" = ").AppendLine(FormatValue(value));
                }
                type = type.BaseType;
            }
        }

        private static bool IsSimple(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)
                || type == typeof(Color) || type == typeof(Rect);
        }

        private static string ReadElementLabel(GameObject go)
        {
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                    continue;
                var field = component.GetType().GetField("label", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(string))
                    continue;
                return (string)field.GetValue(component);
            }
            return null;
        }

        private static string Format(Vector2 value)
        {
            return "(" + value.x.ToString("0.##") + "," + value.y.ToString("0.##") + ")";
        }

        private static string FormatValue(object value)
        {
            if (value == null)
                return "null";
            if (value is string s)
                return s;
            if (value is UnityEngine.Object unityObject)
                return unityObject == null ? "null" : unityObject.name + " (" + unityObject.GetType().Name + ")";
            if (value is System.Collections.IEnumerable enumerable)
            {
                var parts = new List<string>();
                foreach (var item in enumerable)
                    parts.Add(FormatValue(item));
                return "[" + string.Join(", ", parts) + "]";
            }
            return value.ToString();
        }
    }
}
#endif
