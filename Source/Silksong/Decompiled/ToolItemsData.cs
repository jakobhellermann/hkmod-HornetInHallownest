using System;
using Newtonsoft.Json;

namespace Silksong;

[Serializable]
public class ToolItemsData : SerializableNamedList<ToolItemsData.Data, ToolItemsData.NamedData>
{
	[Serializable]
	[JsonObject(MemberSerialization.Fields)]
	public struct Data : ISerializableNamedDataRedundancy
	{
		public bool IsUnlocked;

		public bool IsHidden;

		public bool HasBeenSeen;

		public bool HasBeenSelected;

		public int AmountLeft;

		public bool IsDataRedundant
		{
			get
			{
				if (!IsUnlocked)
				{
					return AmountLeft <= 0;
				}
				return false;
			}
		}
	}

	[Serializable]
	public class NamedData : SerializableNamedData<Data>
	{
	}
}
