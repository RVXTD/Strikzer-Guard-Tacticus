// Written by Alexander Stangl. Review, bug fix (ExtractMin reverse-index update),
// input guards, Contains/Clear/Count, and documentation comments by Claude
// (Anthropic), reviewed by Alexander Stangl per the course syllabus.

using System;

namespace Game.Utility
{
    /// <summary>
    /// A fixed-capacity min-priority queue over integer element indices in the range
    /// [0, capacity), supporting changes to the priority of any element currently
    /// in the queue.
    /// </summary>
    /// <remarks>
    /// Each element index may be in the queue at most once at a time. Insertion,
    /// extraction of the minimum, and priority changes each take O(log n) time;
    /// membership tests take O(1) time. No allocations occur after construction.
    /// </remarks>
    public class IndexedMinHeap
    {
        // Binary min-heap stored in an array; entries[0] holds the minimum.
        private (float priority, int index)[] entries;

        // Maps an element index to its position in entries, or -1 if the element is
        // not in the queue. Invariant: entries[reverseIndex[i]].index == i for every
        // element i in the queue.
        private int[] reverseIndex;

        /// <summary>
        /// The number of elements currently in the queue.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// The number of elements the queue can hold in total.
        /// </summary>
        public int Capacity => entries.Length;

        private int parent(int i) => (i - 1) / 2;
        private int leftChild(int i) => 2 * i + 1;
        private int rightChild(int i) => 2 * i + 2;

        /// <summary>
        /// Creates an empty queue that can hold element indices in [0, <paramref name="capacity"/>).
        /// </summary>
        /// <param name="capacity">The number of distinct element indices the queue supports.</param>
        public IndexedMinHeap(int capacity)
        {
            entries = new (float priority, int index)[capacity];
            reverseIndex = new int[capacity];
            for (int i = 0; i < capacity; ++i)
                reverseIndex[i] = -1;
            Count = 0;
        }

        /// <summary>
        /// Returns whether the queue contains no elements.
        /// </summary>
        public bool IsEmpty()
        {
            return Count == 0;
        }

        /// <summary>
        /// Returns whether the element <paramref name="index"/> is currently in the queue.
        /// </summary>
        /// <param name="index">An element index in [0, capacity).</param>
        public bool Contains(int index)
        {
            return reverseIndex[index] >= 0;
        }

        /// <summary>
        /// Removes all elements from the queue, leaving it ready for reuse at the same capacity.
        /// </summary>
        public void Clear()
        {
            for (int k = 0; k < Count; ++k)
            {
                reverseIndex[entries[k].index] = -1;
            }
            Count = 0;
        }

        // Swaps two heap slots and keeps reverseIndex consistent with the swap.
        private void swapEntries(int a, int b)
        {
            (entries[a], entries[b]) = (entries[b], entries[a]);
            reverseIndex[entries[a].index] = a;
            reverseIndex[entries[b].index] = b;
        }

        // Moves the entry at heap position j toward the root until heap order holds.
        private void percolateUp(int j)
        {
            while (j > 0 && entries[j].priority < entries[parent(j)].priority)
            {
                swapEntries(j, parent(j));
                j = parent(j);
            }
        }

        // Moves the entry at heap position j toward the leaves until heap order holds.
        private void percolateDown(int j)
        {
            while (leftChild(j) < Count)
            {
                int smallerChild = leftChild(j);
                if (rightChild(j) < Count && entries[rightChild(j)].priority < entries[smallerChild].priority)
                    smallerChild = rightChild(j);
                if (entries[j].priority <= entries[smallerChild].priority)
                    break;
                swapEntries(j, smallerChild);
                j = smallerChild;
            }
        }

        /// <summary>
        /// Removes and returns an element with the smallest priority in the queue.
        /// Ties between equal priorities are broken arbitrarily.
        /// </summary>
        /// <returns>The removed element's priority and index.</returns>
        /// <exception cref="InvalidOperationException">The queue is empty.</exception>
        public void ExtractMin(out float priority, out int index)
        {
            if (Count == 0)
                throw new InvalidOperationException("ExtractMin called on an empty IndexedMinHeap.");

            (priority, index) = entries[0];
            --Count;
            reverseIndex[index] = -1;

            if (Count > 0)
            {
                entries[0] = entries[Count];
                reverseIndex[entries[0].index] = 0;
                percolateDown(0);
            }
        }

        /// <summary>
        /// Inserts the element <paramref name="index"/> with the given priority.
        /// </summary>
        /// <param name="priority">The element's priority; smaller values are extracted first.</param>
        /// <param name="index">An element index in [0, capacity) that is not already in the queue.</param>
        /// <exception cref="ArgumentException">The element is already in the queue.</exception>
        public void AddWithPriority(float priority, int index)
        {
            if (reverseIndex[index] >= 0)
                throw new ArgumentException($"Element {index} is already in the IndexedMinHeap.", nameof(index));

            entries[Count] = (priority, index);
            reverseIndex[index] = Count;
            percolateUp(Count);
            ++Count;
        }

        /// <summary>
        /// Sets the priority of the element <paramref name="index"/>, which may be higher or
        /// lower than its current priority.
        /// </summary>
        /// <param name="priority">The element's new priority.</param>
        /// <param name="index">An element index currently in the queue.</param>
        /// <exception cref="ArgumentException">The element is not in the queue.</exception>
        public void ChangePriority(float priority, int index)
        {
            int j = reverseIndex[index];
            if (j < 0)
                throw new ArgumentException($"Element {index} is not in the IndexedMinHeap.", nameof(index));

            float oldPriority = entries[j].priority;
            entries[j].priority = priority;
            if (priority < oldPriority)
                percolateUp(j);
            else
                percolateDown(j);
        }
    }
}
