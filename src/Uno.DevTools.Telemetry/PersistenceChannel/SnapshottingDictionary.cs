// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// 2019/04/12 (Jerome Laban <jerome.laban@nventive.com>):
//	- Extracted from dotnet.exe
// 2024/12/05 (Jerome Laban <jerome@platform.uno>):
//	- Updated for nullability
//

using System.Diagnostics.CodeAnalysis;

namespace Uno.DevTools.Telemetry.PersistenceChannel
{
	internal class SnapshottingDictionary<TKey, TValue> :
		SnapshottingCollection<KeyValuePair<TKey, TValue>, IDictionary<TKey, TValue>>, IDictionary<TKey, TValue> where TKey:notnull
	{
		public SnapshottingDictionary()
			: base(new Dictionary<TKey, TValue>())
		{
		}

		public ICollection<TKey> Keys => GetSnapshot().Keys;

		public ICollection<TValue> Values => GetSnapshot().Values;

		public TValue this[TKey key]
		{
			get => GetSnapshot()[key];

			set
			{
				lock (Collection)
				{
					Collection[key] = value;
					snapshot = null;
				}
			}
		}

		public void Add(TKey key, TValue value)
		{
			lock (Collection)
			{
				Collection.Add(key, value);
				snapshot = null;
			}
		}

		public bool ContainsKey(TKey key)
		{
			return GetSnapshot().ContainsKey(key);
		}

		public bool Remove(TKey key)
		{
			lock (Collection)
			{
				var removed = Collection.Remove(key);
				if (removed)
				{
					snapshot = null;
				}

				return removed;
			}
		}

#if !NET5_0_OR_GREATER
#nullable disable
#pragma warning disable CS8632
#endif
		public bool TryGetValue(TKey key, [NotNullWhen(true)] out TValue? value)
		{
            if(GetSnapshot().TryGetValue(key, out value))
			{
                return false;
            }
			else
			{
				value = default;
				return false;
            }
		}

#nullable restore

		protected sealed override IDictionary<TKey, TValue> CreateSnapshot(IDictionary<TKey, TValue> collection)
		{
			return new Dictionary<TKey, TValue>(collection);
		}
	}
}
