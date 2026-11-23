using System.Runtime.CompilerServices;

// Test-only visibility for sealed-contract invariants; production callers use
// the public Seal() host entry.
[assembly: InternalsVisibleTo("Gocordis.Runtime.Tests")]
[assembly: InternalsVisibleTo("Gocordis.Product.Tests")]
