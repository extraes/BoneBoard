using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard;

public static class Extensions
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

    public static int LevenshteinDistance(this string str, string otherStr)
    {
        var strLength = str.Length;
        var otherLength = otherStr.Length;

        var matrix = new int[strLength + 1, otherLength + 1];

        // First calculation, if one entry is empty return full length
        if (strLength == 0)
            return otherLength;

        if (otherLength == 0)
            return strLength;

        // Initialization of matrix with row size strLength and columns size otherStrLength
        for (var i = 0; i <= strLength; matrix[i, 0] = i++){}
        for (var j = 0; j <= otherLength; matrix[0, j] = j++){}

        // Calculate rows and collumns distances
        for (var i = 1; i <= strLength; i++)
        {
            for (var j = 1; j <= otherLength; j++)
            {
                var cost = (otherStr[j - 1] == str[i - 1]) ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }
        // return result
        return matrix[strLength, otherLength];
    }
}
