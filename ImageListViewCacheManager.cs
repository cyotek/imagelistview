﻿// ImageListView - A listview control for image files
// Copyright (C) 2009 Ozgur Ozcitak
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Ozgur Ozcitak (ozcitak@yahoo.com)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace Manina.Windows.Forms
{
    /// <summary>
    /// Represents the cache manager responsible for asynchronously loading
    /// item thumbnails.
    /// </summary>
    internal class ImageListViewCacheManager : IDisposable
    {
        #region Member Variables
        private readonly object lockObject;

        private ImageListView mImageListView;
        private Thread mThread;
        private CacheMode mCacheMode;
        private int mCacheLimitAsItemCount;
        private long mCacheLimitAsMemory;
        private bool mRetryOnError;
        private Size mCurrentThumbnailSize;

        private Stack<CacheItem> toCache;
        private Dictionary<Guid, CacheItem> thumbCache;
        private Dictionary<Guid, bool> editCache;

        private Stack<CacheItem> rendererToCache;
        private Guid rendererGuid;
        private CacheItem rendererItem;

        private long memoryUsed;
        private long memoryUsedByRemoved;
        private List<Guid> removedItems;

        private bool stopping;
        private bool stopped;
        private bool disposed;
        #endregion

        #region Private Classes
        /// <summary>
        /// Represents an item in the thumbnail cache.
        /// </summary>
        private class CacheItem : IDisposable
        {
            private Guid mGuid;
            private string mFileName;
            private Size mSize;
            private Image mImage;
            private CacheState mState;
            private UseEmbeddedThumbnails mUseEmbeddedThumbnails;
            private bool mAutoRotate;
            private bool mIsVirtualItem;
            private bool disposed;
            private object mVirtualItemKey;

            /// <summary>
            /// Gets the guid of the item.
            /// </summary>
            public Guid Guid { get { return mGuid; } }
            /// <summary>
            /// Gets the name of the image file.
            /// </summary>
            public string FileName { get { return mFileName; } }
            /// <summary>
            /// Gets the size of the requested thumbnail.
            /// </summary>
            public Size Size { get { return mSize; } }
            /// <summary>
            /// Gets the cached image.
            /// </summary>
            public Image Image { get { return mImage; } }
            /// <summary>
            /// Gets or sets the state of the cache item.
            /// </summary>
            public CacheState State { get { return mState; } set { mState = value; } }
            /// <summary>
            /// Gets embedded thumbnail extraction behavior.
            /// </summary>
            public UseEmbeddedThumbnails UseEmbeddedThumbnails { get { return mUseEmbeddedThumbnails; } }
            /// <summary>
            /// Gets Exif rotation behavior.
            /// </summary>
            public bool AutoRotate { get { return mAutoRotate; } }
            /// <summary>
            /// Gets whether this item represents a virtual ImageListViewItem.
            /// </summary>
            public bool IsVirtualItem { get { return mIsVirtualItem; } }
            /// <summary>
            /// Gets the public key for the virtual item.
            /// </summary>
            public object VirtualItemKey { get { return mVirtualItemKey; } }

            /// <summary>
            /// Initializes a new instance of the CacheItem class
            /// for use with a virtual item.
            /// </summary>
            /// <param name="guid">The guid of the ImageListViewItem.</param>
            /// <param name="key">The public key for the virtual item.</param>
            /// <param name="size">The size of the requested thumbnail.</param>
            /// <param name="image">The thumbnail image.</param>
            /// <param name="state">The cache state of the item.</param>
            public CacheItem(Guid guid, object key, Size size, Image image, CacheState state)
                : this(guid, key, size, image, state, UseEmbeddedThumbnails.Auto, true)
            {
                ;
            }
            /// <summary>
            /// Initializes a new instance of the CacheItem class
            /// for use with a virtual item.
            /// </summary>
            /// <param name="guid">The guid of the ImageListViewItem.</param>
            /// <param name="key">The public key for the virtual item.</param>
            /// <param name="size">The size of the requested thumbnail.</param>
            /// <param name="image">The thumbnail image.</param>
            /// <param name="state">The cache state of the item.</param>
            /// <param name="useEmbeddedThumbnails">UseEmbeddedThumbnails property of the owner control.</param>
            /// <param name="autoRotate">AutoRotate property of the owner control.</param>
            public CacheItem(Guid guid, object key, Size size, Image image, CacheState state, UseEmbeddedThumbnails useEmbeddedThumbnails, bool autoRotate)
            {
                mGuid = guid;
                mVirtualItemKey = key;
                mFileName = string.Empty;
                mSize = size;
                mImage = image;
                mState = state;
                mUseEmbeddedThumbnails = useEmbeddedThumbnails;
                mAutoRotate = autoRotate;
                mIsVirtualItem = true;
                disposed = false;
            }
            /// <summary>
            /// Initializes a new instance of the CacheItem class.
            /// </summary>
            /// <param name="guid">The guid of the ImageListViewItem.</param>
            /// <param name="filename">The file system path to the image file.</param>
            /// <param name="size">The size of the requested thumbnail.</param>
            /// <param name="image">The thumbnail image.</param>
            /// <param name="state">The cache state of the item.</param>
            public CacheItem(Guid guid, string filename, Size size, Image image, CacheState state)
                : this(guid, filename, size, image, state, UseEmbeddedThumbnails.Auto, true)
            {
                ;
            }
            /// <summary>
            /// Initializes a new instance of the CacheItem class.
            /// </summary>
            /// <param name="guid">The guid of the ImageListViewItem.</param>
            /// <param name="filename">The file system path to the image file.</param>
            /// <param name="size">The size of the requested thumbnail.</param>
            /// <param name="image">The thumbnail image.</param>
            /// <param name="state">The cache state of the item.</param>
            /// <param name="useEmbeddedThumbnails">UseEmbeddedThumbnails property of the owner control.</param>
            /// <param name="autoRotate">AutoRotate property of the owner control.</param>
            public CacheItem(Guid guid, string filename, Size size, Image image, CacheState state, UseEmbeddedThumbnails useEmbeddedThumbnails, bool autoRotate)
            {
                mGuid = guid;
                mFileName = filename;
                mSize = size;
                mImage = image;
                mState = state;
                mUseEmbeddedThumbnails = useEmbeddedThumbnails;
                mAutoRotate = autoRotate;
                mIsVirtualItem = false;
                disposed = false;
            }

            /// <summary>
            /// Performs application-defined tasks associated with 
            /// freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            public void Dispose()
            {
                if (!disposed)
                {
                    if (mImage != null)
                    {
                        mImage.Dispose();
                        mImage = null;
                    }

                    disposed = true;
                    GC.SuppressFinalize(this);
                }
            }
#if DEBUG
            /// <summary>
            /// Releases unmanaged resources and performs other cleanup operations before the
            /// CacheItem is reclaimed by garbage collection.
            /// </summary>
            ~CacheItem()
            {
                if (mImage != null)
                    System.Diagnostics.Debug.Print("Finalizer of {0} called for non-empty cache item.", GetType());
                Dispose();
            }
#endif
        }
        #endregion

        #region Properties
        /// <summary>
        /// Determines whether the cache manager retries loading items on errors.
        /// </summary>
        public bool RetryOnError { get { return mRetryOnError; } set { mRetryOnError = value; } }
        /// <summary>
        /// Determines whether the cache thread is being stopped.
        /// </summary>
        private bool Stopping { get { lock (lockObject) { return stopping; } } }
        /// <summary>
        /// Determines whether the cache thread is stopped.
        /// </summary>
        public bool Stopped { get { lock (lockObject) { return stopped; } } }
        /// <summary>
        /// Gets or sets the cache mode.
        /// </summary>
        public CacheMode CacheMode
        {
            get { return mCacheMode; }
            set
            {
                lock (lockObject)
                {
                    mCacheMode = value;
                    if (mCacheMode == CacheMode.Continuous)
                    {
                        mCacheLimitAsItemCount = 0;
                        mCacheLimitAsMemory = 0;
                    }
                }
            }
        }
        /// <summary>
        /// Gets or sets the cache limit as count of items.
        /// </summary>
        public int CacheLimitAsItemCount
        {
            get { return mCacheLimitAsItemCount; }
            set { lock (lockObject) { mCacheLimitAsItemCount = value; mCacheLimitAsMemory = 0; mCacheMode = CacheMode.OnDemand; } }
        }
        /// <summary>
        /// Gets or sets the cache limit as allocated memory in MB.
        /// </summary>
        public long CacheLimitAsMemory
        {
            get { return mCacheLimitAsMemory; }
            set { lock (lockObject) { mCacheLimitAsMemory = value; mCacheLimitAsItemCount = 0; mCacheMode = CacheMode.OnDemand; } }
        }
        /// <summary>
        /// Gets the approximate amount of memory used by the cache.
        /// </summary>
        public long MemoryUsed { get { lock (lockObject) { return memoryUsed; } } }
        /// <summary>
        /// Returns the count of items in the cache.
        /// </summary>
        public long CacheSize { get { lock (lockObject) { return thumbCache.Count; } } }
        /// <summary>
        /// Gets or sets the current thumbnail size.
        /// </summary>
        public Size CurrentThumbnailSize
        {
            get { return mCurrentThumbnailSize; }
            set { lock (lockObject) { mCurrentThumbnailSize = value; } }
        }
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the ImageListViewCacheManager class.
        /// </summary>
        /// <param name="owner">The owner control.</param>
        public ImageListViewCacheManager(ImageListView owner)
        {
            lockObject = new object();

            mImageListView = owner;
            mCacheMode = CacheMode.OnDemand;
            mCacheLimitAsItemCount = 0;
            mCacheLimitAsMemory = 20 * 1024 * 1024;
            mRetryOnError = owner.RetryOnError;

            toCache = new Stack<CacheItem>();
            thumbCache = new Dictionary<Guid, CacheItem>();
            editCache = new Dictionary<Guid, bool>();

            rendererToCache = new Stack<CacheItem>();
            rendererGuid = new Guid();
            rendererItem = null;

            memoryUsed = 0;
            memoryUsedByRemoved = 0;
            removedItems = new List<Guid>();

            mThread = new Thread(new ThreadStart(DoWork));
            mThread.IsBackground = true;

            stopping = false;
            stopped = false;
            disposed = false;

            mThread.Start();
            while (!mThread.IsAlive) ;
        }
        #endregion

        #region Instance Methods
        /// <summary>
        /// Starts editing an item. While items are edited,
        /// the cache thread will not work on them to prevent collisions.
        /// </summary>
        /// <param name="guid">The guid representing the item</param>
        public void BeginItemEdit(Guid guid)
        {
            lock (lockObject)
            {
                if (!editCache.ContainsKey(guid))
                {
                    editCache.Add(guid, false);
                }
            }
        }
        /// <summary>
        /// Ends editing an item. After this call, item
        /// image will be continued to be fetched by the thread.
        /// </summary>
        /// <param name="guid">The guid representing the item.</param>
        public void EndItemEdit(Guid guid)
        {
            lock (lockObject)
            {
                if (editCache.ContainsKey(guid))
                {
                    editCache.Remove(guid);
                }
            }
        }
        /// <summary>
        /// Gets the cache state of the specified item.
        /// </summary>
        /// <param name="guid">The guid representing the item.</param>
        public CacheState GetCacheState(Guid guid)
        {
            lock (lockObject)
            {
                CacheItem item = null;
                if (thumbCache.TryGetValue(guid, out item))
                    return item.State;
            }

            return CacheState.Unknown;
        }
        /// <summary>
        /// Rebuilds the thumbnail cache.
        /// Old thumbnails will be kept until they are overwritten
        /// by new ones.
        /// </summary>
        public void Rebuild()
        {
            lock (lockObject)
            {
                foreach (CacheItem item in thumbCache.Values)
                    item.State = CacheState.Unknown;
            }
        }
        /// <summary>
        /// Clears the thumbnail cache.
        /// </summary>
        public void Clear()
        {
            lock (lockObject)
            {
                foreach (CacheItem item in thumbCache.Values)
                    item.Dispose();
                thumbCache.Clear();

                foreach (CacheItem item in toCache)
                    item.Dispose();
                toCache.Clear();

                foreach (CacheItem item in rendererToCache)
                    item.Dispose();
                rendererToCache.Clear();
                if (rendererItem != null)
                    rendererItem.Dispose();

                memoryUsed = 0;
                memoryUsedByRemoved = 0;
                removedItems.Clear();
            }
        }
        /// <summary>
        /// Removes the given item from the cache.
        /// </summary>
        /// <param name="guid">The guid of the item to remove.</param>
        public void Remove(Guid guid)
        {
            Remove(guid, false);
        }
        /// <summary>
        /// Removes the given item from the cache.
        /// </summary>
        /// <param name="guid">The guid of the item to remove.</param>
        /// <param name="removeNow">true to remove the item now; false to remove the
        /// item later when the cache is purged.</param>
        public void Remove(Guid guid, bool removeNow)
        {
            lock (lockObject)
            {
                CacheItem item = null;
                if (!thumbCache.TryGetValue(guid, out item))
                    return;

                if (removeNow)
                {
                    memoryUsed -= item.Size.Width * item.Size.Height * 24 / 8;
                    item.Dispose();
                    thumbCache.Remove(guid);
                }
                else
                {
                    // Calculate the memory usage (approx. Width * Height * BitsPerPixel / 8)
                    memoryUsedByRemoved += item.Size.Width * item.Size.Height * 24 / 8;
                    removedItems.Add(guid);

                    // Remove items now if we can free more than 25% of the cache limit
                    if ((mCacheLimitAsMemory != 0 && memoryUsedByRemoved > mCacheLimitAsMemory / 4) ||
                        (mCacheLimitAsItemCount != 0 && removedItems.Count > mCacheLimitAsItemCount / 4))
                    {
                        CacheItem itemToRemove = null;
                        foreach (Guid iguid in removedItems)
                        {
                            if (thumbCache.TryGetValue(iguid, out itemToRemove))
                            {
                                itemToRemove.Dispose();
                                thumbCache.Remove(iguid);
                            }
                        }
                        removedItems.Clear();
                        memoryUsed -= memoryUsedByRemoved;
                        memoryUsedByRemoved = 0;
                    }
                }
            }
        }
        /// <summary>
        /// Adds the image to the cache queue.
        /// </summary>
        /// <param name="guid">The guid representing this item.</param>
        /// <param name="filename">Filesystem path to the image file.</param>
        /// <param name="thumbSize">Requested thumbnail size.</param>
        /// <param name="useEmbeddedThumbnails">UseEmbeddedThumbnails property of the owner control.</param>
        /// <param name="autoRotate">AutoRotate property of the owner control.</param>
        public void Add(Guid guid, string filename, Size thumbSize,
            UseEmbeddedThumbnails useEmbeddedThumbnails, bool autoRotate)
        {
            lock (lockObject)
            {
                // Already cached?
                CacheItem item = null;
                if (thumbCache.TryGetValue(guid, out item))
                {
                    if (item.Size == thumbSize && item.UseEmbeddedThumbnails == useEmbeddedThumbnails)
                        return;
                }
                // Add to cache
                toCache.Push(new CacheItem(guid, filename,
                    thumbSize, null, CacheState.Unknown,
                    useEmbeddedThumbnails, autoRotate));
                Monitor.Pulse(lockObject);
            }
        }
        /// <summary>
        /// Adds the image to the cache queue.
        /// </summary>
        /// <param name="guid">The guid representing this item.</param>
        /// <param name="filename">Filesystem path to the image file.</param>
        /// <param name="thumbSize">Requested thumbnail size.</param>
        /// <param name="thumb">Thumbnail image to add to cache.</param>
        /// <param name="useEmbeddedThumbnails">UseEmbeddedThumbnails property of the owner control.</param>
        /// <param name="autoRotate">AutoRotate property of the owner control.</param>
        public void Add(Guid guid, string filename, Size thumbSize, Image thumb,
            UseEmbeddedThumbnails useEmbeddedThumbnails, bool autoRotate)
        {
            lock (lockObject)
            {
                // Already cached?
                CacheItem item = null;
                if (thumbCache.TryGetValue(guid, out item))
                {
                    if (item.Size == thumbSize && item.UseEmbeddedThumbnails == useEmbeddedThumbnails)
                        return;
                }
                // Add to cache
                thumbCache.Add(guid, new CacheItem(guid, filename, thumbSize,
                    ThumbnailExtractor.FromImage(thumb, thumbSize, autoRotate),
                    CacheState.Cached));
            }

            try
            {
                if (mImageListView != null && mImageListView.IsHandleCreated && !mImageListView.IsDisposed)
                {
                    mImageListView.BeginInvoke(new ThumbnailCachedEventHandlerInternal(
                        mImageListView.OnThumbnailCachedInternal), guid, false);
                    mImageListView.BeginInvoke(new RefreshDelegateInternal(
                        mImageListView.OnRefreshInternal));
                }
            }
            catch (ObjectDisposedException)
            {
                if (!Stopping) throw;
            }
            catch (InvalidOperationException)
            {
                if (!Stopping) throw;
            }
        }
        /// <summary>
        /// Adds a virtual item to the cache queue.
        /// </summary>
        /// <param name="guid">The guid representing this item.</param>
        /// <param name="key">The key of this item.</param>
        /// <param name="thumbSize">Requested thumbnail size.</param>
        /// <param name="useEmbeddedThumbnails">UseEmbeddedThumbnails property of the owner control.</param>
        /// <param name="autoRotate">AutoRotate property of the owner control.</param>
        public void Add(Guid guid, object key, Size thumbSize,
            UseEmbeddedThumbnails useEmbeddedThumbnails, bool autoRotate)
        {
            lock (lockObject)
            {
                // Already cached?
                CacheItem item = null;
                if (thumbCache.TryGetValue(guid, out item))
                {
                    if (item.Size == thumbSize && item.UseEmbeddedThumbnails == useEmbeddedThumbnails)
                        return;
                }
                // Add to cache
                toCache.Push(new CacheItem(guid, key, thumbSize, null,
                    CacheState.Unknown, useEmbeddedThumbnails, autoRotate));
                Monitor.Pulse(lockObject);
            }
        }
        /// <summary>
        /// Adds a virtual item to the cache.
        /// </summary>
        /// <param name="guid">The guid representing this item.</param>
        /// <param name="key">The key of this item.</param>
        /// <param name="thumbSize">Requested thumbnail size.</param>
        /// <param name="thumb">Thumbnail image to add to cache.</param>
        /// <param name="useEmbeddedThumbnails">UseEmbeddedThumbnails property of the owner control.</param>
        /// <param name="autoRotate">AutoRotate property of the owner control.</param>
        public void Add(Guid guid, object key, Size thumbSize, Image thumb,
            UseEmbeddedThumbnails useEmbeddedThumbnails, bool autoRotate)
        {
            lock (lockObject)
            {
                // Already cached?
                CacheItem item = null;
                if (thumbCache.TryGetValue(guid, out item))
                {
                    if (item.Size == thumbSize && item.UseEmbeddedThumbnails == useEmbeddedThumbnails)
                        return;
                }
                // Add to cache
                thumbCache.Add(guid, new CacheItem(guid, key, thumbSize,
                    ThumbnailExtractor.FromImage(thumb, thumbSize, autoRotate),
                    CacheState.Cached, useEmbeddedThumbnails, autoRotate));
            }

            try
            {
                if (mImageListView != null && mImageListView.IsHandleCreated && !mImageListView.IsDisposed)
                {
                    mImageListView.BeginInvoke(new ThumbnailCachedEventHandlerInternal(
                        mImageListView.OnThumbnailCachedInternal), guid, false);
                    mImageListView.BeginInvoke(new RefreshDelegateInternal(
                        mImageListView.OnRefreshInternal));
                }
            }
            catch (ObjectDisposedException)
            {
                if (!Stopping) throw;
            }
            catch (InvalidOperationException)
            {
                if (!Stopping) throw;
            }
        }
        /// <summary>
        /// Adds the image to the renderer cache queue.
        /// </summary>
        /// <param name="guid">The guid representing this item.</param>
        /// <param name="filename">Filesystem path to the image file.</param>
        /// <param name="thumbSize">Requested thumbnail size.</param>
        /// <param name="useEmbeddedThumbnails">UseEmbeddedThumbnails property of the owner control.</param>
        /// <param name="autoRotate">AutoRotate property of the owner control.</param>
        public void AddToRendererCache(Guid guid, string filename,
            Size thumbSize, UseEmbeddedThumbnails useEmbeddedThumbnails, bool autoRotate)
        {
            lock (lockObject)
            {
                // Already cached?
                if (rendererGuid == guid && rendererItem != null &&
                    rendererItem.Size == thumbSize &&
                    rendererItem.UseEmbeddedThumbnails == useEmbeddedThumbnails)
                    return;

                // Renderer cache holds one item only.
                foreach (CacheItem item in rendererToCache)
                    item.Dispose();
                rendererToCache.Clear();

                rendererToCache.Push(new CacheItem(guid, filename,
                    thumbSize, null, CacheState.Unknown, useEmbeddedThumbnails, autoRotate));
                Monitor.Pulse(lockObject);
            }
        }
        /// <summary>
        /// Adds the virtual item image to the renderer cache queue.
        /// </summary>
        /// <param name="guid">The guid representing this item.</param>
        /// <param name="key">The key of this item.</param>
        /// <param name="thumbSize">Requested thumbnail size.</param>
        /// <param name="useEmbeddedThumbnails">UseEmbeddedThumbnails property of the owner control.</param>
        /// <param name="autoRotate">AutoRotate property of the owner control.</param>
        public void AddToRendererCache(Guid guid, object key, Size thumbSize,
            UseEmbeddedThumbnails useEmbeddedThumbnails, bool autoRotate)
        {
            lock (lockObject)
            {
                // Already cached?
                if (rendererGuid == guid && rendererItem != null &&
                    rendererItem.Size == thumbSize &&
                    rendererItem.UseEmbeddedThumbnails == useEmbeddedThumbnails)
                    return;

                // Renderer cache holds one item only.
                foreach (CacheItem item in rendererToCache)
                    item.Dispose();
                rendererToCache.Clear();

                rendererToCache.Push(new CacheItem(guid, key, thumbSize,
                    null, CacheState.Unknown, useEmbeddedThumbnails, autoRotate));
                Monitor.Pulse(lockObject);
            }
        }
        /// <summary>
        /// Gets the image from the renderer cache. If the image is not cached,
        /// null will be returned.
        /// </summary>
        /// <param name="guid">The guid representing this item.</param>
        /// <param name="thumbSize">Requested thumbnail size.</param>
        /// <param name="useEmbeddedThumbnails">UseEmbeddedThumbnails property of the owner control.</param>
        public Image GetRendererImage(Guid guid, Size thumbSize,
            UseEmbeddedThumbnails useEmbeddedThumbnails)
        {
            lock (lockObject)
            {
                if (rendererGuid == guid && rendererItem != null &&
                    rendererItem.Size == thumbSize &&
                    rendererItem.UseEmbeddedThumbnails == useEmbeddedThumbnails)
                    return rendererItem.Image;
            }
            return null;
        }
        /// <summary>
        /// Gets the image from the thumbnail cache. If the image is not cached,
        /// null will be returned.
        /// </summary>
        /// <param name="guid">The guid representing this item.</param>
        /// <param name="clone">true to return a cloned image; otherwise false.</param>
        public Image GetImage(Guid guid, bool clone)
        {
            lock (lockObject)
            {
                CacheItem item = null;
                if (thumbCache.TryGetValue(guid, out item))
                {
                    Image img = item.Image;
                    if (clone && img != null)
                        img = (Image)img.Clone();
                    return img;
                }
            }
            return null;
        }
        /// <summary>
        /// Gets the image from the thumbnail cache. If the image is not cached,
        /// null will be returned.
        /// </summary>
        /// <param name="guid">The guid representing this item.</param>
        public Image GetImage(Guid guid)
        {
            return GetImage(guid, false);
        }
        /// <summary>
        /// Stops the cache manager.
        /// </summary>
        public void Stop()
        {
            lock (lockObject)
            {
                if (!stopping)
                {
                    stopping = true;
                    Monitor.Pulse(lockObject);
                }
            }
        }
        /// <summary>
        /// Performs application-defined tasks associated with freeing,
        /// releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                Clear();

                disposed = true;

                GC.SuppressFinalize(this);
            }
        }
