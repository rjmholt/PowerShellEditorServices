//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices.Utility
{
    /// <summary>
    /// Provides a synchronized queue which can be used from within async
    /// operations.  This is primarily used for producer/consumer scenarios.
    /// </summary>
    /// <typeparam name="T">The type of item contained in the queue.</typeparam>
    public class AsyncQueue<T>
    {
        #region Private Fields

        private readonly BlockingCollection<T> _blockingCollection;

        #endregion

        #region Properties

        /// <summary>
        /// Returns true if the queue is currently empty.
        /// </summary>
        public bool IsEmpty => _blockingCollection.Count == 0;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes an empty instance of the AsyncQueue class.
        /// </summary>
        public AsyncQueue() : this(Enumerable.Empty<T>())
        {
        }

        /// <summary>
        /// Initializes an instance of the AsyncQueue class, pre-populated
        /// with the given collection of items.
        /// </summary>
        /// <param name="initialItems">
        /// An IEnumerable containing the initial items with which the queue will
        /// be populated.
        /// </param>
        public AsyncQueue(IEnumerable<T> initialItems)
        {
            _blockingCollection = new BlockingCollection<T>(new ConcurrentQueue<T>(initialItems));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Enqueues an item onto the end of the queue.
        /// </summary>
        /// <param name="item">The item to be added to the queue.</param>
        /// <returns>
        /// A Task which can be awaited until the synchronized enqueue
        /// operation completes.
        /// </returns>
        public Task EnqueueAsync(T item)
        {
            Enqueue(item);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Enqueues an item onto the end of the queue.
        /// </summary>
        /// <param name="item">The item to be added to the queue.</param>
        public void Enqueue(T item)
        {
            _blockingCollection.Add(item);
        }

        /// <summary>
        /// Dequeues an item from the queue or waits asynchronously
        /// until an item is available.
        /// </summary>
        /// <returns>
        /// A Task which can be awaited until a value can be dequeued.
        /// </returns>
        public Task<T> DequeueAsync()
        {
            return DequeueAsync(CancellationToken.None);
        }

        /// <summary>
        /// Dequeues an item from the queue or waits asynchronously
        /// until an item is available.  The wait can be cancelled
        /// using the given CancellationToken.
        /// </summary>
        /// <param name="cancellationToken">
        /// A CancellationToken with which a dequeue wait can be cancelled.
        /// </param>
        /// <returns>
        /// A Task which can be awaited until a value can be dequeued.
        /// </returns>
        public Task<T> DequeueAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                return Task.FromResult(Dequeue());
            });
        }

        /// <summary>
        /// Dequeues an item from the queue or waits asynchronously
        /// until an item is available.
        /// </summary>
        /// <returns></returns>
        public T Dequeue()
        {
            return Dequeue(CancellationToken.None);
        }

        /// <summary>
        /// Dequeues an item from the queue or waits asynchronously
        /// until an item is available.  The wait can be cancelled
        /// using the given CancellationToken.
        /// </summary>
        /// <param name="cancellationToken">
        /// A CancellationToken with which a dequeue wait can be cancelled.
        /// </param>
        /// <returns></returns>
        public T Dequeue(CancellationToken cancellationToken)
        {
            return _blockingCollection.Take(cancellationToken);
        }

        #endregion
    }
}
