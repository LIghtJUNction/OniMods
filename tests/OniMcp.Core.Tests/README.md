# OniMcp core regression checks

Run with the .NET 10 SDK; an ONI installation is unnecessary:

```sh
dotnet run --project tests/OniMcp.Core.Tests/OniMcp.Core.Tests.csproj -c Release
```

The executable links the production MCP types, registries, resource routes and
middleware. It checks JSON-RPC null fields, initialization retries, invalid tool
arguments, visibility and metadata cache isolation, every registered static
resource, and resource-template query overrides. Any failed group exits with code 1.

Game tool factories return recording handlers, and the Unity speech overlay is
replaced by a counter. These checks verify dispatch and protocol behavior; game
assembly compatibility, actual game reads and Unity rendering still require a
full mod build and an in-game smoke test.
