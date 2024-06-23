using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard;

public static class EnumerableExtensions
{
    public static T Random<T>(this IEnumerable<T> enumerable)
    {
        var list = enumerable.ToList();
        return list[System.Random.Shared.Next(list.Count)];
    }

    public static T Random<T>(this T[] array)
    {
        return array[System.Random.Shared.Next(array.Length)];
    }
}
