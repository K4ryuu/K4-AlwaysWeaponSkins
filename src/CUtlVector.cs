using System.Collections;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace K4AlwaysWeaponSkins;

public class CUtlVector<T> : NativeObject, IReadOnlyList<T> where T : NativeObject
{
	public int Count => NativeAPI.GetNetworkVectorSize(base.Handle);

	public T this[int index] => this.Element(index);

	public CUtlVector(nint ptr) : base(ptr)
	{ }

	public unsafe T Element(int index)
	{
		if (index < 0 || index >= this.Count)
		{
			throw new IndexOutOfRangeException();
		}

		return (T)Activator.CreateInstance(typeof(T), NativeAPI.GetNetworkVectorElementAt(base.Handle, index))!;
	}

	public void RemoveAll()
	{
		NativeAPI.RemoveAllNetworkVectorElements(this.Handle);
	}

	public IEnumerator<T> GetEnumerator()
	{
		for (int i = 0; i < this.Count; i++)
		{
			yield return this.Element(i);
		}
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
	{
		return this.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return this.GetEnumerator();
	}
}
