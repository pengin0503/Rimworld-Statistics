using System.Reflection.Metadata;

// Shared with tools/PatchTargetCheck, which renders the patch DLL's parameter types the same way as this index.
// System.Reflection.Metadata decodes signatures without loading executable game code.
sealed class TypeRenderer(MetadataReader md) : ISignatureTypeProvider<string, object?>
{
    public string DefinitionName(TypeDefinitionHandle handle)
    {
        var t = md.GetTypeDefinition(handle);
        var name = md.GetString(t.Name);
        if (!t.GetDeclaringType().IsNil)
            return DefinitionName(t.GetDeclaringType()) + "+" + name;
        var ns = md.GetString(t.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private string ReferenceName(TypeReferenceHandle h)
    {
        var t = md.GetTypeReference(h);
        var name = md.GetString(t.Name);
        if (t.ResolutionScope.Kind == HandleKind.TypeReference)
            return ReferenceName((TypeReferenceHandle)t.ResolutionScope) + "+" + name;
        var ns = md.GetString(t.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private string ScopePrefix(EntityHandle handle)
    {
        var h = handle;
        while (h.Kind == HandleKind.TypeReference)
            h = md.GetTypeReference((TypeReferenceHandle)h).ResolutionScope;
        if (h.Kind == HandleKind.AssemblyReference)
        {
            var n = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)h).Name);
            return "[" + n + "]";
        }
        return "[Assembly-CSharp]";
    }

    public string TypeName(EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => "[Assembly-CSharp]" + DefinitionName((TypeDefinitionHandle)h),
        HandleKind.TypeReference => ScopePrefix(h) + ReferenceName((TypeReferenceHandle)h),
        HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(this, null),
        _ => "unknown",
    };

    public string GetArrayType(string e, ArrayShape a) => e + "[]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string generic, System.Collections.Immutable.ImmutableArray<string> args)
        => generic + "<" + string.Join(",", args) + ">";
    public string GetGenericMethodParameter(object? ctx, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? ctx, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => unmodifiedType;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode type) => type switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Int32 => "int32",
        PrimitiveTypeCode.Int64 => "int64",
        PrimitiveTypeCode.UInt32 => "uint32",
        PrimitiveTypeCode.UInt64 => "uint64",
        PrimitiveTypeCode.Single => "float32",
        PrimitiveTypeCode.Double => "float64",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Byte => "uint8",
        PrimitiveTypeCode.SByte => "int8",
        PrimitiveTypeCode.Int16 => "int16",
        PrimitiveTypeCode.UInt16 => "uint16",
        PrimitiveTypeCode.IntPtr => "native int",
        PrimitiveTypeCode.UIntPtr => "native uint",
        _ => type.ToString(),
    };
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle h, byte rawTypeKind)
        => TypeName(h);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle h, byte rawTypeKind)
        => TypeName(h);
    public string GetTypeFromSpecification(MetadataReader reader, object? ctx, TypeSpecificationHandle h, byte rawTypeKind)
        => TypeName(h);
}
