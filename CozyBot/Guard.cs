using System;
using System.Collections.Generic;
using System.Text;

namespace CozyBot
{
    public static class Guard
    {
        public static string NonNullWhitespaceEmpty(string value, string paramName)
            =>
                (String.IsNullOrEmpty(value) || String.IsNullOrWhiteSpace(value))
                ? throw new ArgumentException($"{paramName} cannot be null, whitespace or empty.", paramName)
                : value;


        public static T NonNull<T>(T obj, string paramName)
            where T : class
            => obj ?? throw new ArgumentNullException(paramName, $"{paramName} cannot be null.");

        public static int Positive(int value, string paramName)
            => (value > 0) ? value : throw new ArgumentException($"{paramName} must be greater than zero.", paramName);
    }
}
