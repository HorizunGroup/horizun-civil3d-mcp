#if NET48
// Metadata markers required by modern C#; no runtime behavior is added.
namespace System.Runtime.CompilerServices
{
    public sealed class IsExternalInit { }
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    public sealed class RequiredMemberAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    public sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;
        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }
}
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, Inherited = false)]
    public sealed class SetsRequiredMembersAttribute : Attribute { }
}
#endif