#if DEBUG
        /// <summary>
        /// Releases unmanaged resources and performs other cleanup operations before the
        /// ImageListViewCacheManager is reclaimed by garbage collection.
        /// </summary>
        ~ImageListViewCacheManager()
        {
            System.Diagnostics.Debug.Print("Finalizer of {0} called.", GetType());
            Dispose();
        }
#endif
        #endregion

        #region Worker Method
        /// <summary>
        /// Used by the worker thread to generate image thumbnails.
        /// Once a thumbnail image is generated, the item will be redrawn
        /// to replace the placeholder image.
        /// </summary>
        private void DoWork()
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

            while (!Stopping)
            {
                Guid guid = new Guid();
                CacheItem request = null;
                bool rendererRequest = false;

                try
                {
                    lock (lockObject)
                    {
                        // Wait until we have items waiting to be cached
                        if (toCache.Count == 0 && rendererToCache.Count == 0)
                            Monitor.Wait(lockObject);
                    }

                    // Set to true when we exceed the cache memory limit
                    bool cleanupRequired = false;
                    // Set to true when we fetch at least one thumbnail
                    bool thumbnailCreated = false;

                    // Loop until we exhaust the queue
                    bool queueFull = true;
                    while (queueFull && !Stopping)
                    {
                        lock (lockObject)
                        {
                            sw.Start();
                            // Get an item from the queue
                            if (rendererToCache.Count != 0)
                            {
                                request = rendererToCache.Pop();
                                guid = request.Guid;
                                foreach (CacheItem item in rendererToCache)
                                    item.Dispose();
                                rendererToCache.Clear();
                                rendererRequest = true;
                            }
                            else if (toCache.Count != 0)
                            {
                                request = toCache.Pop();
                                guid = request.Guid;

                                // Is it already cached?
                                CacheItem existing = null;
                                if (thumbCache.TryGetValue(guid, out existing))
                                {
                                    if (existing.Size == mCurrentThumbnailSize)
                                        request = null;
                                }
                            }

                            // Is it in the edit cache?
                            if (editCache.ContainsKey(guid))
                                request = null;
                        }

                        // Is it outside visible area?
                        bool isvisible = true;
                        if (request != null && mCacheMode == CacheMode.OnDemand)
                        {
                            try
                            {
                                if (mImageListView != null && mImageListView.IsHandleCreated && !mImageListView.IsDisposed)
                                {
                                    isvisible = (bool)mImageListView.Invoke(new CheckItemVisibleDelegateInternal(
                                        mImageListView.IsItemVisible), guid);
                                }
                            }
                            catch (ObjectDisposedException)
                            {
                                if (!Stopping) throw;
                            }
                            catch (InvalidOperationException)
                            {
                                if (!Stopping) throw;
                            }
                        }

                        lock (lockObject)
                        {
                            if (!rendererRequest && !isvisible)
                                request = null;
                        }

                        // Proceed if we have a valid request
                        CacheItem result = null;
                        if (request != null)
                        {
                            Image thumb = null;

                            // Read thumbnail image
                            if (thumb == null)
                            {
                                if (request.IsVirtualItem)
                                {
                                    VirtualItemThumbnailEventArgs e = new VirtualItemThumbnailEventArgs(
                                        request.VirtualItemKey, request.Size);
                                    if (mImageListView != null && mImageListView.IsHandleCreated && !mImageListView.IsDisposed)
                                        mImageListView.RetrieveVirtualItemThumbnailInternal(e);
                                    if (e.ThumbnailImage != null)
                                        thumb = e.ThumbnailImage;
                                }
                                else
                                {
                                    thumb = ThumbnailExtractor.FromFile(request.FileName,
                                        request.Size, request.UseEmbeddedThumbnails, request.AutoRotate);
                                }
                            }

                            // Create the cache item
                            if (thumb == null)
                            {
                                if (!mRetryOnError)
                                {
                                    result = new CacheItem(guid, request.FileName,
                                        request.Size, null, CacheState.Error,
                                        request.UseEmbeddedThumbnails, request.AutoRotate);
                                }
                                else
                                    result = null;
                            }
                            else
                            {
                                result = new CacheItem(guid, request.FileName,
                                    request.Size, thumb, CacheState.Cached,
                                    request.UseEmbeddedThumbnails, request.AutoRotate);
                                thumbnailCreated = true;
                            }

                            if (result != null)
                            {
                                if (rendererRequest)
                                {
                                    lock (lockObject)
                                    {
                                        if (rendererItem != null)
                                            rendererItem.Dispose();

                                        rendererGuid = guid;
                                        rendererItem = result;
                                        rendererRequest = false;
                                    }
                                }
                                else
                                {
                                    lock (lockObject)
                                    {
                                        CacheItem existing = null;
                                        if (thumbCache.TryGetValue(guid, out existing))
                                        {
                                            existing.Dispose();
                                            thumbCache.Remove(guid);
                                        }
                                        thumbCache.Add(guid, result);

                                        if (thumb != null)
                                        {
                                            // Did the thumbnail size change while we were
                                            // creating the thumbnail?                                    
                                            if (result.Size != mCurrentThumbnailSize)
                                                result.State = CacheState.Unknown;

                                            // Did we exceed the cache limit?
                                            memoryUsed += thumb.Width * thumb.Height * 24 / 8;
                                            if ((mCacheLimitAsMemory != 0 && memoryUsed > mCacheLimitAsMemory) ||
                                                (mCacheLimitAsItemCount != 0 && thumbCache.Count > mCacheLimitAsItemCount))
                                                cleanupRequired = true;
                                        }
                                    }
                                }
                            }

                            try
                            {
                                if (mImageListView != null && mImageListView.IsHandleCreated && !mImageListView.IsDisposed)
                                {
                                    mImageListView.BeginInvoke(new ThumbnailCachedEventHandlerInternal(
                                        mImageListView.OnThumbnailCachedInternal), guid, (result == null));
                                }
                            }
                            catch (ObjectDisposedException)
                            {
                                if (!Stopping) throw;
                            }
                            catch (InvalidOperationException)
                            {
                                if (!Stopping) throw;
                            }
                        }

                        // Check if the cache is exhausted
                        lock (lockObject)
                        {
                            if (toCache.Count == 0 && rendererToCache.Count == 0)
                                queueFull = false;
                        }

                        // Do we need a refresh?
                        sw.Stop();
                        if (sw.ElapsedMilliseconds > 100)
                        {
                            try
                            {
                                if (mImageListView != null && mImageListView.IsHandleCreated && !mImageListView.IsDisposed)
                                {
                                    mImageListView.BeginInvoke(new RefreshDelegateInternal(
                                        mImageListView.OnRefreshInternal));
                                }
                                sw.Reset();
                            }
                            catch (ObjectDisposedException)
                            {
                                if (!Stopping) throw;
                            }
                            catch (InvalidOperationException)
                            {
                                if (!Stopping) throw;
                            }
                        }
                        if (queueFull)
                            sw.Start();
                        else
                        {
                            sw.Stop();
                            sw.Reset();
                        }
                    }

                    // Clean up invisible items
                    if (cleanupRequired)
                    {
                        Dictionary<Guid, bool> visible = new Dictionary<Guid, bool>();
                        try
                        {
                            if (mImageListView != null && mImageListView.IsHandleCreated && !mImageListView.IsDisposed)
                            {
                                visible = (Dictionary<Guid, bool>)mImageListView.Invoke(
                                    new GetVisibleItemsDelegateInternal(mImageListView.GetVisibleItems));
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            if (!Stopping) throw;
                        }
                        catch (InvalidOperationException)
                        {
                            if (!Stopping) throw;
                        }

                        if (visible.Count != 0)
                        {
                            lock (lockObject)
                            {
                                foreach (KeyValuePair<Guid, CacheItem> item in thumbCache)
                                {
                                    if (!visible.ContainsKey(item.Key) && item.Value.State == CacheState.Cached && item.Value.Image != null)
                                    {
                                        removedItems.Add(item.Key);
                                        memoryUsedByRemoved += item.Value.Image.Width * item.Value.Image.Width * 24 / 8;
                                    }
                                }
                                foreach (Guid iguid in removedItems)
                                {
                                    if (thumbCache.ContainsKey(iguid))
                                    {
                                        thumbCache[iguid].Dispose();
                                        thumbCache.Remove(iguid);
                                    }
                                }
                                removedItems.Clear();
                                memoryUsed -= memoryUsedByRemoved;
                                memoryUsedByRemoved = 0;
                            }
                        }
                    }

                    if (thumbnailCreated)
                    {
                        try
                        {
                            if (mImageListView != null && mImageListView.IsHandleCreated && !mImageListView.IsDisposed)
                            {
                                mImageListView.BeginInvoke(new RefreshDelegateInternal(
                                    mImageListView.OnRefreshInternal));
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            if (!Stopping) throw;
                        }
                        catch (InvalidOperationException)
                        {
                            if (!Stopping) throw;
                        }
                    }
                }
                catch (Exception exception)
                {
                    // Delegate the exception to the parent control
                    try
                    {
                        if (mImageListView != null && mImageListView.IsHandleCreated && !mImageListView.IsDisposed)
                        {
                            mImageListView.BeginInvoke(new CacheErrorEventHandlerInternal(
                                mImageListView.OnCacheErrorInternal),
                                guid, exception, CacheThread.Details);
                        }
                    }
                    finally
                    {
                        ;
                    }
                }
                finally
                {
                    ;
                }
            }

            lock (lockObject)
            {
                stopped = true;
            }
        }
        #endregion
    }
}
