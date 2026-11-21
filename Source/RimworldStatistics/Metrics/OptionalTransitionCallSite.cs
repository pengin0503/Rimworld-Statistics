using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace RimworldStatistics.Metrics;

internal static class OptionalTransitionCallSite
{
    internal static bool Observing(string unit)
        => MetricGates.IsRecording(unit) && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    internal static MethodInfo ResolveGenPlaceTryPlaceThing()
    {
        var method = AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
                [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
                 typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int)])
            ?? AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
                [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
                 typeof(Action<Thing, int>), typeof(Predicate<IntVec3>)]);
        if (method?.ReturnType != typeof(bool))
            throw new InvalidOperationException("GenPlace.TryPlaceThing signature changed");
        return method;
    }

    internal static MethodInfo? ResolvePipeNetDistributeAmongStorage(Type net, Type? storageElementType = null)
    {
        var parameters = storageElementType is null
            ? new[] { typeof(float), typeof(float).MakeByRefType() }
            : new[] { typeof(float), typeof(float).MakeByRefType(), typeof(List<>).MakeGenericType(storageElementType), typeof(bool) };
        var method = AccessTools.DeclaredMethod(net, "DistributeAmongStorage", parameters);
        return method?.ReturnType == typeof(void) ? method : null;
    }

    internal static Func<object, T> CompileFieldGetter<T>(FieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var instance = Expression.Parameter(typeof(object), "instance");
        Expression target = field.IsStatic
            ? Expression.Field(null, field)
            : Expression.Field(Expression.Convert(instance, field.DeclaringType!), field);
        var body = target.Type == typeof(T) ? target : Expression.Convert(target, typeof(T));
        return Expression.Lambda<Func<object, T>>(body, instance).Compile();
    }

    internal static Func<object, T> CompilePropertyGetter<T>(PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var getter = property.GetMethod ?? throw new ArgumentException("Property has no getter", nameof(property));
        return CompileGetter<T>(getter);
    }

    internal static Func<object, T> CompileGetter<T>(MethodInfo getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        var instance = Expression.Parameter(typeof(object), "instance");
        Expression call = getter.IsStatic
            ? Expression.Call(getter)
            : Expression.Call(Expression.Convert(instance, getter.DeclaringType!), getter);
        var body = call.Type == typeof(T) ? call : Expression.Convert(call, typeof(T));
        return Expression.Lambda<Func<object, T>>(body, instance).Compile();
    }

    internal static MethodInfo? FindDeclaredMethodInHierarchyByContains(Type root, string nameSubstring)
    {
        foreach (var type in EnumerateNestedTypes(root))
        {
            foreach (var method in type.GetMethods(AccessTools.allDeclared))
            {
                if (!method.IsAbstract && !method.ContainsGenericParameters
                    && method.Name.Contains(nameSubstring, StringComparison.Ordinal))
                    return method;
            }
        }
        return null;
    }

    internal static List<MethodInfo> FindCallersInTypeTree(Type root, MethodBase callee, Predicate<MethodInfo>? filter = null)
    {
        var matches = new List<MethodInfo>();
        if (callee is not MethodInfo targetMethod)
            return matches;
        foreach (var type in EnumerateNestedTypes(root))
        {
            foreach (var method in type.GetMethods(AccessTools.allDeclared))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null)
                    continue;
                if (filter is not null && !filter(method))
                    continue;
                try
                {
                    var instructions = PatchProcessor.GetOriginalInstructions(method);
                    for (var i = 0; i < instructions.Count; i++)
                    {
                        if (instructions[i].Calls(targetMethod))
                        {
                            matches.Add(method);
                            break;
                        }
                    }
                }
                catch (Exception) { /* Skip methods whose IL cannot be inspected */ }
            }
        }
        return matches;
    }

    private static IEnumerable<Type> EnumerateNestedTypes(Type root)
    {
        yield return root;
        foreach (var nested in root.GetNestedTypes(AccessTools.all))
        {
            foreach (var sub in EnumerateNestedTypes(nested))
                yield return sub;
        }
    }

    internal static IEnumerable<CodeInstruction> ObserveSpawn(
        IEnumerable<CodeInstruction> instructions, MethodInfo spawn, MethodInfo observe)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(spawn);
        ArgumentNullException.ThrowIfNull(observe);
        var body = instructions.ToList();
        var call = body.Single(instruction => instruction.Calls(spawn));
        body.InsertRange(body.IndexOf(call) + 1,
            [new CodeInstruction(OpCodes.Dup), new CodeInstruction(OpCodes.Call, observe)]);
        return body;
    }

    internal static IEnumerable<CodeInstruction> Replace(
        IEnumerable<CodeInstruction> instructions, MethodInfo target, MethodInfo replacement, int count, int matchIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replacement);
        var body = instructions.ToList();
        var matches = body.Where(instruction => instruction.Calls(target)).ToArray();
        if (matches.Length != count)
            throw new InvalidOperationException($"Expected {count} calls to {target.DeclaringType?.Name}.{target.Name}");
        if ((uint)matchIndex >= (uint)matches.Length)
            throw new ArgumentOutOfRangeException(nameof(matchIndex));
        matches[matchIndex].opcode = OpCodes.Call;
        matches[matchIndex].operand = replacement;
        return body;
    }
}
