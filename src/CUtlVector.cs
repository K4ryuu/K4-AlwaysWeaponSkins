using System.Collections;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace K4AlwaysWeaponSkins;

public class CUtlVector<T> : NativeObject, IReadOnlyList<T> where T : NativeObject
{
	public int Count => NativeAPI.GetNetworkVectorSize(Handle);
	public T this[int index] => ElementAt(index);

	public CUtlVector(nint ptr) : base(ptr) { }

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

	public string DebugString()
	{
		StringBuilder sb = new StringBuilder();
		foreach (T element in this)
		{
			sb.AppendLine($"Element [{element.Handle}]");
			var type = typeof(T);
			foreach (var prop in type.GetProperties())
			{
				try
				{
					sb.AppendLine($"\t{prop.Name} ({prop.PropertyType.Name}): {prop.GetValue(element)}");
				}
				catch (Exception ex)
				{
					sb.AppendLine($"\t{prop.Name}: ERROR ({ex.Message})");
				}
			}

			foreach (var field in type.GetFields())
			{
				try
				{
					sb.AppendLine($"\t{field.Name} ({field.FieldType.Name}): {field.GetValue(element)}");
				}
				catch (Exception ex)
				{
					sb.AppendLine($"\t{field.Name}: ERROR ({ex.Message})");
				}
			}
		}

		return sb.ToString();
	}

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
}