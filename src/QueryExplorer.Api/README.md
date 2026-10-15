This architecture follows two named patterns stacked together:

1. **Clean Architecture** (also called Onion Architecture) — that's the overall layering: 
Domain → Application → Infrastructure → API, where dependencies only point inward,
so the core business logic never depends on database or web-framework code.
2. **Strategy pattern** (with a **Factory**) for the multi-database piece — each database 
gets its own `IQueryProvider` implementation, and a factory hands out the right one at runtime. 
This is what lets you add a new database without touching existing code.

Here's the layer structure:Domain has zero dependencies; Infrastructure and Api depend inward on it, 
never the other way. That's what lets you swap the JSON favorites store for a real database, 
or add a third query provider, without rippling changes outward.

Here's how a single query request actually flows through those layers, showing the strategy/factory 
pattern in action:The controller never knows or cares whether it's 
talking to SQL Server or Oracle — it just asks the factory for "whatever provider matches this 
connection's type" and calls the same `IQueryProvider` interface either way. 
That's the whole trick behind adding a third database later: write one new box on the right, 
register it in `Program.cs`, done.