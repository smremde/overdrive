﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TerWoord.OverDriveStorage.Utilities
{
    /// <summary>
    /// Provides a way to store items in a list of limited size so that the oldest-used items are
    /// the first to be removed from the list when the list passes its maximum size.
    /// </summary>
    /// <typeparam name="keyType">
    /// The type of the keys to be used when getting or setting cached items.
    /// </typeparam>
    /// <typeparam name="valueType">
    /// The type of the values to be stored in the cache.
    /// </typeparam>
    public sealed class SimpleRecentItemCache<TValue> : IDisposable
    {
        // we dont use normal KeyValuePair as it uses property accessors, which is (in this case) bad for performance
        public class KVP
        {
            public ulong Key;
            public TValue Value;
        }

        private readonly LinkedList<KVP> _items = new LinkedList<KVP>();
        private readonly Dictionary<UInt64, LinkedListNode<KVP>> _itemKeys = new Dictionary<UInt64, LinkedListNode<KVP>>(16 * 1024);
        private readonly object _lock = new object();

        public Func<ulong, TValue> OnCacheMiss;

        #region Cache Size

        private uint _cacheCapacity = DefaultCacheSize;
        public const uint DefaultCacheSize = 256;
        public const uint MaxCacheSize = int.MaxValue;
        public const uint MinCacheSize = 2;

        public SimpleRecentItemCache(uint capacity = DefaultCacheSize)
        {
            _cacheCapacity = capacity;
            CheckCacheSize();
        }

        public uint CacheCapacity
        {
            get
            {
                return _cacheCapacity;
            } // get

            set
            {
                if (value > MaxCacheSize)
                    value = MaxCacheSize;

                if (value < MinCacheSize)
                    value = MinCacheSize;

                if (value < _cacheCapacity)
                {
                    throw new Exception("Shrinking not yet supported!");
                }

                lock (_lock)
                {
                    _cacheCapacity = value;
                    CheckCacheSize();
                } // lock
            } // set
        }

        public uint CacheCount
        {
            get
            {
                lock (_lock)
                {
                    return (uint)_items.Count;
                }
            }
        }

        // property

        /// <summary>
        /// Removes items from the bottom of the list until the list length matches the cache size.
        /// </summary>
        private void CheckCacheSize()
        {
            lock (_lock)
            {
                while (_items.Count > _cacheCapacity)
                {
                    var xItem = _items.Last;
                    if (ItemRemovedFromCache != null)
                    {
                        ItemRemovedFromCache(xItem.Value.Key, xItem.Value.Value);
                    }
                    _items.RemoveLast();
                    _itemKeys.Remove(xItem.Value.Key);
                }
            }
        } // function

        #endregion Cache Size

        public Action<ulong, TValue> ItemRemovedFromCache;

        /// <summary>
        /// Gets or sets values by index. If the index already exists in the collection, the
        /// existing value will be replaced by the new value.
        /// </summary>
        /// <param name="index">The index to use when getting or setting the value</param>
        /// <returns>The value which is matched to the given index.</returns>
        /// <exception cref="IndexOutOfRangeException">
        /// The index does not exist in the dictionary.
        /// </exception>
        /// <remarks>
        /// When getting a value, the retrieved value will be placed at the top of the list. When
        /// setting a value, the newly set value will also be placed at the top of the list.
        /// After a value is set (or the cache size is changed), items will be removed from the
        /// bottom of the list until the list length matches the cache size.
        /// </remarks>
        public TValue this[ulong index]
        {
            get
            {
                TValue result;
                lock (_lock)
                {
                    if (TryGetValue(index, out result))
                    {
                        return result;
                    }
                    else
                    {
                        result = OnCacheMiss(index);
                        InsertNewItem(index, result);
                        return result;
                    } // if
                }
            } // get

            set
            {
                lock (_lock)
                {
                    LinkedListNode<KVP> xItem;
                    if (_itemKeys.TryGetValue(index, out xItem))
                    {
                        xItem.Value.Value = value;
                        if (xItem.Previous != null)
                        {
                            _items.Remove(xItem);
                            _items.AddFirst(xItem);
                        }
                    }
                    else
                    {
                        InsertNewItem(index, value);
                    }
                    //Debug.WriteLine("Insertng '{0}'", index);
                } // lock
            } // set
        }

        private void InsertNewItem(ulong index, TValue value)
        {
            var xItem = new KVP();
            xItem.Key = index;
            xItem.Value = value;
            var xNode = new LinkedListNode<KVP>(xItem);
            _items.AddFirst(xItem);
            _itemKeys.Add(index, xNode);
            CheckCacheSize();
        }

        public bool TryGetValue(ulong index, out TValue value)
        {
            lock (_lock)
            {
                LinkedListNode<KVP> xItem;
                if (_itemKeys.TryGetValue(index, out xItem))
                {
                    value = xItem.Value.Value;
                    if (xItem.Previous != null)
                    {
                        _items.Remove(xItem);
                        _items.AddFirst(xItem);
                    }
                    return true;
                }
                else
                {
                    value = default(TValue);
                    return false;
                }
            } // lock
        }

        // function

        private bool mDisposed = false;

        public void Dispose()
        {
            if (mDisposed)
            {
                throw new ObjectDisposedException("RecentItemCacheLongKey");
            }
            mDisposed = true;
            GC.SuppressFinalize(this);
            if (ItemRemovedFromCache == null)
            {
                throw new Exception("No ItemRemovedFromCache method specified!");
            }
            Clear();
        }

        //public IEnumerable<KVP> GetAllCachedItemsWithoutTouchingThem()
        //{
        //    lock (_lock)
        //    {
        //        return _items.ToArray();
        //    }
        //}

        public void Clear()
        {
            lock (_lock)
            {
                if (ItemRemovedFromCache == null)
                {
                    _items.Clear();
                    _itemKeys.Clear();
                    return;
                }
                var xItem = _items.First;
                while (xItem != null)
                {
                    ItemRemovedFromCache(xItem.Value.Key, xItem.Value.Value);
                    xItem = xItem.Next;
                }
                _items.Clear();
                _itemKeys.Clear();
            }
        }
    } // class
}