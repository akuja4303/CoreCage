using System.Runtime.CompilerServices;

// Let the CoreCage test project exercise internal test-only seams (pure decision helpers, internal
// test constructors) directly, so the pipeline/OS-mutating pieces they wrap never have to run for
// real in a unit test. Mirrors CoreCage.App/InternalsVisibleTo.cs.
[assembly: InternalsVisibleTo("CoreCage.Tests")]
