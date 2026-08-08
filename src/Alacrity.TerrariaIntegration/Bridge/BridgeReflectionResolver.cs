#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace AlacrityTerraria
{
    /// <summary>Cached exact-signature reflection resolver shared by the injected UI runtime.</summary>
    internal sealed class BridgeReflectionResolver
    {
        private readonly Dictionary<string, MethodResolution> methods = new Dictionary<string, MethodResolution>(StringComparer.Ordinal);
        private readonly Dictionary<string, FieldResolution> fields = new Dictionary<string, FieldResolution>(StringComparer.Ordinal);

        public bool TryResolveStaticMethod(Type type, string name, Type returnType, Type[] parameterTypes, out MethodInfo method, out string diagnostic)
        {
            string key = type.AssemblyQualifiedName + "|method|" + name + "|" + returnType.AssemblyQualifiedName + "|" + string.Join(",", Array.ConvertAll(parameterTypes, parameter => parameter.AssemblyQualifiedName));
            if (!methods.TryGetValue(key, out MethodResolution resolution))
            {
                MethodInfo candidate = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, parameterTypes, null);
                if (candidate == null)
                    resolution = new MethodResolution(null, "Unavailable: static method " + type.FullName + "." + name + " with the expected signature was not found.");
                else if (candidate.ReturnType != returnType)
                    resolution = new MethodResolution(null, "Unavailable: static method " + type.FullName + "." + name + " has an unexpected return type.");
                else
                    resolution = new MethodResolution(candidate, string.Empty);
                methods.Add(key, resolution);
            }

            method = resolution.Method;
            diagnostic = resolution.Diagnostic;
            return method != null;
        }

        public bool TryResolveStaticField(Type type, string name, Type fieldType, out FieldInfo field, out string diagnostic)
        {
            string key = type.AssemblyQualifiedName + "|field|" + name + "|" + fieldType.AssemblyQualifiedName;
            if (!fields.TryGetValue(key, out FieldResolution resolution))
            {
                FieldInfo candidate = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (candidate == null)
                    resolution = new FieldResolution(null, "Unavailable: static field " + type.FullName + "." + name + " was not found.");
                else if (candidate.FieldType != fieldType)
                    resolution = new FieldResolution(null, "Unavailable: static field " + type.FullName + "." + name + " has an unexpected type.");
                else
                    resolution = new FieldResolution(candidate, string.Empty);
                fields.Add(key, resolution);
            }

            field = resolution.Field;
            diagnostic = resolution.Diagnostic;
            return field != null;
        }

        public bool TryCreateDelegate(MethodInfo method, Type delegateType, out Delegate callback, out string diagnostic)
        {
            try
            {
                callback = Delegate.CreateDelegate(delegateType, method);
                diagnostic = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                callback = null;
                diagnostic = "Failed: could not create " + delegateType.Name + " for " + method.DeclaringType.FullName + "." + method.Name + ": " + exception.Message;
                return false;
            }
        }

        private sealed class MethodResolution
        {
            public MethodResolution(MethodInfo method, string diagnostic) { Method = method; Diagnostic = diagnostic; }
            public MethodInfo Method { get; private set; }
            public string Diagnostic { get; private set; }
        }

        private sealed class FieldResolution
        {
            public FieldResolution(FieldInfo field, string diagnostic) { Field = field; Diagnostic = diagnostic; }
            public FieldInfo Field { get; private set; }
            public string Diagnostic { get; private set; }
        }
    }
}
