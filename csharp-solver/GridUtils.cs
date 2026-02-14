using System;
using System.Text;

namespace MapartSolving
{
    public static class GridUtils
    {
        public static void GetSizes<T>(T[,] grid, out int rows, out int columns)
        {
            rows = grid.GetLength(0);
            columns = grid.GetLength(1);
        }

        public static bool Compare<T>(T[,] grid1, T[,] grid2)
        {
            for (int i = 0; i < 2; i++)
                if (grid1.GetLength(i) != grid2.GetLength(i))
                    return false;

            for (int i = 0; i < grid1.GetLength(0); i++)
            for (int j = 0; j < grid1.GetLength(1); j++)
                if (!(grid1[i, j]?.Equals(grid2[i, j]) ?? grid2[i, j] is null))
                    return false;
            return true;
        }

        public static TTo[,] Cast<TFrom, TTo>(TFrom[,] grid, Func<TFrom, TTo> converter)
        {
            GetSizes(grid, out int rows, out int columns);
            var result = new TTo[rows, columns];
            for (int i = 0; i < rows; i++)
            for  (int j = 0; j < columns; j++)
                result[i, j] = converter(grid[i, j]);
            return result;
        }

        #region Nested array conversion
        public static T[,] FromNestedArray<T>(T[][] nestedArray)
        {
            int rows = nestedArray.Length;
            if (rows == 0)
                return new T[0, 0];
            int columns = nestedArray[0].Length;
            if (columns == 0)
                return new T[0, 0];
            var grid = new T[rows, columns];
            for (int row = 0; row < rows; row++)
            {
                Debug.Assert(nestedArray[row].Length == columns);
                for (int column = 0; column < columns; column++)
                    grid[row, column] = nestedArray[row][column];
            }

            return grid;
        }

        public static T[][] ToNestedArray<T>(T[,] grid)
        {
            GetSizes(grid, out int rows, out int columns);
            var nestedArray = new T[rows][];
            for (int row = 0; row < rows; row++)
            {
                nestedArray[row] = new T[columns];
                for (int column = 0; column < columns; column++)
                    nestedArray[row][column] = grid[row, column];
            }

            return nestedArray;
        }
    
        #endregion

        #region Print methods
        public static void Print<T>(T[,] grid) => Print(grid, v => v?.ToString() ?? string.Empty);
    
        public static void Print<T>(T[,] grid, Func<T, string?> toString, string separator = " ")
        {
            GetSizes(grid, out int height,  out int width);
            int maxStringLength = 0;
            var outputStrings = new string[height, width];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                string str = toString(grid[y, x]) ??  string.Empty;
                outputStrings[y, x] = str;
                maxStringLength = Math.Max(maxStringLength, str.Length);
            }
            var outputBuilder = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x > 0)
                        outputBuilder.Append(separator);
                    string str = outputStrings[y, x];
                    outputBuilder.Append(new string(' ', maxStringLength - str.Length));
                    outputBuilder.Append(str);
                }
                outputBuilder.AppendLine();
            }
            Debug.Log(outputBuilder.ToString());
        }
    
        public static void PrintColumns<T>(T[,] grid) => PrintColumns(grid, v => v?.ToString() ?? string.Empty);

        public static void PrintColumns<T>(T[,] grid, Func<T, string?> toString, string separator = " ")
        {
            GetSizes(grid, out int height,  out int width);
            var maxStringLength = new int[width];
            var outputStrings = new string[height, width];
            for (int y = 0;  y < height; y++)
            for (int x = 0; x < width; x++)
            {
                string str = toString(grid[y, x]) ??  string.Empty;
                outputStrings[y, x] = str;
                maxStringLength[x] = Math.Max(maxStringLength[x], str.Length);
            }
            var outputBuilder = new StringBuilder();
            for (int y = 0;  y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (maxStringLength[x] == 0)
                        continue;
                    if (x > 0)
                        outputBuilder.Append(separator);
                    string str = outputStrings[y, x];
                    outputBuilder.Append(new string(' ', maxStringLength[x] - str.Length));
                    outputBuilder.Append(str);
                }
                outputBuilder.AppendLine();
            }
            Debug.Log(outputBuilder.ToString());
        }
        #endregion
    }
}