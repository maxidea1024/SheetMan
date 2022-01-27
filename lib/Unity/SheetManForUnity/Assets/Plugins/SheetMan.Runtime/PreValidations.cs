using System;

namespace SheetMan.Runtime
{
    /// <summary>
    /// PreValidations
    /// </summary>
    public static class PreValidations
    {
        /// <summary>
        /// Throws <see cref="ArgumentException"/> if condition is false.
        /// </summary>
        /// <param name="condition">The condition.</param>
        public static void CheckArgument(bool condition)
        {
            if (!condition)
                throw new ArgumentException();
        }

        /// <summary>
        /// Throws <see cref="ArgumentException"/> with given message if condition is false.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="errorMessage">The error message.</param>
        public static void CheckArgument(bool condition, string errorMessage)
        {
            if (!condition)
                throw new ArgumentException(errorMessage);
        }

        /// <summary>
        /// Throws <see cref="ArgumentNullException"/> if reference is null.
        /// </summary>
        /// <param name="reference">The reference.</param>
        public static T CheckNotNull<T>(T reference)
        {
            if (reference == null)
                throw new ArgumentNullException();

            return reference;
        }

        /// <summary>
        /// Throws <see cref="ArgumentNullException"/> if reference is null.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="paramName">The parameter name.</param>
        public static T CheckNotNull<T>(T reference, string paramName)
        {
            if (reference == null)
                throw new ArgumentNullException(paramName);

            return reference;
        }

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> if condition is false.
        /// </summary>
        /// <param name="condition">The condition.</param>
        public static void CheckState(bool condition)
        {
            if (!condition)
                throw new InvalidOperationException();
        }

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> with given message if condition is false.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="errorMessage">The error message.</param>
        public static void CheckState(bool condition, string errorMessage)
        {
            if (!condition)
                throw new InvalidOperationException(errorMessage);
        }

        /// <summary>
        /// 지정한 타입 `type`이 `TBaseType`을 상속받은 타입임을 체크.
        /// </summary>
        /// <typeparam name="TBaseType"></typeparam>
        /// <param name="type"></param>
        public static Type CheckDerivedType<TBaseType>(Type type)
        {
            if (!typeof(TBaseType).IsAssignableFrom(type))
                throw new InvalidOperationException($"Type '{type.FullName}' doesn't inherit from {nameof(TBaseType)}.");

            return type;
        }
    }
}
