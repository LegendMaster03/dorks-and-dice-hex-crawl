using System.Runtime.CompilerServices;

// Trusted tests can inspect provenance notes without exposing internal,
// server-derived fields to untrusted HTTP request deserialization.
[assembly: InternalsVisibleTo("HexCrawl.Application.Tests")]
