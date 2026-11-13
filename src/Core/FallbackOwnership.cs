using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProbablyFonts.Core
{
    internal static class FallbackPlanner
    {
        public static void BuildOwnedOrder<T>(
            IEnumerable<T> existingRoots,
            IEnumerable<T> proposedCandidates,
            Func<T, IEnumerable<T>> getFallbacks,
            List<T> output,
            IEqualityComparer<T> comparer) where T : class
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (comparer == null) comparer = ReferenceEqualityComparer<T>.Instance;

            HashSet<T> reachable = new HashSet<T>(comparer);
            List<T> traversal = new List<T>(8);
            MarkReachable(existingRoots, getFallbacks, reachable, traversal);
            if (proposedCandidates == null) return;

            foreach (T candidate in proposedCandidates)
            {
                if (candidate == null || reachable.Contains(candidate)) continue;
                if (HasCycle(candidate, getFallbacks, comparer)) continue;

                output.Add(candidate);
                // Owned global copies have no local fallback graph; only this root becomes reachable.
                reachable.Add(candidate);
            }
        }

        private static bool HasCycle<T>(T root, Func<T, IEnumerable<T>> getFallbacks, IEqualityComparer<T> comparer)
            where T : class
        {
            if (getFallbacks == null) return false;

            HashSet<T> active = new HashSet<T>(comparer);
            HashSet<T> completed = new HashSet<T>(comparer);
            List<Visit<T>> stack = new List<Visit<T>>(8) { new Visit<T>(root, false) };

            while (stack.Count != 0)
            {
                Visit<T> visit = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);

                if (visit.Exiting)
                {
                    active.Remove(visit.Node);
                    completed.Add(visit.Node);
                    continue;
                }

                if (active.Contains(visit.Node)) return true;
                if (completed.Contains(visit.Node)) continue;

                active.Add(visit.Node);
                stack.Add(new Visit<T>(visit.Node, true));
                IEnumerable<T> children = getFallbacks(visit.Node);
                if (children == null) continue;

                foreach (T child in children)
                {
                    if (child == null) continue;
                    if (active.Contains(child)) return true;
                    if (!completed.Contains(child)) stack.Add(new Visit<T>(child, false));
                }
            }

            return false;
        }

        private static void MarkReachable<T>(
            IEnumerable<T> roots,
            Func<T, IEnumerable<T>> getFallbacks,
            HashSet<T> reachable,
            List<T> stack) where T : class
        {
            stack.Clear();
            if (roots != null)
            {
                foreach (T root in roots)
                {
                    if (root != null) stack.Add(root);
                }
            }

            MarkReachableStack(getFallbacks, reachable, stack);
        }


        private static void MarkReachableStack<T>(
            Func<T, IEnumerable<T>> getFallbacks,
            HashSet<T> reachable,
            List<T> stack) where T : class
        {
            while (stack.Count != 0)
            {
                int last = stack.Count - 1;
                T node = stack[last];
                stack.RemoveAt(last);
                if (!reachable.Add(node) || getFallbacks == null) continue;

                IEnumerable<T> children = getFallbacks(node);
                if (children == null) continue;
                foreach (T child in children)
                {
                    if (child != null && !reachable.Contains(child)) stack.Add(child);
                }
            }
        }

        private readonly struct Visit<T>
        {
            public readonly T Node;
            public readonly bool Exiting;

            public Visit(T node, bool exiting)
            {
                Node = node;
                Exiting = exiting;
            }
        }
    }

    internal sealed class OwnedListEntries<T> where T : class
    {
        private readonly List<T> ownedEntries = new List<T>();
        private readonly List<T> mountedEntries = new List<T>();
        private readonly List<List<T>> targets = new List<List<T>>();

        public int Count => ownedEntries.Count;

        public void AddMissing(List<T> target, IEnumerable<T> candidates)
        {
            if (target == null || candidates == null) return;

            foreach (T candidate in candidates)
            {
                if (candidate == null) continue;
                bool owned = ContainsReference(ownedEntries, candidate);
                bool present = ContainsReference(target, candidate);
                if (!owned && present) continue;
                if (!owned) ownedEntries.Add(candidate);
                if (!ContainsReference(mountedEntries, candidate)) mountedEntries.Add(candidate);
                Track(target);
                if (!present) target.Add(candidate);
            }
        }

        public bool HasMissingFrom(List<T> target)
        {
            if (mountedEntries.Count == 0) return false;
            if (target == null) return true;

            for (int entryIndex = 0; entryIndex < mountedEntries.Count; entryIndex++)
            {
                if (!ContainsReference(target, mountedEntries[entryIndex])) return true;
            }

            return false;
        }

        public void Unmount(List<T> target, List<T> currentTarget = null)
        {
            if (ownedEntries.Count != 0)
            {
                Track(target);
                Track(currentTarget);
            }

            RemoveOwnedReferences(target);
            if (!ReferenceEquals(target, currentTarget)) RemoveOwnedReferences(currentTarget);

            for (int index = 0; index < targets.Count; index++)
            {
                List<T> mountedTarget = targets[index];
                if (ReferenceEquals(mountedTarget, target) || ReferenceEquals(mountedTarget, currentTarget)) continue;
                RemoveOwnedReferences(mountedTarget);
            }

            mountedEntries.Clear();
        }

        public void Restore(List<T> target, List<T> currentTarget = null)
        {
            Unmount(target, currentTarget);
            ownedEntries.Clear();
            targets.Clear();
        }

        private void Track(List<T> target)
        {
            if (target == null) return;

            for (int index = 0; index < targets.Count; index++)
            {
                if (ReferenceEquals(targets[index], target)) return;
            }

            targets.Add(target);
        }

        private void RemoveOwnedReferences(List<T> target)
        {
            if (target == null) return;

            for (int entryIndex = ownedEntries.Count - 1; entryIndex >= 0; entryIndex--)
            {
                T owned = ownedEntries[entryIndex];
                for (int listIndex = target.Count - 1; listIndex >= 0; listIndex--)
                {
                    if (ReferenceEquals(target[listIndex], owned)) target.RemoveAt(listIndex);
                }
            }
        }

        private static bool ContainsReference(List<T> list, T value)
        {
            for (int index = 0; index < list.Count; index++)
            {
                if (ReferenceEquals(list[index], value)) return true;
            }

            return false;
        }
    }

    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

        private ReferenceEqualityComparer() { }

        public bool Equals(T x, T y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
