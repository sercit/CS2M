using System.Reflection;
using System;

namespace CS2M.BaseGame.Systems
{
    public static class ReflectionExtensions
    {
        private const BindingFlags MemberLookup = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        public static void SetPrivateProperty(this object obj, string propertyName, object value)
        {
            var prop = obj.GetType().GetProperty(propertyName, MemberLookup);
            if (prop == null || !prop.CanWrite)
                throw new InvalidOperationException($"Set Method not found for '{propertyName}'");
            prop.SetValue(obj, value);
        }

        public static void SetPrivateField(this object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, MemberLookup);
            field?.SetValue(obj, value);
        }
    }
}
