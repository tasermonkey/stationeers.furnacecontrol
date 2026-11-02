using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace FurnaceControlMod
{
    // Makes the board from the Hash Display board before the game loads its prefabs, as Fast Vent makes its
    // vents. The clone keeps that board's model, console fit and setup page; its component is swapped for
    // FurnaceControlBoard, and its main screen is emptied for the board's own pages. Of the game's circuitboards,
    // the Hash Display's setup page is the plainest: a title and the device list, at its tallest (the others
    // give some of its height to Lock/Mode or pressure buttons).
    [HarmonyPatch(typeof(Prefab), "LoadAll")]
    static class PrefabFactory
    {
        const string SourceName = "CircuitboardHashDisplay";
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        static bool _done;
        static readonly HashSet<GameObject> _orphans = new HashSet<GameObject>(); // buttons whose only job was the source board's

        static void Prefix()
        {
            if (_done || WorldManager.Instance == null) return;
            _done = true;
            try { Build(); }
            catch (Exception e) { Debug.LogError("[FurnaceControl] could not create the board: " + e); }
        }

        static void Build()
        {
            var source = WorldManager.Instance.SourcePrefabs.FirstOrDefault(thing => thing != null && thing.PrefabName == SourceName) as Circuitboard;
            if (source == null)
            {
                Debug.LogError($"[FurnaceControl] {SourceName} not found - did the game rename it? No board.");
                return;
            }
            Debug.Log("[FurnaceControl] " + SourceName + ":\n" + Dump(source.transform));

            var holder = new GameObject("~FurnaceControlPrefabs");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var clone = Object.Instantiate(source, holder.transform);
            clone.gameObject.name = FurnaceControlBoard.BoardPrefabName;
            var board = Swap(clone);
            board.PrefabName = FurnaceControlBoard.BoardPrefabName;
            board.PrefabHash = Animator.StringToHash(FurnaceControlBoard.BoardPrefabName);
            FixOwners(board);
            FurnaceScreens.EmptyMainScreen(board);
            // The setup page's title: a key with no translation shows as itself (LocalizedText.Refresh)
            foreach (var title in board.Screens[1].GetComponentsInChildren<LocalizedText>(true))
                title.StringKey = "Furnace Setup"; // "Furnace Control Setup" is too wide for it
            // The setup page keeps its device list, but not buttons for the source board's own settings
            foreach (var orphan in _orphans)
                if (orphan != null && orphan.transform.IsChildOf(board.Screens[1].transform))
                {
                    Debug.Log($"[FurnaceControl] removed {orphan.name} from the setup page");
                    Object.DestroyImmediate(orphan);
                }

            WorldManager.Instance.SourcePrefabs.Add(board);
            Debug.Log("[FurnaceControl] prefab created: " + board.PrefabName + "\n" + Dump(board.transform));
        }

        // Puts a FurnaceControlBoard in the old board's place, with every field the two share.
        static FurnaceControlBoard Swap(Circuitboard old)
        {
            var board = old.gameObject.AddComponent<FurnaceControlBoard>();
            for (var type = typeof(Circuitboard); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var field in type.GetFields(Fields))
                    if (!field.IsLiteral && !field.IsInitOnly)
                        field.SetValue(board, field.GetValue(old));
            Retarget(old, board);
            Object.DestroyImmediate(old);
            return board;
        }

        // Points every reference to the old board, and every button wired to it, at the new one.
        static void Retarget(Component old, FurnaceControlBoard board)
        {
            foreach (var component in old.GetComponentsInChildren<Component>(true))
            {
                if (component == null || ReferenceEquals(component, old)) continue;
                for (var type = component.GetType(); type != null && type != typeof(Component); type = type.BaseType)
                    foreach (var field in type.GetFields(Fields))
                    {
                        if (field.IsLiteral || field.IsInitOnly) continue;
                        if (typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                        {
                            if (field.GetValue(component) is UnityEventBase unityEvent) RetargetEvent(unityEvent, old, board, component);
                        }
                        else if (field.FieldType.IsAssignableFrom(typeof(FurnaceControlBoard))
                                 && ReferenceEquals(field.GetValue(component), old))
                            field.SetValue(component, board);
                    }
            }
        }

        // A button's persistent listener names a method on the old board. One the new board has (ButtonSearch,
        // ButtonUp…) is pointed at it; one it hasn't (the source board's own) is dropped.
        static void RetargetEvent(UnityEventBase unityEvent, Component old, FurnaceControlBoard board, Component owner)
        {
            var group = typeof(UnityEventBase).GetField("m_PersistentCalls", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(unityEvent);
            if (!(group?.GetType().GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(group) is IList calls)) return;
            var changed = false;
            for (var i = calls.Count - 1; i >= 0; i--)
            {
                var call = calls[i];
                var callType = call.GetType();
                var target = callType.GetField("m_Target", BindingFlags.Instance | BindingFlags.NonPublic);
                if (target == null || !ReferenceEquals(target.GetValue(call), old)) continue;
                var methodName = callType.GetField("m_MethodName", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(call) as string;
                var method = methodName == null ? null : typeof(FurnaceControlBoard).GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(m => m.Name == methodName);
                changed = true;
                if (method == null)
                {
                    calls.RemoveAt(i);
                    if (calls.Count == 0) _orphans.Add(owner.gameObject);
                    Debug.Log($"[FurnaceControl] dropped {owner.name}'s listener {methodName}");
                    continue;
                }
                target.SetValue(call, board);
                callType.GetField("m_TargetAssemblyTypeName", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(call, method.DeclaringType.AssemblyQualifiedName);
                Debug.Log($"[FurnaceControl] {owner.name}'s listener {methodName} -> {method.DeclaringType.Name}");
            }
            if (changed)
                typeof(UnityEventBase).GetMethod("DirtyPersistentCalls", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(unityEvent, null);
        }

        // Points the clone's parts at the clone rather than the thing it was copied from (Fast Vent's).
        static void FixOwners(Thing thing)
        {
            if (thing.Slots != null)
                foreach (var slot in thing.Slots)
                    if (slot != null) slot.Parent = thing;
            if (thing.Renderers != null)
                foreach (var renderer in thing.Renderers)
                    if (renderer != null) renderer.Parent = thing;
            if (thing.AudioEvents != null)
                foreach (var audio in thing.AudioEvents)
                    if (audio != null) audio.Parent = thing;
            if (thing.Interactables == null) return;
            foreach (var interactable in thing.Interactables)
            {
                if (interactable == null) continue;
                interactable.Parent = thing;
                if (interactable.AssociatedAudioEvents != null)
                    foreach (var audio in interactable.AssociatedAudioEvents)
                        if (audio != null) audio.Parent = thing;
            }
        }

        // The prefab's objects and components, for the log: what the screens are made of.
        static string Dump(Transform root)
        {
            var text = new StringBuilder();
            void Walk(Transform transform, int depth)
            {
                text.Append(' ', depth * 2).Append(transform.name).Append(transform.gameObject.activeSelf ? "" : " (off)");
                if (transform is RectTransform rect)
                    text.Append($" [{rect.anchorMin}-{rect.anchorMax} {rect.rect.size}]");
                text.Append(": ").AppendLine(string.Join(", ", transform.GetComponents<Component>()
                    .Where(component => component != null && !(component is Transform)).Select(component => component.GetType().Name)));
                foreach (Transform child in transform) Walk(child, depth + 1);
            }
            Walk(root, 0);
            return text.ToString();
        }
    }
}
