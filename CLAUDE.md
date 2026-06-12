# Jellyfin AceStream Plugin — Engineering Rules

These rules are **mandatory** and apply to every change in this repository.

## SOLID — always

Respect the SOLID principles at all times. Concretely, in this codebase:

- **S — Single Responsibility:** one reason to change per type. Use cases do one thing; adapters only translate; the domain holds no I/O.
- **O — Open/Closed:** extend via new adapters implementing an existing port. Never edit a use case to support a new engine/proxy/transport.
- **L — Liskov Substitution:** any implementation of a port (`ISearchPort`, `IPlaybackPort`, …) must be substitutable without breaking its consumers.
- **I — Interface Segregation:** keep ports small and focused. Search and playback are separate interfaces — no fat interface.
- **D — Dependency Inversion:** application and domain depend on **abstractions (ports)**, never on `HttpClient`, Jellyfin types, or concrete clients. Details point inward.

## Architecture

- **Hexagonal (ports & adapters).** Domain + Application have **zero** dependency on Jellyfin or HTTP.
- **Normalization / single source of truth.** `AceChannel` is the canonical entity; `Infohash` is its identity. Jellyfin DTOs are **projections**, never duplicated state. Map only at the boundaries.
- **Value objects vs entities:** value objects use value equality; entities use identity equality. Keep the distinction explicit.

## Design patterns — for maintainability, not for show

- Apply the design pattern that fits the problem (Adapter, Factory, Strategy, Repository, Decorator, …) when it **reduces** coupling or complexity.
- **Do not force patterns.** A pattern applied without a real need is over-engineering and hurts maintainability as much as missing one. The goal is the simplest design that satisfies SOLID — the pattern is a means, never the goal.
- Prefer composition over inheritance.

## Refactoring — continuous

- Refactor continuously: leave each touched file cleaner than you found it.
- Use named refactorings deliberately (Extract Method, Replace Conditional with Polymorphism, Introduce Parameter Object, Replace Magic Number with Constant, …).
- Eliminate duplication (DRY). Keep functions small and intention-revealing. Remove dead code.
- Treat code smells (long methods, large classes, primitive obsession, feature envy, shotgun surgery) as defects to fix, not tolerate.

## Testing

- **Strict TDD:** red → green → refactor. Write the failing test first.
- Domain and application are unit-tested without network or Docker. Adapters are integration-tested against the real engine/proxy.

## Toolchain

- Target framework: **net9.0** — what Jellyfin 10.11 loads. Both `csproj` files target it.
- Build/test with the system .NET SDK at `/usr/lib64/dotnet`. The machine only ships the .NET 10 runtime, so run tests with `DOTNET_ROLL_FORWARD=Major`.
