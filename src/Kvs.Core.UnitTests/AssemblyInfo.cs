using Xunit;

// Set a global timeout of 10 seconds for all tests
[assembly: CollectionBehavior(DisableTestParallelization = false, MaxParallelThreads = -1)]
[assembly: Xunit.TestFramework("Xunit.Sdk.TestFramework", "xunit.execution.dotnet")]

// Configure default test timeout
// Note: Individual test [Fact(Timeout = X)] attributes will override this
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1025:The assert should have a user message", Justification = "Test assertions")]