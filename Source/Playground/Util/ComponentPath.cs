// Parsing component references like `Root/Child:2@SpriteRenderer:1`.
//
// A reference points at a GameObject by its Transform-hierarchy path and,
// optionally, one of its components. Neither path segments nor components are
// unique, so any field may carry a `:<index>` to disambiguate among equally
// matching siblings/components (0-based). The structural characters `/`, `@`,
// `:` and `\` can be escaped with a backslash to use them literally in a name.
//
// Grammar:
//   path     := segment ('/' segment)* ('@' selector)?
//   segment  := name (':' index)?
//   selector := name (':' index)?
//
// This is a port of rabex-cli's `src/component_path.rs`, extended with Unity
// scene resolution (ResolveGameObject / ResolveComponent / GetPath). Kept in
// sync with DevUtils' copy in the Silksong tooling.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

/// A name plus an optional index disambiguating among equal-named matches.
public sealed class Field(string name, int? index = null) {
    public readonly int? Index = index;
    public readonly string Name = name;

    /// Inverse of parsing: escapes structural characters so the output round-trips.
    public override string ToString() {
        var name = ComponentPath.Escape(Name);
        return Index is { } i ? $"{name}:{i.ToString(CultureInfo.InvariantCulture)}" : name;
    }

    public override bool Equals(object? obj) {
        return obj is Field f && f.Name == Name && f.Index == Index;
    }

    public override int GetHashCode() {
        return HashCode.Combine(Name, Index);
    }
}

/// A reference to a GameObject (by hierarchy path) and optionally a component.
public sealed class ComponentPath(IReadOnlyList<Field> segments, Field? component) {
    /// Component selector (`@Type[:index]`), if any.
    public readonly Field? Component = component;

    /// Hierarchy path, root first; always at least one segment.
    public readonly IReadOnlyList<Field> Segments = segments;

    /// `ToString` is the inverse of `Parse`: it escapes structural characters so
    /// the output round-trips back through `Parse`.
    public override string ToString() {
        var sb = new StringBuilder();
        for (var i = 0; i < Segments.Count; i++) {
            if (i > 0) sb.Append('/');
            sb.Append(Segments[i]);
        }

        if (Component != null) {
            sb.Append('@');
            sb.Append(Component);
        }

        return sb.ToString();
    }

    public override bool Equals(object? obj) {
        return obj is ComponentPath p && Segments.SequenceEqual(p.Segments) && Equals(Component, p.Component);
    }

    public override int GetHashCode() {
        return HashCode.Combine(Segments.Count, Component);
    }

    /// Parse a [ComponentPath]. Throws [FormatException] on malformed input.
    public static ComponentPath Parse(string input) {
        // The component selector is everything after the first unescaped '@'.
        var at = SplitKeepEscapes(input, '@');
        string pathPart;
        Field? component;
        switch (at.Count) {
            case 1:
                pathPart = at[0];
                component = null;
                break;
            case 2:
                pathPart = at[0];
                component = ParseField(at[1], "component");
                break;
            default:
                throw new FormatException("at most one '@' component selector is allowed");
        }

        var segments = SplitKeepEscapes(pathPart, '/').Select(seg => ParseField(seg, "path segment")).ToList();
        if (segments.Any(s => s.Name.Length == 0)) throw new FormatException("empty path segment");

        return new ComponentPath(segments, component);
    }

    private static Field ParseField(string raw, string what) {
        var parts = SplitKeepEscapes(raw, ':');
        switch (parts.Count) {
            case 1:
                return new Field(Unescape(parts[0]));
            case 2:
                if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    throw new FormatException($"invalid index ':{parts[1]}' (expected a number)");
                return new Field(Unescape(parts[0]), index);
            default:
                throw new FormatException($"at most one ':index' is allowed per {what}");
        }
    }

    /// Split on unescaped `delim`, leaving any other `\x` escapes intact in the
    /// pieces (so a later split on a different delimiter still sees them escaped).
    private static List<string> SplitKeepEscapes(string s, char delim) {
        var parts = new List<string>();
        var cur = new StringBuilder();
        for (var i = 0; i < s.Length; i++) {
            var c = s[i];
            if (c == '\\') {
                cur.Append('\\');
                if (i + 1 < s.Length) cur.Append(s[++i]);
            }
            else if (c == delim) {
                parts.Add(cur.ToString());
                cur.Clear();
            }
            else {
                cur.Append(c);
            }
        }

        parts.Add(cur.ToString());
        return parts;
    }

    /// Backslash-escape the structural characters so a name round-trips.
    internal static string Escape(string name) {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) {
            if (c is '\\' or '/' or '@' or ':') sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// Remove escaping backslashes, yielding the literal name.
    private static string Unescape(string s) {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
            if (s[i] == '\\') {
                if (i + 1 < s.Length) sb.Append(s[++i]);
            }
            else {
                sb.Append(s[i]);
            }

        return sb.ToString();
    }

    /// Find the first GameObject whose hierarchy matches these segments. A
    /// segment without an index matches any equal-named sibling; with an index
    /// it must be that 0-based occurrence among same-named siblings.
    public GameObject? ResolveGameObject() {
        return AllGameObjects().FirstOrDefault(Matches);
    }

    /// Resolve this path's component selector on `go` (null if no selector).
    public Component? ResolveComponent(GameObject go) {
        return Component != null ? FindComponent(go, Component) : null;
    }

    /// Find a component on `go` by type name, picking the selector's 0-based
    /// occurrence among same-typed components (defaulting to the first).
    public static Component? FindComponent(GameObject go, Field selector) {
        return go.GetComponents<Component>()
            .Where(c => c != null && c.GetType().Name == selector.Name)
            .ElementAtOrDefault(selector.Index ?? 0);
    }

    private bool Matches(GameObject go) {
        var chain = new List<GameObject>();
        for (var t = go.transform; t != null; t = t.parent) chain.Add(t.gameObject);
        chain.Reverse();

        if (chain.Count != Segments.Count) return false;
        for (var i = 0; i < Segments.Count; i++) {
            var seg = Segments[i];
            var node = chain[i];
            if (node.name != seg.Name) return false;
            if (seg.Index is { } idx && SiblingIndex(node) != idx) return false;
        }

        return true;
    }

    /// Canonical, round-trippable path of `go`: names are escaped, and a
    /// `:index` is added only where a same-named sibling makes it ambiguous.
    public static string GetPath(GameObject go) {
        var fields = new List<Field>();
        for (var t = go.transform; t != null; t = t.parent) {
            var node = t.gameObject;
            int? index = CountSameName(node) > 1 ? SiblingIndex(node) : null;
            fields.Add(new Field(node.name, index));
        }

        fields.Reverse();
        return new ComponentPath(fields, null).ToString();
    }

    public static string GetPath(Transform t) {
        return GetPath(t.gameObject);
    }

    private static IEnumerable<GameObject> AllGameObjects() {
        return Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private static IEnumerable<GameObject> SiblingsOf(GameObject go) {
        var parent = go.transform.parent;
        return parent != null
            ? parent.Cast<Transform>().Select(t => t.gameObject)
            : AllGameObjects().Where(g => g.transform.parent == null);
    }

    private static int SiblingIndex(GameObject go) {
        var index = 0;
        foreach (var sibling in SiblingsOf(go)) {
            if (ReferenceEquals(sibling, go)) return index;
            if (sibling.name == go.name) index++;
        }

        return index;
    }

    private static int CountSameName(GameObject go) {
        return SiblingsOf(go).Count(s => s.name == go.name);
    }
}
