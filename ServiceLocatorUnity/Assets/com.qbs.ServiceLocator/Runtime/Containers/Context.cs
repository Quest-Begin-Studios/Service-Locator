using System;
using System.Collections.Generic;

namespace QBS.ServiceLocator
{
    /// <summary>
    ///     Defines custom context scopes for ScopedContext lifetime services.
    ///     Allows grouping services into logical contexts that can be independently initialized and disposed.
    ///     Define application-specific contexts in your own assembly using <c>const int</c> values and implicit conversion:
    ///     <code>
    /// public static class GameContexts
    /// {
    ///     public const int MainMenu = 1;
    ///     public const int Gameplay = 2;
    /// }
    /// </code>
    ///     Pass them to <see cref="ServiceAttribute" /> and ServiceLocator APIs directly — implicit conversion handles the
    ///     rest.
    /// <remarks>
    /// Value `0` is reserved as unset/unassigned, do not use.     
    /// </remarks>
    /// </summary>
    public readonly struct Context : IEquatable<Context>
    {
        private static readonly Dictionary<int, Context> _contexts = new();
        
        public readonly int Value;

        public Context(int value)
        {
            if (value == 0)
            {
                throw new ArgumentException("Zero is used as a default value for contexts, use other ints!");
            }

            Value = value;
            
            if (!_contexts.TryAdd(value, this))
            {
                throw new ArgumentException($"Context {value} is already a registered value for contexts");
            }
        }

        public static implicit operator Context(int value)
        {
            return _contexts.TryGetValue(value, out var actualValue) ? actualValue : new Context(value);
        }

        public static implicit operator int(Context c)
        {
            return c.Value;
        }

        public bool Equals(Context other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is Context c && Equals(c);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public static bool operator ==(Context a, Context b)
        {
            return a.Value == b.Value;
        }

        public static bool operator !=(Context a, Context b)
        {
            return a.Value != b.Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static void ClearRegisteredContexts()
        {
            _contexts.Clear();
        }
    }
}