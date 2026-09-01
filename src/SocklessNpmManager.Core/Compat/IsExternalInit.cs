// Enables `init` accessors and `record` types when targeting netstandard2.0.
// The compiler only needs this type to exist; it is never referenced at runtime.

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif
