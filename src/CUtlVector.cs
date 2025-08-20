using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace K4AlwaysWeaponSkins;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct CUtlVectorRaw
{
	public int Size;
	public nint Memory;
	public int AllocSize;
	public int GrowSize;
}

public class CUtlVector<T>(nint ptr) : NativeObject(ptr), IReadOnlyList<T> where T : NativeObject
{
	public int Count => NativeAPI.GetNetworkVectorSize(Handle);
	public T this[int index] => ElementAt(index);

	public T ElementAt(int index)
	{
		if (index < 0 || index >= Count)
			throw new IndexOutOfRangeException();

		nint elementPtr = NativeAPI.GetNetworkVectorElementAt(Handle, index);
		return (T)Activator.CreateInstance(typeof(T), elementPtr)!;
	}

	public void RemoveAll() => NativeAPI.RemoveAllNetworkVectorElements(Handle);

	public IEnumerator<T> GetEnumerator()
	{
		for (int i = 0; i < Count; i++)
			yield return ElementAt(i);
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public TValue? GetValue<TValue>(string name)
	{
		foreach (T element in this)
		{
			var type = typeof(T);
			var prop = type.GetProperty(name);
			if (prop != null)
			{
				return (TValue?)prop.GetValue(element);
			}

			var field = type.GetField(name);
			if (field != null)
			{
				return (TValue?)field.GetValue(element);
			}
		}

		throw new KeyNotFoundException($"Property/Field '{name}' not found in any element.");
	}

	public static unsafe nint CreateVector(int initialSize = 0)
	{
		int allocSize = initialSize > 0 ? initialSize : 16;
		nint vectorPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CUtlVectorRaw>());
		nint memoryPtr = Marshal.AllocHGlobal(allocSize * IntPtr.Size);

		CUtlVectorRaw* vector = (CUtlVectorRaw*)vectorPtr;
		vector->Size = 0;
		vector->Memory = memoryPtr;
		vector->AllocSize = allocSize;
		vector->GrowSize = 0;

		return vectorPtr;
	}

	public static unsafe void FreeVector(nint vectorPtr)
	{
		if (vectorPtr == IntPtr.Zero) return;

		CUtlVectorRaw* vector = (CUtlVectorRaw*)vectorPtr;
		if (vector->Memory != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(vector->Memory);
		}
		Marshal.FreeHGlobal(vectorPtr);
	}

	public static unsafe int GetVectorCount(nint vectorPtr)
	{
		if (vectorPtr == IntPtr.Zero) return 0;
		CUtlVectorRaw* vector = (CUtlVectorRaw*)vectorPtr;
		return vector->Size;
	}

	public static unsafe nint GetVectorElement(nint vectorPtr, int index)
	{
		if (vectorPtr == IntPtr.Zero) return IntPtr.Zero;
		CUtlVectorRaw* vector = (CUtlVectorRaw*)vectorPtr;
		if (index < 0 || index >= vector->Size) return IntPtr.Zero;

		nint* elements = (nint*)vector->Memory;
		return elements[index];
	}
}