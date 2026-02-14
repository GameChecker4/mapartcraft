using System;
using static MapartSolving.GridUtils;

namespace MapartSolving
{
    public static class MapartGridUtils
    {
        public static Direction[,] GetDirections(int[,] heights)
        {
            GetSizes(heights, out int width, out int height);
            var directions = new Direction[width, height - 1];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height - 1; y++)
            {
                directions[x, y] = (Direction)Math.Sign(heights[x, y + 1] - heights[x, y]);
            }
            return directions;
        }

        public static int Qualify(int[,] heights)
        {
            int quality = 0;
            GetSizes(heights, out int width, out int height);
            for (int x = 1; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (Math.Abs(heights[x, y] - heights[x - 1, y]) <= 1)
                    quality++;
            }
            return quality;
        }

        #region Casts
        public static Direction[,] CastDirections(int[,] directions) => Cast(directions, i =>
        {
            if (!Enum.IsDefined(typeof(Direction), i))
                throw new InvalidCastException();
            return (Direction)i;
        });

        public static int[,] CastDirections(Direction[,] directions) => Cast(directions, d => (int)d);
        #endregion

        #region Generate grids
        public static int[,] GenerateClassic(Direction[,] directions)
        {
            GetSizes(directions, out int width, out int height);
            var result = new int[width, height + 1];
            for (int x = 0; x < width; x++)
            {
                int min = 0;
                for (int y = 0; y < height; y++)
                {
                    int nextValue = result[x, y] + (int)directions[x, y];
                    result[x, y + 1] = nextValue;
                    min = Math.Min(nextValue, min);
                }

                for (int y = 0; y <= height; y++)
                    result[x, y] -= min;
            }

            return result;
        }

        public static int[,] GenerateValley(Direction[,] directions)
        {
            GetSizes(directions, out int width, out int height);
            var result = new int[width, height + 1];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                    if (directions[x, y] != Direction.Down)
                        result[x, y + 1] = result[x, y] + (int)directions[x, y];

                for (int y = height - 1; y >= 0; y--)
                    if (directions[x, y] != Direction.Up && result[x, y] <= result[x, y + 1])
                        result[x, y] = result[x, y + 1] - (int)directions[x, y];
            }

            return result;
        }
        #endregion

        #region Turn methods
        public static T[,] Turn<T>(T[,] input)
        {
            GetSizes(input, out int rows, out int columns);
            var result = new T[columns, rows];
            for (int row = 0; row < columns; row++)
            for (int column = 0; column < rows; column++)
                result[row, column] = input[rows - column - 1, row];
            return result;
        }
    
        public static T[,] UndoTurn<T>(T[,] input)
        {
            GetSizes(input, out int rows, out int columns);
            var result = new T[columns, rows];
            for (int row = 0; row < columns; row++)
            for (int column = 0; column < rows; column++)
                result[row, column] = input[column, columns - row - 1];
            return result;
        }
        #endregion
    
        #region Print methods
        public static void PrintTurned<T>(T[,] grid) => PrintTurned(grid, v => v?.ToString() ?? string.Empty);
        public static void PrintTurned<T>(T[,] grid, Func<T, string> toString, string separator = " ") => Print(UndoTurn(grid), toString, separator);
        #endregion
    }
}