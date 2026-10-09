#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// Editor Tool to clean up gameplay-related components from selected GameObjects.
/// Useful for creating purely visual/preview meshes or local UI models by stripping
/// Network, Physics, and gameplay scripts recursively.
/// </summary>
public class ComponentStripperWindow : EditorWindow
{
    private bool removeNetwork = true;
    private bool removePhysics = true;
    private bool removeGameplayScripts = true;
    private bool includeChildren = true;

    [MenuItem("Tools/Component Stripper")]
    public static void ShowWindow()
    {
        GetWindow<ComponentStripperWindow>("Component Stripper");
    }

    private void OnGUI()
    {
        GUILayout.Label("Strip Selected GameObjects", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        removeNetwork = EditorGUILayout.Toggle("Remove Network Components", removeNetwork);
        removePhysics = EditorGUILayout.Toggle("Remove Physics (Rigidbody/Colliders)", removePhysics);
        removeGameplayScripts = EditorGUILayout.Toggle("Remove Custom Scripts (MonoBehaviours)", removeGameplayScripts);
        includeChildren = EditorGUILayout.Toggle("Include Children (Recursive)", includeChildren);

        EditorGUILayout.Space();

        if (Selection.gameObjects.Length == 0)
        {
            EditorGUILayout.HelpBox("Please select one or more GameObjects in the Hierarchy.", MessageType.Info);
            return;
        }

        if (GUILayout.Button("Strip Selected Components", GUILayout.Height(40)))
        {
            StripSelected();
        }
    }

    private void StripSelected()
    {
        int totalRemoved = 0;
        HashSet<GameObject> targets = new HashSet<GameObject>();

        foreach (GameObject selected in Selection.gameObjects)
        {
            if (selected == null) continue;
            targets.Add(selected);

            if (includeChildren)
            {
                foreach (Transform t in selected.GetComponentsInChildren<Transform>(true))
                {
                    targets.Add(t.gameObject);
                }
            }
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Strip Components");
        int groupIndex = Undo.GetCurrentGroup();

        // We process in passes:
        // 1. Joint/Constraints dependencies (to avoid Unity warnings when destroying rigidbodies)
        // 2. Custom Scripts & Network & Rigidbody / Colliders
        foreach (GameObject go in targets)
        {
            // Pass 1: Remove Joints first (as they depend on rigidbodies)
            if (removePhysics)
            {
                Joint[] joints = go.GetComponents<Joint>();
                foreach (var j in joints)
                {
                    Undo.DestroyObjectImmediate(j);
                    totalRemoved++;
                }
            }

            // Pass 2: Remove specific components
            Component[] components = go.GetComponents<Component>();
            foreach (Component comp in components)
            {
                if (comp == null || comp is Transform) continue;

                bool shouldRemove = false;

                // Check Network (Unity Netcode)
                if (removeNetwork)
                {
                    if (comp is NetworkObject || 
                        comp is NetworkBehaviour || 
                        comp.GetType().FullName.Contains("Unity.Netcode") || 
                        comp.GetType().Name.Contains("NetworkTransform") ||
                        comp.GetType().Name.Contains("ClientNetworkTransform"))
                    {
                        shouldRemove = true;
                    }
                }

                // Check Physics
                if (removePhysics)
                {
                    // Explicitly target Rigidbody, Colliders, Joints, and related physics components
                    if (comp is Rigidbody || comp is Collider || comp is Joint || comp is ConstantForce || comp.GetType().Name == "Rigidbody")
                    {
                        shouldRemove = true;
                    }
                }

                // Check Custom Gameplay Scripts
                if (removeGameplayScripts)
                {
                    // Remove MonoBehaviours that are not Unity built-in UI/Rendering components
                    if (comp is MonoBehaviour && 
                        !(comp is TMPro.TextMeshPro) && 
                        !(comp is TMPro.TextMeshProUGUI) &&
                        !comp.GetType().FullName.StartsWith("UnityEngine.UI") &&
                        !comp.GetType().FullName.StartsWith("UnityEngine.EventSystems") &&
                        !comp.GetType().FullName.StartsWith("UnityEngine.Rendering"))
                    {
                        shouldRemove = true;
                    }
                }

                if (shouldRemove)
                {
                    Undo.DestroyObjectImmediate(comp);
                    totalRemoved++;
                }
            }
        }

        Undo.CollapseUndoOperations(groupIndex);
        Debug.Log($"[ComponentStripper] ✅ Finished stripping components. Removed {totalRemoved} components from {targets.Count} game objects (including children: {includeChildren}).");
    }
}
#endif
