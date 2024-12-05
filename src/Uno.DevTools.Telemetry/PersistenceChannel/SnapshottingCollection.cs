// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// 2019/04/12 (Jerome Laban <jerome.laban@nventive.com>):
//	- Extracted from dotnet.exe
// 2024/12/05 (Jerome Laban <jerome@platform.uno>):
//	- Updated for nullability
//

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Uno.DevTools.Telemetry.PersistenceChannel
{
	internal abstract class SnapshottingCollection<TItem, TCollection> : ICollection<TItem>
		where TCollection : notnull, ICollection<TItem>
	{
		protected readonly TCollection Collection;
		protected TCollection? snapshot;

		protected SnapshottingCollection(TCollection collection)
		{
			Collection = collection;
		}

		public int Count => GetSnapshot().Count;

		public bool IsReadOnly => false;

		public void Add(TItem item)
		{
			lock (Collection)
			{
				Collection.Add(item);
				snapshot = default;
			}
		}

		public void Clear()
		{
			lock (Collection)
			{
				Collection.Clear();
				snapshot = default;
			}
		}

		public bool Contains(TItem item)
		{
			return GetSnapshot().Contains(item);
		}

		public void CopyTo(TItem[] array, int arrayIndex)
		{
			GetSnapshot().CopyTo(array, arrayIndex);
		}

		public bool Remove(TItem item)
		{
			lock (Collection)
			{
				bool removed = Collection.Remove(item);
				if (removed)
				{
					snapshot = default;
				}

				return removed;
			}
		}

		public IEnumerator<TItem> GetEnumerator()
		{
			return GetSnapshot().GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		protected abstract TCollection CreateSnapshot(TCollection collection);

		protected TCollection GetSnapshot()
		{
			TCollection? localSnapshot = snapshot;
			if (localSnapshot == null)
			{
				lock (Collection)
				{
					snapshot = CreateSnapshot(Collection);
					localSnapshot = snapshot;
				}
			}

			return localSnapshot;
		}
	}
}
