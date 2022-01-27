using CommandLine;
using SheetMan.Models;

namespace SheetMan.CodeGeneration
{
    public partial class CsCodeGenerator
    {
        static readonly string CS_SNIPPER_USING = @"
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using SheetMan.Runtime;

#if !NO_UNITY
using Cysharp.Threading.Tasks;
#endif
";


        static readonly string CS_SNIPPET_EXCEPTION = @"
/// <summary>
/// Exception about SheetMan.
/// </summary>
public class SheetManException : System.Exception
{
	public SheetManException()
	{
	}

	public SheetManException(string message) : base(message)
	{
	}

	public SheetManException(string message, System.Exception inner) : base(message, inner)
	{
	}
}
";


        static readonly string CS_SNIPPER_READ_BYTES_DELEGATES = @"
public delegate Task<byte[]> ReadAllBytesAsyncDelegate(string filename);

public static ReadAllBytesAsyncDelegate ReadAllBytesAsync = async (string filename) => {
#if !NO_UNITY
    byte[] bytes = null;
    await Task.Run(() => {
        bytes = File.ReadAllBytes(filename);
    });

    return bytes;
#else
    var bytes = await System.IO.File.ReadAllBytesAsync(filename);
    if (bytes == null)
        throw new SheetManException($$""Cannot read a file '{filename}'"");

    return bytes;
#endif
};
";


        static readonly string CS_SNIPPET_COLLECTION_HELPER = @"
/// <summary>
/// Collection helper class.
/// </summary>
public static class CollectionsHelper
{
    /// <summary>
    /// This will return true if the two collections are value-wise the same.
    /// If the collection contains a collection, the collections will be compared using this method.
    /// </summary>
    public static bool Equals(IEnumerable first, IEnumerable second)
    {
        if (first == null && second == null)
            return true;

        if (first == null || second == null)
            return false;

        var fiter = first.GetEnumerator();
        var siter = second.GetEnumerator();

        var fnext = fiter.MoveNext();
        var snext = siter.MoveNext();

        while (fnext && snext)
        {
            var fenum = fiter.Current as IEnumerable;
            var senum = siter.Current as IEnumerable;

            if (fenum != null && senum != null)
            {
                if (!Equals(fenum, senum))
                    return false;
            }
            else if (fenum == null ^ senum == null)
            {
                return false;
            }
            else if (!Equals(fiter.Current, siter.Current))
            {
                return false;
            }

            fnext = fiter.MoveNext();
            snext = siter.MoveNext();
        }

        return fnext == snext;
    }

    /// <summary>
    /// This returns a hashcode based on the value of the enumerable.
    /// </summary>
    public static int GetHashCode(IEnumerable enumerable)
    {
        if (enumerable == null)
            return 0;

        int hashcode = 0;
        foreach (var item in enumerable)
        {
            int objHash = !(item is IEnumerable enumerableItem) ? item.GetHashCode() : GetHashCode(enumerableItem);

            unchecked
            {
                hashcode = (hashcode * 397) ^ objHash;
            }
        }

        return hashcode;
    }
}";


        static readonly string CS_SNIPPET_TOSTRING_HELPER = @"
/// <summary>
/// ToString helper class.
/// </summary>
public static class ToStringHelper
{
    public static void ToString(object self, StringBuilder target, bool first = true)
    {
        if (!first)
            target.Append("", "");

        bool firstChild = true;
        if (self is string)
        {
            target.Append('""');
            target.Append(self);
            target.Append('""');
        }
        else if (self is IDictionary dictionary)
        {
            target.Append(""{"");
            foreach (DictionaryEntry pair in dictionary)
            {
                if (firstChild)
                    firstChild = false;
                else
                    target.Append("", "");

                target.Append(""{"");
                ToString(pair.Key, target, true);
                target.Append("", "");
                ToString(pair.Value, target, true);
                target.Append(""}"");
            }
            target.Append(""}"");
        }
        else if (self is IEnumerable enumerable)
        {
            target.Append(""["");
            foreach (var element in enumerable)
            {
                ToString(element, target, firstChild);
                firstChild = false;
            }
            target.Append(""]"");
        }
        else
        {
            target.Append(self);
        }
    }
}";
    }
}
