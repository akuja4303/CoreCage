using System.Runtime.CompilerServices;

// Let the CoreCage test project drive the ViewModel's internal async operations
// (RefreshStatusAsync / ApplyModeAsync) directly, so tests are deterministic
// instead of racing the fire-and-forget calls the constructor and buttons kick off.
[assembly: InternalsVisibleTo("CoreCage.Tests")]
